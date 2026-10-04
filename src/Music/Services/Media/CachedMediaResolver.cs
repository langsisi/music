using System;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Cache;
using Music.Services.Online;

namespace Music.Services.Media;

/// <summary>
/// 带磁盘缓存的取流策略：
/// <list type="bullet">
/// <item>本地曲目：直接返回路径，不进缓存。</item>
/// <item>FTP/SMB/WebDAV：无 Range 语义，必须先下载到缓存再本地播放（拖动进度不会重传整个文件）。</item>
/// <item>HTTP（Navidrome）：服务端支持 Range，直连流式播放，同时后台落盘供下次复用。</item>
/// <item>在线（GD 音乐台）：流地址有时效，每次播放现取，不做缓存。</item>
/// </list>
/// 缓存上限为 0 时退化为全部直连。
/// </summary>
public sealed class CachedMediaResolver : IMediaResolver
{
    /// <summary>在线播放统一请求的音质（kbps）。</summary>
    private const int OnlineBitrate = 320;

    private readonly IAudioCache _cache;
    private readonly GdMusicClient _gd;

    public CachedMediaResolver(IAudioCache cache, GdMusicClient gd)
    {
        _cache = cache;
        _gd = gd;
    }

    public async Task<string> ResolveAsync(Track track, CancellationToken cancellationToken = default)
    {
        // 在线曲目：地址带签名/时效，缓存会失效，因此每次播放都现取。
        if (track.SourceType == MusicSourceType.Online)
        {
            _cache.SetPinned(null);

            if (!OnlineTrack.TryParseStreamIdentity(track.RemoteId, out var source, out var id))
            {
                throw new InvalidOperationException($"在线曲目缺少取流标识：{track.Id}");
            }

            var url = await _gd
                .GetStreamUrlAsync(source, id, OnlineBitrate, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(url))
            {
                throw new InvalidOperationException(
                    $"在线曲目「{track.DisplayTitle}」暂时无法播放（可能受版权或会员限制）。");
            }

            return url;
        }

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

        if (track.SourceType is MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav)
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
