using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Library;
using Music.Services.Remote;

namespace Music.Services.Sources;

/// <summary>删除音乐的结果。<see cref="Message"/> 可直接展示给用户。</summary>
public enum DeleteStatus
{
    /// <summary>删除成功（文件已删除并移出曲库）。</summary>
    Succeeded,

    /// <summary>该音源不支持删除（如 Navidrome）。</summary>
    Skipped,

    /// <summary>删除失败，<c>Message</c> 为原因。</summary>
    Failed,
}

public sealed record DeleteResult(DeleteStatus Status, string Message)
{
    public static DeleteResult Ok(string message) => new(DeleteStatus.Succeeded, message);

    public static DeleteResult Skip(string message) => new(DeleteStatus.Skipped, message);

    public static DeleteResult Fail(string message) => new(DeleteStatus.Failed, message);
}

/// <summary>
/// 删除音乐：删掉源文件本身并从曲库移除（收藏、归类关系一并清除）。
/// <list type="bullet">
/// <item>本地：删除磁盘文件（连同同名 <c>.lrc</c>）。</item>
/// <item>FTP / SMB / WebDAV：删除远端文件（连同同名 <c>.lrc</c>）。</item>
/// <item>在线（GD 音乐台）：没有实体文件，仅从曲库移除。</item>
/// <item>Navidrome：没有删除接口，跳过。</item>
/// </list>
/// </summary>
public sealed class TrackDeleteService
{
    private readonly ILibraryStore _library;
    private readonly ISettingsStore _settings;
    private readonly IRemoteFileClientFactory _remoteClients;

    public TrackDeleteService(
        ILibraryStore library,
        ISettingsStore settings,
        IRemoteFileClientFactory remoteClients)
    {
        _library = library;
        _settings = settings;
        _remoteClients = remoteClients;
    }

    public async Task<DeleteResult> DeleteAsync(Track track, CancellationToken cancellationToken = default)
    {
        if (track.SourceType == MusicSourceType.Navidrome)
        {
            return DeleteResult.Skip("Navidrome 音源不支持删除。");
        }

        string message;
        try
        {
            switch (track.SourceType)
            {
                case MusicSourceType.Local:
                    DeleteLocalFile(track.Path);
                    message = "已删除本地文件并从曲库移除。";
                    break;

                case MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav:
                    await DeleteRemoteFileAsync(track, cancellationToken).ConfigureAwait(false);
                    message = "已删除远端文件并从曲库移除。";
                    break;

                case MusicSourceType.Online:
                    message = "已从曲库移除。";
                    break;

                default:
                    return DeleteResult.Skip("该音源不支持删除。");
            }
        }
        catch (Exception ex)
        {
            return DeleteResult.Fail($"删除文件失败：{ex.Message}");
        }

        await _library.RemoveTracksAsync([track.Id], cancellationToken).ConfigureAwait(false);
        return DeleteResult.Ok(message);
    }

    private static void DeleteLocalFile(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }

        // 写回时生成的同名 .lrc 也一并清掉。
        TryDeleteFile(Path.ChangeExtension(path, ".lrc"));
    }

    private async Task DeleteRemoteFileAsync(Track track, CancellationToken cancellationToken)
    {
        var config = _settings.Current.Sources.FirstOrDefault(source => source.Id == track.SourceId)
            ?? throw new InvalidOperationException("找不到音源配置。");

        var remotePath = track.RemoteId;
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new InvalidOperationException("曲目缺少远端路径。");
        }

        await using var client = _remoteClients.Create(config);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await client.DeleteAsync(remotePath, cancellationToken).ConfigureAwait(false);

        // 同名 .lrc 是写回时生成的，顺带删除；没有或删不掉都不影响主流程。
        try
        {
            await client.DeleteAsync(Path.ChangeExtension(remotePath, ".lrc"), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 歌词文件不存在或无权删除时忽略。
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 歌词文件删不掉不影响曲目删除。
        }
    }
}
