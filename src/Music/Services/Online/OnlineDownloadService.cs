using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Metadata;
using Music.Services.Remote;

namespace Music.Services.Online;

/// <summary>一个可选的下载目标，对应「设置 → 音源」里的一个音源。</summary>
public sealed record OnlineDownloadTarget(string Id, string Name, MusicSourceConfig Config);

/// <summary>
/// 把在线曲目下载到指定目标：
/// 先按音质取流并下载音频，再补齐封面 / 歌词（写入标签 + 同名 .lrc），最后落到本地文件夹或 FTP/SMB/WebDAV。
/// Navidrome 的 Subsonic 接口不支持上传，因此不做为下载目标（可在设置里配置指向其音乐目录的 FTP/SMB/WebDAV 音源）。
/// </summary>
public sealed class OnlineDownloadService
{
    private readonly GdMusicClient _client;
    private readonly HttpClient _httpClient;
    private readonly IRemoteFileClientFactory _remoteClientFactory;
    private readonly ISettingsStore _settings;

    public OnlineDownloadService(
        GdMusicClient client,
        HttpClient httpClient,
        IRemoteFileClientFactory remoteClientFactory,
        ISettingsStore settings)
    {
        _client = client;
        _httpClient = httpClient;
        _remoteClientFactory = remoteClientFactory;
        _settings = settings;
    }

    /// <summary>当前可用于下载的目标：已配置且可用的本地文件夹 / FTP / SMB / WebDAV 音源。</summary>
    public IReadOnlyList<OnlineDownloadTarget> GetTargets()
    {
        var targets = new List<OnlineDownloadTarget>();

        foreach (var source in _settings.Current.Sources)
        {
            if (!source.Enabled)
            {
                continue;
            }

            switch (source)
            {
                case LocalSourceConfig local when local.Folders.Any(Directory.Exists):
                    targets.Add(new OnlineDownloadTarget(source.Id, source.DisplayName, source));
                    break;

                case FtpSourceConfig ftp when !string.IsNullOrWhiteSpace(ftp.Host):
                    targets.Add(new OnlineDownloadTarget(source.Id, source.DisplayName, source));
                    break;

                case SmbSourceConfig smb when !string.IsNullOrWhiteSpace(smb.Host)
                                              && !string.IsNullOrWhiteSpace(smb.ShareName):
                    targets.Add(new OnlineDownloadTarget(source.Id, source.DisplayName, source));
                    break;

                case WebDavSourceConfig webDav when !string.IsNullOrWhiteSpace(webDav.BaseUrl):
                    targets.Add(new OnlineDownloadTarget(source.Id, source.DisplayName, source));
                    break;
            }
        }

        return targets;
    }

    /// <summary>下载并写入目标，返回可读的落盘位置。</summary>
    public async Task<string> DownloadAsync(
        OnlineTrack track,
        OnlineDownloadTarget target,
        int bitrate,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var streamUrl = await _client
            .GetStreamUrlAsync(track.Source, track.UrlId, bitrate, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new GdMusicException($"「{track.DisplayTitle}」暂时拿不到播放地址（可能受版权或会员限制）。");

        var extension = ResolveExtension(streamUrl);
        var fileName = BuildFileName(track, extension);

        Directory.CreateDirectory(AppPaths.CacheDir);
        var tempPath = Path.Combine(AppPaths.CacheDir, Guid.NewGuid().ToString("N") + extension);

        try
        {
            await DownloadToFileAsync(streamUrl, tempPath, progress, cancellationToken).ConfigureAwait(false);

            var coverBytes = await _client
                .GetCoverBytesAsync(track.Source, track.PicId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var lyric = await _client
                .GetLyricAsync(track.Source, track.LyricId, cancellationToken)
                .ConfigureAwait(false);

            AudioTagWriter.Write(
                tempPath,
                coverBytes,
                lyric,
                track.DisplayTitle,
                track.DisplayArtist,
                track.Album);

            return await SaveToTargetAsync(target, tempPath, fileName, lyric, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task<string> SaveToTargetAsync(
        OnlineDownloadTarget target,
        string tempAudioPath,
        string fileName,
        string? lyric,
        CancellationToken cancellationToken)
    {
        switch (target.Config)
        {
            case LocalSourceConfig local:
                var folder = local.Folders.FirstOrDefault(Directory.Exists)
                    ?? throw new InvalidOperationException("该本地音源还没有可用的文件夹。");
                return SaveToFolder(folder, tempAudioPath, fileName, lyric);

            case FtpSourceConfig:
            case SmbSourceConfig:
            case WebDavSourceConfig:
                return await UploadToRemoteAsync(target.Config, tempAudioPath, fileName, lyric, cancellationToken)
                    .ConfigureAwait(false);

            case NavidromeSourceConfig:
                throw new NotSupportedException(
                    "Navidrome 的接口不支持上传音乐。请配置一个指向其音乐目录的 FTP/SMB/WebDAV 音源作为下载目标。");

            default:
                throw new NotSupportedException($"暂不支持的下载目标：{target.Name}");
        }
    }

    private static string SaveToFolder(string folder, string tempAudioPath, string fileName, string? lyric)
    {
        Directory.CreateDirectory(folder);

        var destination = ResolveAvailablePath(
            folder,
            Path.GetFileNameWithoutExtension(fileName),
            Path.GetExtension(fileName));

        File.Copy(tempAudioPath, destination, overwrite: false);

        if (!string.IsNullOrWhiteSpace(lyric))
        {
            File.WriteAllText(Path.ChangeExtension(destination, ".lrc"), lyric);
        }

        return destination;
    }

    private async Task<string> UploadToRemoteAsync(
        MusicSourceConfig config,
        string tempAudioPath,
        string fileName,
        string? lyric,
        CancellationToken cancellationToken)
    {
        var (remoteDir, displayPrefix) = ResolveRemoteTarget(config);
        var remoteAudio = $"{remoteDir}/{fileName}";

        await using var client = _remoteClientFactory.Create(config);

        await using (var audio = File.OpenRead(tempAudioPath))
        {
            await client.UploadAsync(remoteAudio, audio, null, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(lyric))
        {
            var remoteLyric = $"{remoteDir}/{Path.ChangeExtension(fileName, ".lrc")}";
            using var lyricStream = new MemoryStream(Encoding.UTF8.GetBytes(lyric));
            await client.UploadAsync(remoteLyric, lyricStream, null, cancellationToken).ConfigureAwait(false);
        }

        return $"{displayPrefix}{remoteAudio}";
    }

    /// <summary>远端根目录（'/' 开头，无尾斜杠）与展示地址前缀。</summary>
    private static (string RemoteDir, string DisplayPrefix) ResolveRemoteTarget(MusicSourceConfig config)
    {
        var root = config switch
        {
            FtpSourceConfig ftp => ftp.RootPath,
            SmbSourceConfig smb => smb.RootPath,
            WebDavSourceConfig webDav => webDav.RootPath,
            _ => "/",
        };

        var remoteDir = string.IsNullOrWhiteSpace(root) || root == "/"
            ? "/"
            : "/" + root.Trim('/');

        var displayPrefix = config switch
        {
            FtpSourceConfig ftp => $"ftp://{ftp.Host}:{ftp.Port}",
            SmbSourceConfig smb => $"smb://{smb.Host}/{smb.ShareName}",
            WebDavSourceConfig webDav => webDav.BaseUrl.TrimEnd('/'),
            _ => string.Empty,
        };

        return (remoteDir, displayPrefix);
    }

    private async Task DownloadToFileAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long received = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            if (total > 0)
            {
                progress?.Report((double)received / total);
            }
        }

        progress?.Report(1);
    }

    /// <summary>从流地址推断扩展名，取不到时退回 .mp3。</summary>
    private static string ResolveExtension(string url)
    {
        string extension;
        try
        {
            extension = Path.GetExtension(new Uri(url).AbsolutePath);
        }
        catch (UriFormatException)
        {
            extension = string.Empty;
        }

        return !string.IsNullOrEmpty(extension) && AudioFileTypes.IsAudioFile("track" + extension)
            ? extension
            : ".mp3";
    }

    private static string BuildFileName(OnlineTrack track, string extension)
    {
        var baseName = Sanitize($"{track.DisplayArtist} - {track.DisplayTitle}");
        if (string.IsNullOrWhiteSpace(baseName) || baseName == "-")
        {
            baseName = Sanitize(track.Key);
        }

        return baseName + extension;
    }

    /// <summary>重名时自动加序号，避免覆盖已有文件。</summary>
    private static string ResolveAvailablePath(string folder, string baseName, string extension)
    {
        var destination = Path.Combine(folder, baseName + extension);
        for (var index = 2; File.Exists(destination); index++)
        {
            destination = Path.Combine(folder, $"{baseName} ({index}){extension}");
        }

        return destination;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 临时文件清理失败可忽略。
        }
    }
}