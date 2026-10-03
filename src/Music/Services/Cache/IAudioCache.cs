using System;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Cache;

/// <summary>缓存占用统计。</summary>
public readonly record struct CacheStats(long TotalBytes, int FileCount)
{
    public string SizeText
    {
        get
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = TotalBytes;
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{TotalBytes} B" : $"{value:0.##} {units[unit]}";
        }
    }
}

/// <summary>
/// 音频磁盘缓存：播放过的网络曲目落盘复用，避免每次重新下载。
/// 容量上限来自设置，超出后按 LRU 淘汰。
/// </summary>
public interface IAudioCache
{
    /// <summary>缓存上限（字节），0 表示禁用缓存。</summary>
    long MaxBytes { get; }

    bool IsEnabled { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>命中则返回本地文件路径，否则返回 null。</summary>
    Task<string?> TryGetAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>下载到缓存并返回本地路径（已命中则直接返回）。</summary>
    Task<string> DownloadAsync(
        Track track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>后台下载，用于「先直连播放、同时落盘」的场景。</summary>
    void DownloadInBackground(Track track);

    /// <summary>标记当前正在播放的条目，淘汰时跳过它。</summary>
    void SetPinned(string? trackId);

    /// <summary>按 LRU 淘汰到上限以内，返回删除的文件数。</summary>
    Task<int> EvictAsync(CancellationToken cancellationToken = default);

    Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
