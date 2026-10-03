using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Ftp;

namespace Music.Services.Sources;

/// <summary>
/// FTP 音源：递归列出远端目录，把音频文件登记为曲目。
/// FTP 没有元数据接口，标题取自文件名、专辑取自所在目录；播放时先整文件下载到缓存（见 <c>SqliteAudioCache</c>）。
/// </summary>
public sealed class FtpMusicSource : IMusicSource, IConnectionTestableMusicSource
{
    private readonly FtpSourceConfig _config;
    private readonly IFtpFileClientFactory _clientFactory;

    public FtpMusicSource(FtpSourceConfig config, IFtpFileClientFactory clientFactory)
    {
        _config = config;
        _clientFactory = clientFactory;
    }

    public MusicSourceConfig Config => _config;

    public MusicSourceType Type => MusicSourceType.Ftp;

    public async Task<IReadOnlyList<Track>> GetTracksAsync(
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var client = _clientFactory.Create(_config);

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var entries = await client.ListAsync(_config.RootPath, progress, cancellationToken).ConfigureAwait(false);

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
                SourceType = MusicSourceType.Ftp,
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

    private string BuildDisplayUri(string remotePath)
    {
        var suffix = remotePath.StartsWith('/') ? remotePath : "/" + remotePath;
        return $"ftp://{_config.Host}:{_config.Port}{suffix}";
    }
}
