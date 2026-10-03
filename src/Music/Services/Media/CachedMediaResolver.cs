using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Cache;

namespace Music.Services.Media;

/// <summary>
/// 带磁盘缓存的取流策略：
/// <list type="bullet">
/// <item>本地曲目：直接返回路径，不进缓存。</item>
/// <item>FTP：无 Range 语义，必须先下载到缓存再本地播放（拖动进度不会重传整个文件）。</item>
/// <item>HTTP（Navidrome）：服务端支持 Range，直连流式播放，同时后台落盘供下次复用。</item>
/// </list>
/// 缓存上限为 0 时退化为全部直连。
/// </summary>
public sealed class CachedMediaResolver : IMediaResolver
{
    private readonly IAudioCache _cache;

    public CachedMediaResolver(IAudioCache cache) => _cache = cache;

    public async Task<string> ResolveAsync(Track track, CancellationToken cancellationToken = default)
    {
        if (track.SourceType == MusicSourceType.Local || !_cache.IsEnabled)
        {
            _cache.SetPinned(null);
            return track.Path;
        }

        var cached = await _cache.TryGetAsync(track, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _cache.SetPinned(track.Id);
            return cached;
        }

        if (track.SourceType == MusicSourceType.Ftp)
        {
            var local = await _cache
                .DownloadAsync(track, null, cancellationToken)
                .ConfigureAwait(false);
            _cache.SetPinned(track.Id);
            return local;
        }

        // 直连播放，同时后台下载；下次再播就会命中缓存。
        _cache.DownloadInBackground(track);
        return track.Path;
    }
}
