using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Music.Models;

namespace Music.Services.Remote;

/// <summary>基于 FluentFTP 的实现。连接按需建立并在实例生命周期内复用。</summary>
public sealed class FtpRemoteFileClient : IRemoteFileClient
{
    private readonly AsyncFtpClient _client;
    private bool _connected;

    public FtpRemoteFileClient(FtpSourceConfig config)
        => _client = new AsyncFtpClient(config.Host, config.UserName, config.Password, config.Port);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_connected)
        {
            return;
        }

        await _client.Connect(cancellationToken).ConfigureAwait(false);
        _connected = true;
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        var path = string.IsNullOrWhiteSpace(rootPath) ? "/" : rootPath;
        progress?.Report(new ScanProgress("正在列出 FTP 目录", 0, 0));

        var items = await _client
            .GetListing(path, FtpListOption.Recursive | FtpListOption.Size, cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<RemoteEntry>(items.Length);
        foreach (var item in items)
        {
            // 链接不展开，避免目录自引用造成死循环。
            if (item.Type == FtpObjectType.Link)
            {
                continue;
            }

            entries.Add(new RemoteEntry(item.FullName, item.Size, item.Type == FtpObjectType.Directory));
        }

        progress?.Report(new ScanProgress("已列出", entries.Count, entries.Count));
        return entries;
    }

    public async Task DownloadAsync(
        string remotePath,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        // FtpProgress.Progress 是 0..100 的百分比。
        var ftpProgress = progress is null
            ? null
            : new Progress<FtpProgress>(report => progress.Report(report.Progress / 100));

        await _client
            .DownloadStream(destination, remotePath, 0, ftpProgress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Stream> OpenReadAsync(
        string remotePath,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        // restartPosition 即 FTP 的 REST 命令，直接从 offset 处开始取数据；length 由调用方截断。
        // fileLen 传 -1：不让它为了拿文件大小再跑一次 SIZE，读取本身不需要该值。
        return await _client
            .OpenRead(remotePath, FtpDataType.Binary, offset, fileLen: -1, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UploadAsync(
        string remotePath,
        Stream content,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        // FtpProgress.Progress 是 0..100 的百分比。
        IProgress<FtpProgress>? ftpProgress = progress is null
            ? null
            : new Progress<FtpProgress>(report => progress.Report(report.Progress / 100));

        await _client
            .UploadStream(
                content,
                remotePath,
                FtpRemoteExists.Overwrite,
                createRemoteDir: true,
                ftpProgress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync().ConfigureAwait(false);
}
