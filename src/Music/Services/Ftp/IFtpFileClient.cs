using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Ftp;

/// <summary>FTP 上的一个条目。<see cref="Path"/> 是服务器上的绝对路径。</summary>
public readonly record struct FtpEntry(string Path, long Size, bool IsDirectory);

/// <summary>
/// FTP 文件操作的最小抽象。抽出来有两个好处：FTP 音源与缓存下载共用同一套连接逻辑；
/// 同时可以在没有真实 FTP 服务器的环境里替换成假实现做验证。
/// </summary>
public interface IFtpFileClient : IAsyncDisposable
{
    /// <summary>连上服务器并校验账号；失败时抛异常。</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>递归列出 <paramref name="rootPath"/> 下的全部条目。</summary>
    Task<IReadOnlyList<FtpEntry>> ListAsync(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>把远端文件下载到 <paramref name="destination"/>。</summary>
    Task DownloadAsync(
        string remotePath,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    /// <summary>把 <paramref name="content"/> 上传到远端路径；目录不存在时自动创建。</summary>
    Task UploadAsync(
        string remotePath,
        Stream content,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
