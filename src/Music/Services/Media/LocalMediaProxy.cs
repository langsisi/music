using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Remote;

namespace Music.Services.Media;

/// <summary>
/// 本地 HTTP 代理：为 FTP / SMB / WebDAV 曲目登记一个只监听回环地址的地址，
/// 让 VLC 能像播放 HTTP 流一样「边下边播」并拖动进度，而不必先等整文件下载完成。
/// <para>
/// 只处理 GET / HEAD 与单段 Range，服务失败时由调用方回退到整文件下载；
/// 因此它即使在某些平台上不可用，也只是退化成原来的行为。
/// </para>
/// </summary>
public sealed class LocalMediaProxy : IDisposable
{
    /// <summary>请求头读取上限，防止异常客户端把内存撑爆。</summary>
    private const int MaxHeaderBytes = 16 * 1024;

    private readonly ISettingsStore _settings;
    private readonly IRemoteFileClientFactory _clientFactory;
    private readonly ConcurrentDictionary<string, Resource> _resources = new();
    private readonly object _gate = new();

    private TcpListener? _listener;
    private int _port;
    private bool _started;
    private bool _failed;

    public LocalMediaProxy(ISettingsStore settings, IRemoteFileClientFactory clientFactory)
    {
        _settings = settings;
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// 播放代理地址失败后置为 true（见 PlaybackService），此后一律走整文件下载，避免反复重试。
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// 为文件协议曲目登记一个可 Range 读取的资源并返回本地播放地址；
    /// 不满足条件（非文件协议、缺文件大小或远端路径、代理不可用）时返回 null。
    /// </summary>
    public string? TryGetStreamUrl(Track track)
    {
        if (Disabled || track.FileSize <= 0 || string.IsNullOrEmpty(track.RemoteId))
        {
            return null;
        }

        if (track.SourceType is not (MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav))
        {
            return null;
        }

        var config = _settings.Current.Sources.FirstOrDefault(source => source.Id == track.SourceId);
        if (config is null)
        {
            return null;
        }

        if (!TryStart(out var port))
        {
            return null;
        }

        var token = Guid.NewGuid().ToString("N");
        _resources[token] = new Resource(config, track.RemoteId, track.FileSize);

        // 把扩展名带进 URL，VLC 借此判断容器格式（真实请求路径只取 token）。
        return $"http://127.0.0.1:{port}/r/{token}{Path.GetExtension(track.FileName)}";
    }

    public void Dispose()
    {
        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
            // 关闭监听失败可忽略。
        }

        _listener = null;
        _resources.Clear();
    }

    /// <summary>首次使用时启动回环监听；端口交给系统分配，避免与其它程序冲突。</summary>
    private bool TryStart(out int port)
    {
        if (_started)
        {
            port = _port;
            return true;
        }

        if (_failed)
        {
            port = 0;
            return false;
        }

        lock (_gate)
        {
            if (_started)
            {
                port = _port;
                return true;
            }

            if (_failed)
            {
                port = 0;
                return false;
            }

            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();

                _listener = listener;
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _started = true;

                _ = Task.Run(() => AcceptLoopAsync(listener));

                port = _port;
                return true;
            }
            catch (Exception)
            {
                // 平台不允许回环监听等情况：判定为不可用，调用方回退到整文件下载。
                _failed = true;
                port = 0;
                return false;
            }
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 监听被关闭，结束循环。
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;

                await using var stream = client.GetStream();

                if (await ReadRequestAsync(stream).ConfigureAwait(false) is not { } request)
                {
                    return;
                }

                if (!_resources.TryGetValue(request.Token, out var resource))
                {
                    await WriteAsync(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
                        .ConfigureAwait(false);
                    return;
                }

                var (start, end) = ResolveRange(request.Range, resource.Length);

                if (request.IsHead)
                {
                    await WriteAsync(stream, BuildHeaders(200, "OK", resource, start, end)).ConfigureAwait(false);
                    return;
                }

                var partial = request.Range is not null;
                await WriteAsync(
                        stream,
                        BuildHeaders(partial ? 206 : 200, partial ? "Partial Content" : "OK", resource, start, end))
                    .ConfigureAwait(false);

                await CopyRangeAsync(resource, start, end, stream).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // VLC 探测完就挂断是常态，读取/写入失败一律静默忽略。
            }
        }
    }

    private async Task CopyRangeAsync(Resource resource, long start, long end, Stream destination)
    {
        var remaining = end - start + 1;

        await using var client = _clientFactory.Create(resource.Config);
        await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        await using var source = await client
            .OpenReadAsync(resource.RemotePath, start, remaining, CancellationToken.None)
            .ConfigureAwait(false);

        var buffer = new byte[81920];

        while (remaining > 0)
        {
            var want = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, want)).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            remaining -= read;
        }

        await destination.FlushAsync().ConfigureAwait(false);
    }

    private static string BuildHeaders(int status, string reason, Resource resource, long start, long end)
    {
        var builder = new StringBuilder();
        builder.Append($"HTTP/1.1 {status} {reason}\r\n");
        builder.Append($"Content-Type: {resource.ContentType}\r\n");
        builder.Append($"Content-Length: {end - start + 1}\r\n");
        builder.Append("Accept-Ranges: bytes\r\n");

        if (status == 206)
        {
            builder.Append($"Content-Range: bytes {start}-{end}/{resource.Length}\r\n");
        }

        builder.Append("Connection: close\r\n\r\n");
        return builder.ToString();
    }

    /// <summary>把 <c>Range</c> 头换算成闭区间；缺省或无法解析时返回整个文件。</summary>
    private static (long Start, long End) ResolveRange(string? rangeHeader, long length)
    {
        var last = Math.Max(0, length - 1);

        if (string.IsNullOrEmpty(rangeHeader)
            || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return (0, last);
        }

        // 只支持单段 Range；多段请求退化为整文件，VLC 不会主动请求多段。
        var spec = rangeHeader["bytes=".Length..].Split(',')[0].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return (0, last);
        }

        var startText = spec[..dash];
        var endText = spec[(dash + 1)..];

        long start;
        long end;

        if (startText.Length == 0)
        {
            // bytes=-N：最后 N 个字节。
            if (!long.TryParse(endText, out var suffix) || suffix <= 0)
            {
                return (0, last);
            }

            start = Math.Max(0, length - suffix);
            end = last;
        }
        else
        {
            if (!long.TryParse(startText, out start))
            {
                return (0, last);
            }

            end = long.TryParse(endText, out var parsed) ? parsed : last;
        }

        start = Math.Clamp(start, 0, last);
        end = Math.Clamp(end, start, last);
        return (start, end);
    }

    private static async Task WriteAsync(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }

    /// <summary>读取到请求头结束（<c>\r\n\r\n</c>）为止，只关心方法、token 与 Range。</summary>
    private static async Task<Request?> ReadRequestAsync(Stream stream)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        var headerEnd = -1;

        while (text.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
            headerEnd = text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                break;
            }
        }

        if (headerEnd < 0)
        {
            return null;
        }

        var lines = text.ToString()[..headerEnd].Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2)
        {
            return null;
        }

        string? range = null;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
            {
                range = lines[i]["Range:".Length..].Trim();
                break;
            }
        }

        var isHead = parts[0].Equals("HEAD", StringComparison.OrdinalIgnoreCase);
        return new Request(isHead, ExtractToken(parts[1]), range);
    }

    /// <summary>从 <c>/r/{token}.mp3?x=1</c> 中取出 token，同时兼容绝对形式 <c>http://host/r/{token}.mp3</c>。</summary>
    private static string ExtractToken(string target)
    {
        const string prefix = "/r/";

        var path = target;

        var query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        var scheme = path.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var slash = path.IndexOf('/', scheme + 3);
            path = slash >= 0 ? path[slash..] : "/";
        }

        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var rest = path[prefix.Length..];
        var dot = rest.IndexOf('.');
        return dot >= 0 ? rest[..dot] : rest;
    }

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".m4a" or ".aac" or ".mp4" => "audio/mp4",
        ".wav" => "audio/wav",
        ".ogg" or ".opus" => "audio/ogg",
        ".wma" => "audio/x-ms-wma",
        ".ape" => "audio/x-ape",
        _ => "application/octet-stream",
    };

    private readonly record struct Request(bool IsHead, string Token, string? Range);

    private sealed record Resource(MusicSourceConfig Config, string RemotePath, long Length)
    {
        public string ContentType => GuessContentType(RemotePath);
    }
}
