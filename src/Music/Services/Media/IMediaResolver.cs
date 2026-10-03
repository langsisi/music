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
    Task<string> ResolveAsync(Track track, CancellationToken cancellationToken = default);
}
