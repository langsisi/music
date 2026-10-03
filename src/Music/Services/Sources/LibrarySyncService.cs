using System;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Library;

namespace Music.Services.Sources;

/// <summary>把一个音源的扫描结果同步进本地曲库索引。</summary>
public sealed class LibrarySyncService
{
    private readonly IMusicSourceFactory _sourceFactory;
    private readonly ILibraryStore _libraryStore;
    private readonly ISettingsStore _settings;

    public LibrarySyncService(
        IMusicSourceFactory sourceFactory,
        ILibraryStore libraryStore,
        ISettingsStore settings)
    {
        _sourceFactory = sourceFactory;
        _libraryStore = libraryStore;
        _settings = settings;
    }

    /// <summary>扫描并写入索引，返回入库曲目数。</summary>
    public async Task<int> SyncAsync(
        MusicSourceConfig config,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = _sourceFactory.Create(config);
        var tracks = await source.GetTracksAsync(progress, cancellationToken).ConfigureAwait(false);

        // 曲目 Id 由路径哈希得到，重新扫描不会改变已收藏曲目的 Id。
        await _libraryStore.RemoveSourceTracksAsync(config.Id, cancellationToken).ConfigureAwait(false);
        await _libraryStore.UpsertTracksAsync(tracks, cancellationToken).ConfigureAwait(false);

        return tracks.Count;
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

            await SyncAsync(config, null, cancellationToken).ConfigureAwait(false);
        }
    }
}
