using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Remote;

namespace Music.Services.Sources;

/// <summary>
/// 文件协议音源（FTP / SMB / WebDAV）：递归列出远端目录，把音频文件登记为曲目。
/// 这类协议没有元数据接口，标题取自文件名、专辑取自所在目录；
/// 播放时先整文件下载到缓存（见 <c>SqliteAudioCache</c>）。
/// </summary>
public sealed class RemoteFileMusicSource : IMusicSource, IConnectionTestableMusicSource
{
    private readonly MusicSourceConfig _config;
    private readonly IRemoteFileClientFactory _clientFactory;

    public RemoteFileMusicSource(MusicSourceConfig config, IRemoteFileClientFactory clientFactory)
    {
        _config = config;
        _clientFactory = clientFactory;
    }

    public MusicSourceConfig Config => _config;

    public MusicSourceType Type => _config.Type;

    /// <summary>起始目录，只有 FTP/SMB/WebDAV 才有该字段。</summary>
    private string RootPath => _config switch
    {
        FtpSourceConfig ftp => ftp.RootPath,
        SmbSourceConfig smb => smb.RootPath,
        WebDavSourceConfig webDav => webDav.RootPath,
        _ => "/",
    };

    public async Task<IReadOnlyList<Track>> GetTracksAsync(
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var client = _clientFactory.Create(_config);

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var entries = await client.ListAsync(RootPath, progress, cancellationToken).ConfigureAwait(false);

        var files = entries
            .Where(entry => !entry.IsDirectory && AudioFileTypes.IsAudioFile(entry.Path))
            .ToList();

        progress?.Report(new ScanProgress("正在整理曲目", 0, files.Count));

        var tracks = new List<Track>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remotePath = NormalizeRemotePath(file.Path);
            tracks.Add(new Track
            {
                Id = TrackKey.Create(_config.Id, remotePath),
                SourceId = _config.Id,
                SourceType = _config.Type,
                Path = BuildDisplayUri(remotePath),
                RemoteId = remotePath,
                Title = Path.GetFileNameWithoutExtension(remotePath),
                Album = Path.GetFileName(Path.GetDirectoryName(remotePath) ?? string.Empty),
                FileSize = file.Size,
            });
        }

        progress?.Report(new ScanProgress("同步完成", tracks.Count, tracks.Count));
        return tracks;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var client = _clientFactory.Create(_config);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>远端路径统一成 '/' 形式，保证曲目 Id 在不同服务器写法下保持稳定。</summary>
    private static string NormalizeRemotePath(string path) => path.Replace('\\', '/');

    /// <summary>用于界面展示的地址，取流时不使用该地址。</summary>
    private string BuildDisplayUri(string remotePath)
    {
        var suffix = remotePath.StartsWith('/') ? remotePath : "/" + remotePath;

        return _config switch
        {
            FtpSourceConfig ftp => $"ftp://{ftp.Host}:{ftp.Port}{suffix}",
            SmbSourceConfig smb => $"smb://{smb.Host}/{smb.ShareName}{suffix}",
            WebDavSourceConfig webDav => $"{webDav.BaseUrl.TrimEnd('/')}{NormalizeRoot(webDav.RootPath)}{suffix}",
            _ => suffix,
        };
    }

    private static string NormalizeRoot(string rootPath) =>
        string.IsNullOrWhiteSpace(rootPath) || rootPath == "/" ? string.Empty : "/" + rootPath.Trim('/');
}
