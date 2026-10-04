using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Music.Models;

namespace Music.Services.Online;

/// <summary>
/// 在线发现：搜索、每日推荐与随机推荐。
/// GD 音乐台没有「推荐」接口（官方说明只提供搜索与播放），因此这里的推荐是
/// 用一组热门歌手/关键词作为种子去搜索、再去重合成的结果。每日推荐按日期确定性生成，
/// 随机推荐每次换一批。
/// </summary>
public sealed class OnlineMusicService
{
    /// <summary>推荐结果条数上限（同时控制启动时的请求量与封面加载量）。</summary>
    private const int RecommendationLimit = 12;

    /// <summary>每天/每批使用的种子数量。</summary>
    private const int SeedCount = 3;

    /// <summary>每个种子取几首。</summary>
    private const int SongsPerSeed = 6;

    private static readonly string[] SeedPool =
    [
        "周杰伦", "林俊杰", "陈奕迅", "邓紫棋", "薛之谦", "毛不易",
        "李荣浩", "五月天", "Beyond", "王杰", "张学友", "刘德华",
        "孙燕姿", "许嵩", "汪苏泷", "周深", "朴树", "赵雷",
        "Taylor Swift", "Ed Sheeran", "Adele", "Coldplay", "Maroon 5", "A-Lin",
    ];

    private readonly GdMusicClient _client;
    private readonly Random _random = new();

    public OnlineMusicService(GdMusicClient client) => _client = client;

    /// <summary>按关键词搜索在线曲目。</summary>
    public Task<IReadOnlyList<OnlineTrack>> SearchAsync(
        string source,
        string keyword,
        CancellationToken cancellationToken = default)
        => _client.SearchAsync(source, keyword, cancellationToken: cancellationToken);

    /// <summary>每日推荐：同一天内结果固定。</summary>
    public Task<IReadOnlyList<OnlineTrack>> GetDailyRecommendationsAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var seed = DateOnly.FromDateTime(DateTime.Now).DayNumber;
        var seeds = PickSeeds(new Random(seed));
        return CollectAsync(source, seeds, shuffle: false, cancellationToken);
    }

    /// <summary>随机推荐：每次调用换一批。</summary>
    public Task<IReadOnlyList<OnlineTrack>> GetRandomRecommendationsAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        lock (_random)
        {
            var seeds = PickSeeds(_random);
            return CollectAsync(source, seeds, shuffle: true, cancellationToken);
        }
    }

    /// <summary>取封面字节；失败返回 null（供列表缩略图使用）。</summary>
    public Task<byte[]?> GetCoverBytesAsync(OnlineTrack track, int size = 120, CancellationToken cancellationToken = default)
        => _client.GetCoverBytesAsync(track.Source, track.PicId, size, cancellationToken);

    /// <summary>
    /// 把在线封面缓存到本地（文件名带内容哈希），返回可绑定的本地路径；失败返回 null。
    /// </summary>
    public async Task<string?> EnsureCoverFileAsync(OnlineTrack track, CancellationToken cancellationToken = default)
    {
        var bytes = await _client
            .GetCoverBytesAsync(track.Source, track.PicId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            AppPaths.EnsureCreated();

            var extension = LooksLikePng(bytes) ? ".png" : ".jpg";
            var hash = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()[..8];
            var path = Path.Combine(AppPaths.CoversDir, $"online_{SafeId(track.Key)}_{hash}{extension}");

            if (!File.Exists(path))
            {
                await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            }

            return path;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// 把在线歌词缓存成 <c>ScrapedLrcProvider</c> 约定的本地 .lrc，播放时即可显示歌词；失败静默。
    /// </summary>
    public async Task EnsureLyricFileAsync(OnlineTrack track, CancellationToken cancellationToken = default)
    {
        try
        {
            AppPaths.EnsureCreated();
            var path = AppPaths.LyricsFileFor(OnlineTrack.BuildTrackId(track.Source, track.Id));
            if (File.Exists(path))
            {
                return;
            }

            var lyric = await _client
                .GetLyricAsync(track.Source, track.LyricId, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(lyric))
            {
                await File.WriteAllTextAsync(path, lyric, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 在线歌词只是锦上添花。
        }
    }

    /// <summary>
    /// 播放前的准备：歌词先落盘（播放时即可显示），封面在后台缓存并挂到播放队列里的曲目上。
    /// 首页与发现页共用，避免各自写一份。
    /// </summary>
    public async Task PrepareForPlaybackAsync(
        OnlineTrack track,
        Track live,
        CancellationToken cancellationToken = default)
    {
        await EnsureLyricFileAsync(track, cancellationToken).ConfigureAwait(false);
        _ = ApplyCoverAsync(track, live, cancellationToken);
    }

    private async Task ApplyCoverAsync(OnlineTrack track, Track live, CancellationToken cancellationToken)
    {
        var path = await EnsureCoverFileAsync(track, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return;
        }

        // 曲目对象可能被 UI 线程读取，赋值回到 UI 线程执行。
        await Dispatcher.UIThread.InvokeAsync(() => live.CoverPath = path);
    }

    private static bool LooksLikePng(byte[] data)
        => data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    private static string SafeId(string value)
        => value.Replace(':', '_').Replace('/', '_').Replace('\\', '_');

    private static IReadOnlyList<string> PickSeeds(Random random)
    {
        var pool = SeedPool.ToArray();
        for (var i = pool.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.Take(SeedCount).ToArray();
    }

    private async Task<IReadOnlyList<OnlineTrack>> CollectAsync(
        string source,
        IReadOnlyList<string> seeds,
        bool shuffle,
        CancellationToken cancellationToken)
    {
        var results = new List<OnlineTrack>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seed in seeds)
        {
            IReadOnlyList<OnlineTrack> found;
            try
            {
                found = await _client
                    .SearchAsync(source, seed, SongsPerSeed, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单个种子搜索失败不影响整体推荐。
                continue;
            }

            foreach (var track in found)
            {
                if (seen.Add(track.Key))
                {
                    results.Add(track);
                }
            }

            if (results.Count >= RecommendationLimit)
            {
                break;
            }
        }

        if (shuffle)
        {
            for (var i = results.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (results[i], results[j]) = (results[j], results[i]);
            }
        }

        return results.Count > RecommendationLimit
            ? results.Take(RecommendationLimit).ToList()
            : results;
    }
}