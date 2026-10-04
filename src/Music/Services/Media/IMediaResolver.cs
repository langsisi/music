using System;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Media;

/// <summary>
/// 把曲目解析为「当前可播放」的本地路径或 URL。
/// 具体是直连远端还是先落盘缓存，由实现决定。
/// </summary>
public interface IMediaResolver
{
    /// <summary>
    /// 解析出可播放的本地路径或 URL。
    /// 需要下载时就绪前通过 <paramref name="progress"/> 上报 0..1 的进度，供界面显示缓冲状态。
    /// </summary>
    Task<string> ResolveAsync(
        Track track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
