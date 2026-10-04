using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Remote;

namespace Music.Services.Metadata;

/// <summary>写回音源的结果。<see cref="Message"/> 在失败时给出可读原因。</summary>
public enum WriteBackStatus
{
    /// <summary>未写回（开关关闭 / 音源不支持 / 没有可写内容）。</summary>
    Skipped,

    /// <summary>写回成功。</summary>
    Succeeded,

    /// <summary>写回失败，<c>Message</c> 为原因。</summary>
    Failed,
}

public sealed record WriteBackResult(WriteBackStatus Status, string Message)
{
    public static readonly WriteBackResult Skipped = new(WriteBackStatus.Skipped, string.Empty);

    public static WriteBackResult Ok() => new(WriteBackStatus.Succeeded, string.Empty);

    public static WriteBackResult Fail(string message) => new(WriteBackStatus.Failed, message);
}

/// <summary>
/// 把刮削得到的封面 / 歌词写回音源本身：
/// <list type="bullet">
/// <item>本地：直接改写音频标签，并在同目录写一个同名 <c>.lrc</c>。</item>
/// <item>FTP/SMB/WebDAV：下载原文件到缓存临时文件 → 改写标签 → 覆盖上传，同时上传同名 <c>.lrc</c>。</item>
/// <item>Navidrome（接口不支持上传）与在线曲目（无实体文件）：跳过。</item>
/// </list>
/// 是否启用由 <see cref="AppSettings.ScrapeWriteBack"/> 控制；全程不抛异常，超时降级为失败文案。
/// </summary>
public sealed class SourceWriteBackService
{
    /// <summary>写回兜底超时，避免界面永久停在「正在应用…」。</summary>
    private static readonly TimeSpan WriteBackTimeout = TimeSpan.FromSeconds(120);

    private readonly ISettingsStore _settings;
    private readonly IRemoteFileClientFactory _remoteClientFactory;

    public SourceWriteBackService(ISettingsStore settings, IRemoteFileClientFactory remoteClientFactory)
    {
        _settings = settings;
        _remoteClientFactory = remoteClientFactory;
    }

    public async Task<WriteBackResult> WriteBackAsync(
        Track track,
        byte[]? coverBytes,
        string? lyrics,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.ScrapeWriteBack)
        {
            return WriteBackResult.Skipped;
        }

        if (coverBytes is null && string.IsNullOrWhiteSpace(lyrics))
        {
            return WriteBackResult.Skipped;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WriteBackTimeout);

            return track.SourceType switch
            {
                MusicSourceType.Local => await WriteLocalAsync(track, coverBytes, lyrics, timeout.Token)
                    .ConfigureAwait(false),

                MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav =>
                    await WriteRemoteAsync(track, coverBytes, lyrics, timeout.Token).ConfigureAwait(false),

                _ => WriteBackResult.Skipped,
            };
        }
        catch (Exception ex)
        {
            return WriteBackResult.Fail(ex.Message);
        }
    }

    private static async Task<WriteBackResult> WriteLocalAsync(
        Track track,
        byte[]? coverBytes,
        string? lyrics,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(track.Path))
        {
            return WriteBackResult.Fail("找不到本地音频文件。");
        }

        var path = track.Path;
        var tagOk = await Task.Run(
                () => AudioTagWriter.Write(path, coverBytes, lyrics),
                cancellationToken)
            .ConfigureAwait(false);

        var lrcOk = await TryWriteSidecarLyricsAsync(
                Path.ChangeExtension(path, ".lrc"), lyrics, cancellationToken)
            .ConfigureAwait(false);

        return tagOk || lrcOk
            ? WriteBackResult.Ok()
            : WriteBackResult.Fail("写入音频标签失败。");
    }

    private async Task<WriteBackResult> WriteRemoteAsync(
        Track track,
        byte[]? coverBytes,
        string? lyrics,
        CancellationToken cancellationToken)
    {
        var config = _settings.Current.Sources
            .FirstOrDefault(source => source.Id == track.SourceId);
        if (config is null)
        {
            return WriteBackResult.Fail("找不到音源配置。");
        }

        var remotePath = track.RemoteId;
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return WriteBackResult.Fail("曲目缺少远端路径。");
        }

        AppPaths.EnsureCreated();
        var extension = Path.GetExtension(remotePath);
        var tempPath = Path.Combine(AppPaths.CacheDir, Guid.NewGuid().ToString("N") + extension);

        try
        {
            await using var client = _remoteClientFactory.Create(config);

            await using (var destination = File.Create(tempPath))
            {
                await client
                    .DownloadAsync(remotePath, destination, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            var tagOk = await Task.Run(
                    () => AudioTagWriter.Write(tempPath, coverBytes, lyrics),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!tagOk)
            {
                return WriteBackResult.Fail("写入音频标签失败。");
            }

            await using (var audio = File.OpenRead(tempPath))
            {
                await client
                    .UploadAsync(remotePath, audio, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(lyrics))
            {
                var lrcRemote = Path.ChangeExtension(remotePath, ".lrc");
                using var lrcStream = new MemoryStream(Encoding.UTF8.GetBytes(lyrics));
                await client
                    .UploadAsync(lrcRemote, lrcStream, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            return WriteBackResult.Ok();
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static async Task<bool> TryWriteSidecarLyricsAsync(
        string lrcPath,
        string? lyrics,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lyrics))
        {
            return false;
        }

        try
        {
            await File.WriteAllTextAsync(lrcPath, lyrics, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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
