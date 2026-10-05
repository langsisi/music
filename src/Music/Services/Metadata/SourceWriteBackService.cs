using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    /// <summary>写回成功且远端文件已按标题重命名：<see cref="NewRemotePath"/> 为新路径，<see cref="NewDisplayPath"/> 为展示地址。</summary>
    public static WriteBackResult Renamed(string newRemotePath, string newDisplayPath, string? message = null)
        => new(WriteBackStatus.Succeeded, message ?? string.Empty)
        {
            NewRemotePath = newRemotePath,
            NewDisplayPath = newDisplayPath,
        };

    /// <summary>远端文件重命名后的新路径（未重命名时为 null）。</summary>
    public string? NewRemotePath { get; init; }

    /// <summary>重命名后用于界面展示的地址（未重命名时为 null）。</summary>
    public string? NewDisplayPath { get; init; }
}

/// <summary>
/// 把刮削得到或用户编辑的封面 / 歌词 / 元数据（标题、歌手、专辑）写回音源本身：
/// <list type="bullet">
/// <item>本地：直接改写音频标签，并在同目录写一个同名 <c>.lrc</c>。</item>
/// <item>FTP/SMB/WebDAV：下载原文件到缓存临时文件 → 改写标签 → 覆盖上传，同时上传同名 <c>.lrc</c>。</item>
/// <item>Navidrome（接口不支持上传）与在线曲目（无实体文件）：跳过。</item>
/// </list>
/// 全程不抛异常，超时降级为失败文案。
/// </summary>
public sealed class SourceWriteBackService
{
    /// <summary>写回兜底超时，避免界面永久停在「正在应用…」。</summary>
    private static readonly TimeSpan WriteBackTimeout = TimeSpan.FromSeconds(120);

    private readonly ISettingsStore _settings;
    private readonly IRemoteFileClientFactory _remoteClientFactory;

    /// <summary>复制式改名回退路径里删除失败的远端旧文件登记，等下一次切歌（播放代理释放句柄）后自动重试。</summary>
    private readonly ConcurrentQueue<PendingRemoteDelete> _pendingDeletes = new();

    private readonly record struct PendingRemoteDelete(string SourceId, string RemotePath);

    public SourceWriteBackService(ISettingsStore settings, IRemoteFileClientFactory remoteClientFactory)
    {
        _settings = settings;
        _remoteClientFactory = remoteClientFactory;
    }

    /// <summary>
    /// 写回封面 / 歌词 / 元数据到音源。<paramref name="title"/> 等为 null 或空时保持文件里的原值不动。
    /// <paramref name="progress"/> 用于回报阶段文案（远端是整文件往返，耗时较长，需要让用户看到在动）。
    /// </summary>
    public async Task<WriteBackResult> WriteBackAsync(
        Track track,
        byte[]? coverBytes,
        string? lyrics,
        string? title = null,
        string? artist = null,
        string? album = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (coverBytes is null
            && string.IsNullOrWhiteSpace(lyrics)
            && string.IsNullOrWhiteSpace(title)
            && string.IsNullOrWhiteSpace(artist)
            && string.IsNullOrWhiteSpace(album))
        {
            return WriteBackResult.Skipped;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WriteBackTimeout);

            return track.SourceType switch
            {
                MusicSourceType.Local => await WriteLocalAsync(
                        track, coverBytes, lyrics, title, artist, album, progress, timeout.Token)
                    .ConfigureAwait(false),

                MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav =>
                    await WriteRemoteAsync(
                            track, coverBytes, lyrics, title, artist, album, progress, timeout.Token)
                        .ConfigureAwait(false),

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
        string? title,
        string? artist,
        string? album,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(track.Path))
        {
            return WriteBackResult.Fail("找不到本地音频文件。");
        }

        progress?.Report("正在写入本地文件标签…");

        var path = track.Path;
        var tagOk = await Task.Run(
                () => AudioTagWriter.Write(path, coverBytes, lyrics, title, artist, album, track.Year),
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
        string? title,
        string? artist,
        string? album,
        IProgress<string>? progress,
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

            // 保存信息后远端文件名与标题保持一致；先确认目标名可用（不存在同名其他文件）。
            var targetPath = await ResolveRenameTargetAsync(
                client, remotePath, title, progress, cancellationToken);
            var renamed = targetPath is not null && !string.Equals(targetPath, remotePath, StringComparison.OrdinalIgnoreCase);

            // 远端写回是「整文件下载 → 改标签 → 覆盖上传」，文件越大越慢，
            // 这里按阶段回报进度，避免界面一直停在「正在应用…」像是卡死。
            const string downloadStage = "正在下载原文件";
            progress?.Report($"{downloadStage}… 0%");
            await using (var destination = File.Create(tempPath))
            {
                await client
                    .DownloadAsync(
                        remotePath,
                        destination,
                        StageProgress(progress, downloadStage, 0, 50),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report("正在写入标签…");
            var tagOk = await Task.Run(
                    () => AudioTagWriter.Write(tempPath, coverBytes, lyrics, title, artist, album, track.Year),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!tagOk)
            {
                return WriteBackResult.Fail("写入音频标签失败。");
            }

            const string uploadStage = "正在上传到音源";
            progress?.Report($"{uploadStage}… 50%");

            var moved = false;
            if (renamed)
            {
                // 改名流程：先把打好标签的内容覆盖回原文件，再让服务器原地改名（WebDAV MOVE）——
                // 旧文件由服务器直接重命名消失，全程不需要删除，正被播放占用也不受影响。
                await UploadAudioAsync(client, remotePath, tempPath, progress, uploadStage, cancellationToken)
                    .ConfigureAwait(false);

                progress?.Report("正在重命名远端文件…");
                moved = await TryMoveRemoteAsync(client, remotePath, targetPath!, cancellationToken)
                    .ConfigureAwait(false);

                if (!moved)
                {
                    // 服务器不支持 MOVE 时退回复制式改名：再传一份新文件名；标签一定写回，改名不成不算保存失败。
                    try
                    {
                        await UploadAudioAsync(client, targetPath!, tempPath, progress, uploadStage, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        await TryDeleteRemoteQuietlyAsync(client, targetPath!, cancellationToken)
                            .ConfigureAwait(false);

                        await UploadAudioAsync(client, remotePath, tempPath, progress, uploadStage, cancellationToken)
                            .ConfigureAwait(false);

                        return new WriteBackResult(
                            WriteBackStatus.Succeeded, "按标题重命名失败，元数据已写回原文件。");
                    }
                }
            }
            else
            {
                await UploadAudioAsync(client, remotePath, tempPath, progress, uploadStage, cancellationToken)
                    .ConfigureAwait(false);
            }

            var lrcOld = Path.ChangeExtension(remotePath, ".lrc");
            var lrcRemote = Path.ChangeExtension(renamed ? targetPath! : remotePath, ".lrc");
            if (!string.IsNullOrWhiteSpace(lyrics))
            {
                progress?.Report("正在上传歌词…");
                using var lrcStream = new MemoryStream(Encoding.UTF8.GetBytes(lyrics));
                await client
                    .UploadAsync(lrcRemote, lrcStream, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (renamed)
            {
                // 没有新歌词也要让歌词文件跟着改名，其他设备同步才不会丢歌词。
                if (moved)
                {
                    // 服务端改名能力可用：旧 .lrc 直接 MOVE 到新名；没有旧歌词时改名失败，静默忽略。
                    await TryMoveRemoteAsync(client, lrcOld, lrcRemote, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await TryMoveRemoteLyricsAsync(client, remotePath, lrcRemote, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (!renamed)
            {
                return WriteBackResult.Ok();
            }

            progress?.Report("正在清理旧文件…");
            var notes = new List<string>(2);

            if (moved)
            {
                // 音频文件已被服务器原地改名，只剩旧歌词文件（若有）需要清理；失败登记延后删。
                if (!await TryDeleteRemoteQuietlyAsync(client, lrcOld, cancellationToken).ConfigureAwait(false))
                {
                    EnqueuePendingDelete(config.Id, lrcOld);
                    notes.Add("旧歌词文件暂时无法删除，切歌后会自动重试清理");
                }
            }
            else
            {
                // 复制式改名回退：旧音频 + 旧歌词还在原位，尽力删除；失败（常见于正被播放占用）
                // 不能让保存报失败——新文件已就位，曲库已指向新文件，登记下来等切歌后自动重试。
                var oldDeleted = await TryDeleteRemoteQuietlyAsync(client, remotePath, cancellationToken)
                    .ConfigureAwait(false);
                var lrcDeleted = await TryDeleteRemoteQuietlyAsync(client, lrcOld, cancellationToken)
                    .ConfigureAwait(false);

                if (!oldDeleted)
                {
                    EnqueuePendingDelete(config.Id, remotePath);
                    notes.Add($"旧文件「{Path.GetFileName(remotePath)}」暂时无法删除（可能正被播放占用），切歌后会自动重试清理");
                }

                if (!lrcDeleted)
                {
                    EnqueuePendingDelete(config.Id, lrcOld);
                    notes.Add("旧歌词文件暂时无法删除，切歌后会自动重试清理");
                }
            }

            return WriteBackResult.Renamed(
                targetPath!,
                RemoteFileUri.BuildDisplayUri(config, targetPath!),
                notes.Count > 0 ? string.Join("；", notes) + "。" : null);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// 计算按标题重命名后的远端路径（同目录同名不同扩展名不变）。
    /// 标题为空、清洗后为空、或与现名相同（忽略大小写）时返回 null 表示不改名；
    /// 目标名已被同目录其他文件占用时也返回 null，避免覆盖别人的文件。
    /// </summary>
    private static async Task<string?> ResolveRenameTargetAsync(
        IRemoteFileClient client,
        string remotePath,
        string? title,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var desired = SanitizeFileName(title);
        if (desired.Length == 0)
        {
            return null;
        }

        var fileName = remotePath[(remotePath.LastIndexOf('/') + 1)..];
        var directory = remotePath[..^fileName.Length];
        var target = directory + desired + Path.GetExtension(fileName);

        if (string.Equals(target, remotePath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            progress?.Report("正在检查文件名…");
            var entries = await client
                .ListAsync(directory.Length == 0 ? "/" : directory, null, cancellationToken)
                .ConfigureAwait(false);

            // 大小写不敏感地比较：SMB/Windows 这类服务端会按同一文件处理。
            var occupied = entries.Any(entry
                => !entry.IsDirectory
                    && !string.Equals(entry.Path, remotePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Path, target, StringComparison.OrdinalIgnoreCase));

            return occupied ? null : target;
        }
        catch (Exception)
        {
            // 列不出目录时无法确认目标名是否空闲，保守起见不改名，标签照常写回原文件。
            return null;
        }
    }

    /// <summary>把打好标签的临时文件上传到指定远端路径（流位置归零后再传，重试时才不会从头缺一截）。</summary>
    private static async Task UploadAudioAsync(
        IRemoteFileClient client,
        string remotePath,
        string tempPath,
        IProgress<string>? progress,
        string stage,
        CancellationToken cancellationToken)
    {
        await using var audio = File.OpenRead(tempPath);
        audio.Position = 0;
        await client
            .UploadAsync(remotePath, audio, StageProgress(progress, stage, 50, 100), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>尽力删除远端文件：不存在视为已删；被占用 / 无权限等失败返回 false，由调用方决定如何降级。</summary>
    private static async Task<bool> TryDeleteRemoteQuietlyAsync(
        IRemoteFileClient client,
        string remotePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteAsync(remotePath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 尽力用服务端 MOVE 把远端文件原地改名（WebDAV 支持；FTP/SMB 客户端不实现该能力，直接返回 false）。
    /// 返回 false 表示未能改名，调用方退回复制式改名（上传新文件 + 删除旧文件）。
    /// </summary>
    private static async Task<bool> TryMoveRemoteAsync(
        IRemoteFileClient client,
        string oldPath,
        string newPath,
        CancellationToken cancellationToken)
    {
        if (client is not IRemoteFileMoveClient mover)
        {
            return false;
        }

        try
        {
            await mover.MoveAsync(oldPath, newPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>是否还有登记待清理的远端旧文件（决定切歌后是否值得触发清理）。</summary>
    public bool HasPendingDeletes => !_pendingDeletes.IsEmpty;

    /// <summary>登记一个稍后重试删除的远端旧文件。</summary>
    private void EnqueuePendingDelete(string sourceId, string remotePath)
        => _pendingDeletes.Enqueue(new PendingRemoteDelete(sourceId, remotePath));

    /// <summary>
    /// 重试清理登记的远端旧文件：逐条删除；成功或音源已删除就出队，仍失败（还在被占用等）重新入队等下一次。
    /// 全程不抛异常，供切歌后后台触发。
    /// </summary>
    public async Task FlushPendingDeletesAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingDeletes.IsEmpty)
        {
            return;
        }

        var retry = new List<PendingRemoteDelete>();
        while (_pendingDeletes.TryDequeue(out var pending))
        {
            try
            {
                var config = _settings.Current.Sources.FirstOrDefault(source => source.Id == pending.SourceId);
                if (config is null)
                {
                    continue; // 音源已删除，无从清理，放弃。
                }

                await using var client = _remoteClientFactory.Create(config);
                await client.DeleteAsync(pending.RemotePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                retry.Add(pending);
                break;
            }
            catch (Exception)
            {
                retry.Add(pending); // 仍被占用等失败，登记下次再试。
            }
        }

        foreach (var pending in retry)
        {
            _pendingDeletes.Enqueue(pending);
        }
    }

    /// <summary>把旧路径的 .lrc 搬到新路径：下载成功就上传到新名下；没有歌词文件时静默跳过。</summary>
    private static async Task TryMoveRemoteLyricsAsync(
        IRemoteFileClient client,
        string oldRemotePath,
        string newLrcPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var buffer = new MemoryStream();
            await client
                .DownloadAsync(Path.ChangeExtension(oldRemotePath, ".lrc"), buffer, null, cancellationToken)
                .ConfigureAwait(false);

            if (buffer.Length == 0)
            {
                return;
            }

            buffer.Position = 0;
            await client.UploadAsync(newLrcPath, buffer, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 旧文件本来就没有歌词时下载会失败，忽略即可。
        }
    }

    /// <summary>把标题清洗成远端文件系统可安全使用的文件名（去掉非法字符与首尾空白、结尾点号）。</summary>
    private static string SanitizeFileName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var name = title.Trim();
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            if (ch is '/' or '\\' || ch < 32 || Path.GetInvalidFileNameChars().Contains(ch))
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(ch);
            }
        }

        var clean = builder.ToString().Trim().TrimEnd('.').Trim();

        // 文件名过长在部分服务端（SMB 255 字符上限等）会创建失败，超长时截断。
        if (clean.Length > 120)
        {
            clean = clean[..120].Trim().TrimEnd('.');
        }

        return clean;
    }

    /// <summary>把 0~1 的字节进度换算成整体百分比文案，例如「正在下载原文件… 42%」。</summary>
    private static IProgress<double>? StageProgress(
        IProgress<string>? progress,
        string stage,
        int fromPercent,
        int toPercent)
        => progress is null
            ? null
            : new Progress<double>(value => progress.Report(
                $"{stage}… {fromPercent + (toPercent - fromPercent) * value:0}%"));

    /// <summary>
    /// 仅把歌词上传到音源（不做整文件往返写标签）：
    /// 本地音源直接写同目录同名 <c>.lrc</c>；FTP/SMB/WebDAV 上传同名 <c>.lrc</c>；
    /// Navidrome 与在线曲目没有实体文件入口，跳过。
    /// 由「同步」按钮显式触发，不受「刮削时写回音源」开关控制。
    /// </summary>
    public async Task<WriteBackResult> UploadLyricsAsync(
        Track track,
        string? lyrics,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lyrics))
        {
            return new WriteBackResult(WriteBackStatus.Skipped, "该结果没有歌词。");
        }

        try
        {
            return track.SourceType switch
            {
                MusicSourceType.Local => await UploadLyricsLocalAsync(track, lyrics, cancellationToken)
                    .ConfigureAwait(false),

                MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav =>
                    await UploadLyricsRemoteAsync(track, lyrics, progress, cancellationToken)
                        .ConfigureAwait(false),

                _ => new WriteBackResult(WriteBackStatus.Skipped, "该音源没有可写的实体文件。"),
            };
        }
        catch (Exception ex)
        {
            return WriteBackResult.Fail(ex.Message);
        }
    }

    private static async Task<WriteBackResult> UploadLyricsLocalAsync(
        Track track,
        string lyrics,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(track.Path))
        {
            return WriteBackResult.Fail("找不到本地音频文件。");
        }

        await File.WriteAllTextAsync(
                Path.ChangeExtension(track.Path, ".lrc"), lyrics, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return WriteBackResult.Ok();
    }

    private async Task<WriteBackResult> UploadLyricsRemoteAsync(
        Track track,
        string lyrics,
        IProgress<string>? progress,
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

        progress?.Report("正在上传歌词到音源…");

        var lrcRemote = Path.ChangeExtension(remotePath, ".lrc");
        await using var client = _remoteClientFactory.Create(config);
        using var lrcStream = new MemoryStream(Encoding.UTF8.GetBytes(lyrics));
        await client
            .UploadAsync(lrcRemote, lrcStream, null, cancellationToken)
            .ConfigureAwait(false);
        return WriteBackResult.Ok();
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
