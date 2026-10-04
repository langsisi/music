using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Library;
using Music.Services.Metadata;

namespace Music.Services.Sources;

/// <summary>把一个音源的扫描结果同步进本地曲库索引。</summary>
public sealed class LibrarySyncService
{
    private readonly IMusicSourceFactory _sourceFactory;
    private readonly ILibraryStore _libraryStore;
    private readonly ISettingsStore _settings;
    private readonly RemoteMetadataEnricher _enricher;

    /// <summary>后台补封面任务是否在跑（1 表示在跑），避免重复发起。</summary>
    private int _coverPrefetchRunning;

    public LibrarySyncService(
        IMusicSourceFactory sourceFactory,
        ILibraryStore libraryStore,
        ISettingsStore settings,
        RemoteMetadataEnricher enricher)
    {
        _sourceFactory = sourceFactory;
        _libraryStore = libraryStore;
        _settings = settings;
        _enricher = enricher;
    }

    /// <summary>
    /// 扫描并写入索引，返回入库曲目数与本次补齐的封面张数。
    /// <paramref name="prefetchCovers"/> 为 true 时，对 FTP / SMB / WebDAV 曲目顺带抓取封面，
    /// 这样同步完就能在列表里看到封面，而不必先播放一次。
    /// </summary>
    public async Task<SyncResult> SyncAsync(
        MusicSourceConfig config,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool prefetchCovers = true)
    {
        var source = _sourceFactory.Create(config);
        var tracks = await source.GetTracksAsync(progress, cancellationToken).ConfigureAwait(false);

        // 远端文件协议扫描时不读文件标签：标题取自文件名、歌手/年份为空、封面路径为空。
        // 直接覆盖会把用户编辑过的元数据和已抓到的封面冲掉，所以先沿用库里同 Id 曲目的值。
        if (config.Type is MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav)
        {
            await CarryOverExistingAsync(config, tracks, cancellationToken).ConfigureAwait(false);
        }

        // 曲目 Id 由路径哈希得到，重新扫描不会改变已收藏曲目的 Id。
        await _libraryStore.RemoveSourceTracksAsync(config.Id, cancellationToken).ConfigureAwait(false);
        await _libraryStore.UpsertTracksAsync(tracks, cancellationToken).ConfigureAwait(false);

        var covers = 0;
        if (prefetchCovers)
        {
            covers = await _enricher.PrefetchCoversAsync(tracks, progress, cancellationToken).ConfigureAwait(false);
        }

        return new SyncResult(tracks.Count, covers);
    }

    /// <summary>
    /// 重新同步远端音源时，把库里已有的标题 / 歌手 / 专辑 / 年份 / 封面路径沿用到新扫描结果上，
    /// 避免用户编辑过的元数据（以及后台补齐的封面）被文件名派生的值覆盖。
    /// 曲目 Id 由路径哈希得到，路径没变 Id 就不变，因此能一一对上。
    /// </summary>
    private async Task CarryOverExistingAsync(
        MusicSourceConfig config,
        IReadOnlyList<Track> tracks,
        CancellationToken cancellationToken)
    {
        var existing = await _libraryStore
            .GetTracksAsync(config.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing.Count == 0)
        {
            return;
        }

        var previous = existing.ToDictionary(track => track.Id, StringComparer.Ordinal);

        foreach (var track in tracks)
        {
            if (!previous.TryGetValue(track.Id, out var old))
            {
                continue;
            }

            track.Title = old.Title;
            track.Artist = old.Artist;
            track.Album = old.Album;
            track.Year = old.Year;
            track.CoverPath = old.CoverPath;
        }
    }

    /// <summary>
    /// 启动时补齐：对「已配置但本地索引里一首都没有」的音源做一次扫描。
    /// 已有曲目的音源不会重扫，避免每次启动都遍历磁盘。
    /// </summary>
    public async Task SyncMissingSourcesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var config in _settings.Current.Sources)
        {
            if (!config.Enabled)
            {
                continue;
            }

            var existing = await _libraryStore
                .GetSourceTrackCountAsync(config.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existing > 0)
            {
                continue;
            }

            // 启动路径不抓封面：避免开机时对成千上万个文件发请求，封面交给后台慢慢补。
            await SyncAsync(config, null, cancellationToken, prefetchCovers: false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 后台把整个曲库里还缺封面的 FTP / SMB / WebDAV 曲目慢慢补齐：不阻塞调用方、不显示进度，
    /// 封面会随抓取分批落库、陆续出现在列表里。已有任务在跑时直接返回，避免重复请求。
    /// </summary>
    public void StartBackgroundCoverPrefetch()
    {
        if (Interlocked.CompareExchange(ref _coverPrefetchRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var tracks = await _libraryStore
                    .GetTracksAsync(cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
                await _enricher
                    .PrefetchCoversAsync(tracks, null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 后台补封面失败不影响使用，下次启动会再试。
            }
            finally
            {
                Interlocked.Exchange(ref _coverPrefetchRunning, 0);
            }
        });
    }
}

/// <summary>一次同步的结果：入库曲目数与本次补齐的封面张数。</summary>
public readonly record struct SyncResult(int TrackCount, int CoverCount);
