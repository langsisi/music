using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Sources;

/// <summary>
/// Navidrome 音源：拉取全部专辑与曲目写入本地索引。
/// 每张专辑只下载一次封面并赋给该专辑的所有曲目，避免逐曲重复下载。
/// </summary>
public sealed class NavidromeMusicSource : IMusicSource, IConnectionTestableMusicSource
{
    private const int CoverConcurrency = 4;

    private readonly NavidromeSourceConfig _config;
    private readonly HttpClient _httpClient;

    public NavidromeMusicSource(NavidromeSourceConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    public MusicSourceConfig Config => _config;

    public MusicSourceType Type => MusicSourceType.Navidrome;

    public async Task<IReadOnlyList<Track>> GetTracksAsync(
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var client = new NavidromeClient(_config, _httpClient);

        await client.PingAsync(cancellationToken).ConfigureAwait(false);
        var songs = await client.GetAllSongsAsync(progress, cancellationToken).ConfigureAwait(false);

        var covers = await DownloadCoversAsync(client, songs, progress, cancellationToken).ConfigureAwait(false);

        var tracks = new List<Track>(songs.Count);
        foreach (var song in songs)
        {
            tracks.Add(new Track
            {
                Id = TrackKey.Create(_config.Id, song.Id),
                SourceId = _config.Id,
                SourceType = MusicSourceType.Navidrome,
                Path = client.BuildStreamUrl(song.Id),
                RemoteId = song.Id,
                Title = song.Title,
                Artist = song.Artist,
                Album = song.Album,
                Genre = song.Genre,
                DurationSeconds = song.Duration,
                TrackNumber = song.TrackNumber,
                Year = song.Year,
                FileSize = song.Size,
                CoverPath = covers.GetValueOrDefault(song.AlbumId),
            });
        }

        return tracks;
    }

    /// <summary>校验服务器地址与账号是否可用，供「测试连接」使用。</summary>
    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
        => new NavidromeClient(_config, _httpClient).PingAsync(cancellationToken);

    private static async Task<ConcurrentDictionary<string, string?>> DownloadCoversAsync(
        NavidromeClient client,
        IReadOnlyList<NavidromeSong> songs,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 一张专辑一个封面，按专辑去重后再下载。
        var albumCovers = songs
            .Where(song => !string.IsNullOrWhiteSpace(song.CoverArtId))
            .GroupBy(song => song.AlbumId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().CoverArtId!, StringComparer.Ordinal);

        var covers = new ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
        if (albumCovers.Count == 0)
        {
            return covers;
        }

        progress?.Report(new ScanProgress("正在获取专辑封面", 0, albumCovers.Count));
        var completed = 0;

        await Parallel.ForEachAsync(
            albumCovers,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = CoverConcurrency,
                CancellationToken = cancellationToken,
            },
            async (pair, token) =>
            {
                var path = await client.EnsureCoverAsync(pair.Key, pair.Value, token).ConfigureAwait(false);
                covers[pair.Key] = path;

                progress?.Report(new ScanProgress(
                    "正在获取专辑封面",
                    Interlocked.Increment(ref completed),
                    albumCovers.Count));
            }).ConfigureAwait(false);

        return covers;
    }
}
