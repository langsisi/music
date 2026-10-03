using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Sources;

/// <summary>Navidrome / Subsonic 交互失败（认证、网络、协议错误）。</summary>
public sealed class NavidromeException : Exception
{
    public NavidromeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>从服务器取回的一首曲目（尚未映射为本地 <see cref="Track"/>）。</summary>
public sealed record NavidromeSong(
    string Id,
    string Title,
    string Artist,
    string Album,
    string AlbumId,
    string? CoverArtId,
    string Genre,
    double Duration,
    int TrackNumber,
    int Year,
    long Size,
    string? Suffix);

/// <summary>
/// Subsonic REST 客户端（Navidrome 兼容）。认证方式为 <c>token = md5(密码 + salt)</c>，
/// 每个实例使用一个随机 salt，因此实例本身是「一次会话」级别的对象。
/// </summary>
public sealed class NavidromeClient
{
    private const string ApiVersion = "1.16.1";
    private const string ClientName = "MusicAvalonia";

    /// <summary>getAlbumList2 单页上限（Subsonic 规范最大值）。</summary>
    private const int AlbumPageSize = 500;

    private static readonly int AlbumConcurrency = Math.Clamp(Environment.ProcessorCount, 2, 6);

    private static readonly string[] CoverExtensions = [".jpg", ".png", ".webp", ".gif", ".bmp"];

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _authQuery;

    public NavidromeClient(NavidromeSourceConfig config, HttpClient httpClient)
    {
        _httpClient = httpClient;
        _baseUrl = NormalizeBaseUrl(config.BaseUrl);

        var salt = Guid.NewGuid().ToString("N")[..12];
        var token = Convert
            .ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(config.Password + salt)))
            .ToLowerInvariant();

        _authQuery = string.Join(
            '&',
            $"u={Uri.EscapeDataString(config.UserName)}",
            $"t={token}",
            $"s={salt}",
            $"v={ApiVersion}",
            $"c={Uri.EscapeDataString(ClientName)}",
            "f=json");
    }

    /// <summary>可交给播放器直接播放的流地址（带认证参数）。</summary>
    public string BuildStreamUrl(string songId)
        => BuildUrl("stream", $"id={Uri.EscapeDataString(songId)}&format=raw");

    /// <summary>探活；失败时抛出 <see cref="NavidromeException"/> 并带上可读原因。</summary>
    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        // 不抛异常即视为连通。
        using var call = await CallAsync("ping", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 拉取全部专辑的曲目。专辑详情并发获取，逐个专辑回报进度。
    /// </summary>
    public async Task<IReadOnlyList<NavidromeSong>> GetAllSongsAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ScanProgress("正在获取专辑列表", 0, 0));

        var albums = await GetAlbumListAsync(cancellationToken).ConfigureAwait(false);
        if (albums.Count == 0)
        {
            progress?.Report(new ScanProgress("同步完成", 0, 0));
            return [];
        }

        var results = new List<NavidromeSong>[albums.Count];
        var completed = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, albums.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = AlbumConcurrency,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                var album = albums[index];
                results[index] = await GetAlbumSongsAsync(album.Id, album.CoverArtId, token)
                    .ConfigureAwait(false);
                progress?.Report(new ScanProgress(
                    "正在获取专辑曲目",
                    Interlocked.Increment(ref completed),
                    albums.Count));
            }).ConfigureAwait(false);

        var songs = new List<NavidromeSong>(results.Sum(result => result.Count));
        foreach (var albumSongs in results)
        {
            songs.AddRange(albumSongs);
        }

        progress?.Report(new ScanProgress("同步完成", songs.Count, songs.Count));
        return songs;
    }

    /// <summary>下载专辑封面到本地封面目录并返回路径；已有则直接复用，失败返回 null。</summary>
    public async Task<string?> EnsureCoverAsync(
        string albumId,
        string? coverArtId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(coverArtId))
        {
            return null;
        }

        AppPaths.EnsureCreated();
        var baseName = Path.Combine(AppPaths.CoversDir, "navidrome_" + ToSafeName(albumId));

        foreach (var extension in CoverExtensions)
        {
            var existing = baseName + extension;
            if (File.Exists(existing) && new FileInfo(existing).Length > 0)
            {
                return existing;
            }
        }

        try
        {
            var url = BuildUrl("getCoverArt", $"id={Uri.EscapeDataString(coverArtId)}&size=600");
            using var response = await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var path = baseName + ResolveImageExtension(response.Content.Headers.ContentType?.MediaType);

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var destination = File.Create(path);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 封面只是锦上添花，拿不到不影响曲目同步与播放。
            return null;
        }
    }

    /// <summary>按歌曲 Id 取歌词并转成 LRC 文本；服务器不支持或无歌词时返回 null。</summary>
    public async Task<string?> GetLyricsAsync(string songId, CancellationToken cancellationToken = default)
    {
        using var call = await CallAsync(
            "getLyricsBySongId",
            $"id={Uri.EscapeDataString(songId)}",
            cancellationToken).ConfigureAwait(false);

        var structuredList = Property(Property(call.Root, "lyricsList"), "structuredLyrics");
        if (structuredList.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var lyrics in structuredList.EnumerateArray())
        {
            var lines = Property(lyrics, "line");
            if (lines.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var parsed = new List<(double Start, string Value)>();
            foreach (var line in lines.EnumerateArray())
            {
                var value = GetString(line, "value");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // OpenSubsonic 的 start 单位是毫秒。
                    parsed.Add((GetDouble(line, "start") / 1000, value));
                }
            }

            // 时间戳全为 0 说明是没有时间轴的纯文本歌词，本播放器的滚动歌词用不上。
            if (parsed.Count == 0 || parsed.TrueForAll(item => item.Start <= 0))
            {
                continue;
            }

            var builder = new StringBuilder();
            foreach (var (start, value) in parsed)
            {
                builder.Append('[').Append(FormatTimestamp(start)).Append(']').AppendLine(value);
            }

            return builder.ToString();
        }

        return null;
    }

    private async Task<List<(string Id, string? CoverArtId)>> GetAlbumListAsync(CancellationToken cancellationToken)
    {
        var albums = new List<(string Id, string? CoverArtId)>();
        var offset = 0;

        while (true)
        {
            using var call = await CallAsync(
                "getAlbumList2",
                $"type=alphabeticalByArtist&size={AlbumPageSize}&offset={offset}",
                cancellationToken).ConfigureAwait(false);

            var page = Property(Property(call.Root, "albumList2"), "album");
            if (page.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var count = 0;
            foreach (var album in page.EnumerateArray())
            {
                var id = GetString(album, "id");
                if (!string.IsNullOrEmpty(id))
                {
                    albums.Add((id, GetString(album, "coverArt")));
                    count++;
                }
            }

            if (count < AlbumPageSize)
            {
                break;
            }

            offset += AlbumPageSize;
        }

        return albums;
    }

    private async Task<List<NavidromeSong>> GetAlbumSongsAsync(
        string albumId,
        string? albumCoverArtId,
        CancellationToken cancellationToken)
    {
        using var call = await CallAsync(
            "getAlbum",
            $"id={Uri.EscapeDataString(albumId)}",
            cancellationToken).ConfigureAwait(false);

        var album = Property(call.Root, "album");
        var albumName = GetString(album, "name") ?? string.Empty;
        var albumArtist = GetString(album, "artist") ?? string.Empty;
        var resolvedAlbumId = GetString(album, "id") ?? albumId;
        var coverArtId = GetString(album, "coverArt") ?? albumCoverArtId;

        var songs = new List<NavidromeSong>();
        var songArray = Property(album, "song");
        if (songArray.ValueKind != JsonValueKind.Array)
        {
            return songs;
        }

        foreach (var song in songArray.EnumerateArray())
        {
            var id = GetString(song, "id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            songs.Add(new NavidromeSong(
                Id: id,
                Title: GetString(song, "title") ?? string.Empty,
                Artist: GetString(song, "artist") ?? albumArtist,
                Album: GetString(song, "album") ?? albumName,
                AlbumId: resolvedAlbumId,
                CoverArtId: GetString(song, "coverArt") ?? coverArtId,
                Genre: GetString(song, "genre") ?? string.Empty,
                Duration: GetDouble(song, "duration"),
                TrackNumber: GetInt(song, "track"),
                Year: GetInt(song, "year"),
                Size: GetLong(song, "size"),
                Suffix: GetString(song, "suffix")));
        }

        return songs;
    }

    /// <summary>
    /// 调用一个 <c>/rest/{method}.view</c> 接口。
    /// 返回的 <see cref="SubsonicCall.Root"/> 已经指向 <c>subsonic-response</c> 节点，调用方直接取字段即可。
    /// </summary>
    private async Task<SubsonicCall> CallAsync(
        string method,
        string? extraQuery,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(method, extraQuery);

        try
        {
            await using var stream = await _httpClient
                .GetStreamAsync(url, cancellationToken)
                .ConfigureAwait(false);

            var document = await JsonDocument
                .ParseAsync(stream, default, cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("subsonic-response", out var response))
            {
                document.Dispose();
                throw new NavidromeException("服务器返回的不是 Subsonic 格式的响应。");
            }

            var status = GetString(response, "status");
            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                var message = DescribeError(response);
                document.Dispose();
                throw new NavidromeException(message);
            }

            return new SubsonicCall(document, response);
        }
        catch (HttpRequestException ex)
        {
            throw new NavidromeException($"无法连接服务器：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NavidromeException("连接服务器超时。", ex);
        }
        catch (JsonException ex)
        {
            throw new NavidromeException("服务器返回的响应无法解析。", ex);
        }
    }

    /// <summary>一次 Subsonic 调用的结果：负责释放底层文档。</summary>
    private sealed class SubsonicCall(JsonDocument document, JsonElement root) : IDisposable
    {
        /// <summary><c>subsonic-response</c> 节点。</summary>
        public JsonElement Root { get; } = root;

        public void Dispose() => document.Dispose();
    }

    private static string DescribeError(JsonElement response)
    {
        var error = Property(response, "error");
        var code = GetInt(error, "code");
        var message = GetString(error, "message");

        var reason = code switch
        {
            40 => "用户名或密码错误。",
            41 => "服务器不支持该功能（可能版本过低）。",
            43 => "登录被拒绝：账号已被禁用或不允许对此客户端登录。",
            44 => "登录被拒绝：请检查用户名与密码。",
            50 => "当前账号没有该操作的权限。",
            60 => "服务器数据库错误。",
            70 => "请求的数据不存在。",
            _ => null,
        };

        if (reason is null)
        {
            return string.IsNullOrWhiteSpace(message)
                ? $"服务器返回错误（代码 {code}）。"
                : $"服务器返回错误（代码 {code}）：{message}";
        }

        return string.IsNullOrWhiteSpace(message) ? reason : $"{reason}（{message}）";
    }

    private string BuildUrl(string method, string? extraQuery)
    {
        var url = $"{_baseUrl}/rest/{method}.view?{_authQuery}";
        return string.IsNullOrEmpty(extraQuery) ? url : $"{url}&{extraQuery}";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "http://" + trimmed;
    }

    /// <summary>取一层对象属性；不存在或类型不符时返回 <see cref="JsonValueKind.Undefined"/>。</summary>
    private static JsonElement Property(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : default;

    private static string? GetString(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static double GetDouble(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;
    }

    private static int GetInt(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

    private static long GetLong(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
    }

    /// <summary>把秒数格式化成 LRC 时间戳，如 <c>01:23.45</c>。</summary>
    private static string FormatTimestamp(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}.{time.Milliseconds / 10:D2}";
    }

    private static string ResolveImageExtension(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        _ => ".jpg",
    };

    /// <summary>Subsonic 的 Id 可能含 <c>/</c> 等文件名非法字符（如 <c>al_1/2</c>）。</summary>
    private static string ToSafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        return builder.ToString();
    }
}
