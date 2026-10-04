using System;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Cache;
using Music.Services.Metadata;
using Music.Services.Online;

namespace Music.Services.Media;

/// <summary>
/// 带磁盘缓存的取流策略：
/// <list type="bullet">
/// <item>本地曲目：直接返回路径，不进缓存。</item>
/// <item>FTP/SMB/WebDAV：先交给本地 HTTP 代理「边下边播」（秒开、可拖动），并在后台落盘供下次直接本地播放；
/// 代理不可用时退化为整文件下载后再本地播放，下载进度上报给界面。</item>
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
    private readonly RemoteMetadataEnricher _enricher;
    private readonly LocalMediaProxy _proxy;

    public CachedMediaResolver(
        IAudioCache cache,
        GdMusicClient gd,
        RemoteMetadataEnricher enricher,
        LocalMediaProxy proxy)
    {
        _cache = cache;
        _gd = gd;
        _enricher = enricher;
        _proxy = proxy;
    }

    public async Task<string> ResolveAsync(
        Track track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
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

            progress?.Report(1);
            return url;
        }

        if (track.SourceType == MusicSourceType.Local || !_cache.IsEnabled)
        {
            _cache.SetPinned(null);
            progress?.Report(1);
            return track.Path;
        }

        var cached = await _cache.TryGetAsync(track, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _cache.SetPinned(track.Id);

            // 命中的缓存文件也能顺手补齐封面/歌词（老缓存或首次补齐失败的情况）。
            _enricher.EnrichInBackground(track, cached);
            progress?.Report(1);
            return cached;
        }

        if (track.SourceType is MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav)
        {
            // 代理可用的前提下优先边下边播：VLC 立刻开始播放，拖动进度只按需向远端取区间。
            // 同时后台整文件落盘，下次播放直接命中本地缓存，也才有条件补齐封面/歌词。
            if (_proxy.TryGetStreamUrl(track) is { } streamUrl)
            {
                _cache.SetPinned(track.Id);
                _ = PrimeCacheAsync(track);
                progress?.Report(1);
                return streamUrl;
            }

            var local = await _cache
                .DownloadAsync(track, progress, cancellationToken)
                .ConfigureAwait(false);
            _cache.SetPinned(track.Id);

            // 文件刚落到本地缓存，读它的内嵌标签补齐封面/歌词（零额外下载）。
            _enricher.EnrichInBackground(track, local);
            return local;
        }

        // 直连播放，同时后台下载；下次再播就会命中缓存。
        _cache.DownloadInBackground(track);
        progress?.Report(1);
        return track.Path;
    }

    /// <summary>边下边播期间把整文件预热进缓存，完成后顺手补齐封面/歌词；失败不影响正在进行的播放。</summary>
    private async Task PrimeCacheAsync(Track track)
    {
        try
        {
            var local = await _cache.DownloadAsync(track).ConfigureAwait(false);
            _enricher.EnrichInBackground(track, local);
        }
        catch (Exception)
        {
            // 预热失败只是这次没缓存，下次播放会重新尝试。
        }
    }
}
