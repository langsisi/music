using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Lyrics;

/// <summary>按曲目解析歌词，并缓存解析结果，避免每次切歌都重新读盘/解析。</summary>
public sealed class LyricsService
{
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly ConcurrentDictionary<string, LyricDocument> _cache = new();

    /// <summary>歌词在后台补齐落盘后触发（参数为曲目 Id），界面据此重新加载当前曲目的歌词。</summary>
    public event Action<string>? LyricsArrived;

    public LyricsService(IEnumerable<ILyricsProvider> providers) => _providers = providers.ToList();

    public async Task<LyricDocument> GetLyricsAsync(Track track, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(track.Id, out var cached))
        {
            return cached;
        }

        var lyrics = LyricDocument.Empty;

        foreach (var provider in _providers.Where(provider => provider.CanHandle(track)))
        {
            var raw = await provider.GetRawLyricsAsync(track, cancellationToken).ConfigureAwait(false);
            var parsed = LrcParser.Parse(raw);

            if (!parsed.IsEmpty)
            {
                lyrics = parsed;
                break;
            }
        }

        _cache[track.Id] = lyrics;
        return lyrics;
    }

    /// <summary>清除某曲目的歌词缓存，用于歌词后台补齐/刮削后立即生效，并通知界面重新加载。</summary>
    public void Invalidate(string trackId)
    {
        _cache.TryRemove(trackId, out _);
        LyricsArrived?.Invoke(trackId);
    }
}
