using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Remote;

/// <summary>远程文件系统上的一个条目。<see cref="Path"/> 以 '/' 分隔，相对音源根目录。</summary>
public readonly record struct RemoteEntry(string Path, long Size, bool IsDirectory);

/// <summary>
/// 远程文件操作的最小抽象（FTP / SMB / WebDAV 共用）。抽出来有两个好处：
/// 各协议音源与缓存下载共用同一套连接逻辑；同时可以在没有真实服务器的环境里替换成假实现做验证。
/// </summary>
public interface IRemoteFileClient : IAsyncDisposable
{
    /// <summary>连上服务器并校验账号；失败时抛异常。</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>递归列出 <paramref name="rootPath"/> 下的全部条目。</summary>
    Task<IReadOnlyList<RemoteEntry>> ListAsync(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>把远端文件下载到 <paramref name="destination"/>。</summary>
    Task DownloadAsync(
        string remotePath,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 从 <paramref name="offset"/> 起顺序读取至多 <paramref name="length"/> 字节，返回可读的流。
    /// 用于本地 HTTP 代理实现「边下边播」与拖动：每次调用独占一条连接，调用方负责释放流。
    /// </summary>
    Task<Stream> OpenReadAsync(
        string remotePath,
        long offset,
        long length,
        CancellationToken cancellationToken);

    /// <summary>把 <paramref name="content"/> 上传到远端路径；目录不存在时自动创建。</summary>
    Task UploadAsync(
        string remotePath,
        Stream content,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    /// <summary>删除远端文件；文件不存在时视为成功。</summary>
    Task DeleteAsync(string remotePath, CancellationToken cancellationToken);
}

/// <summary>
/// 可选能力接口：支持在服务端直接重命名文件的客户端（WebDAV 的 MOVE）。
/// 按标题改名写回时优先走这里——旧文件由服务器原地改名消失，不需要「上传新文件 + 删除旧文件」，
/// 即使旧文件正被播放代理占用也不受影响。调用方需用 <c>is</c> 判断客户端是否支持。
/// </summary>
public interface IRemoteFileMoveClient
{
    /// <summary>把远端文件 <paramref name="remotePath"/> 改名为 <paramref name="destinationPath"/>；失败时抛异常。</summary>
    Task MoveAsync(string remotePath, string destinationPath, CancellationToken cancellationToken);
}
