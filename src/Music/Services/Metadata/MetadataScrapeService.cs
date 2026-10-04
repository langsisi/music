using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Library;
using Music.Services.Lyrics;

namespace Music.Services.Metadata;

/// <summary>按选定候选应用封面/歌词后的结果。<see cref="Message"/> 可直接展示给用户。</summary>
public sealed record ScrapeResult(bool CoverUpdated, bool LyricsUpdated, string Message);

/// <summary>
/// 在线元数据刮削：先按数据源搜索候选，再由用户选定某一条后下载封面/歌词，
/// <b>只缓存到本地</b>（不改动音乐文件），并落库更新封面路径与用户编辑过的元数据。
/// </summary>
public sealed class MetadataScrapeService
{
    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly ILibraryStore _libraryStore;
    private readonly LyricsService _lyricsService;

    public MetadataScrapeService(
        IEnumerable<IMetadataProvider> providers,
        ILibraryStore libraryStore,
        LyricsService lyricsService)
    {
        _providers = providers.ToList();
        _libraryStore = libraryStore;
        _lyricsService = lyricsService;
    }

    /// <summary>可用数据源（注册顺序即界面上拉顺序）。</summary>
    public IReadOnlyList<IMetadataProvider> Providers => _providers;

    private IMetadataProvider? FindProvider(string providerId)
        => _providers.FirstOrDefault(provider => provider.Id == providerId);

    /// <summary>按指定数据源搜索候选；数据源缺失或请求失败时返回空列表。</summary>
    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        string providerId,
        TrackQuery query,
        CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(providerId);
        if (provider is null)
        {
            return [];
        }

        try
        {
            return await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>读取候选封面字节，用于搜索结果列表的缩略图；失败返回 null。</summary>
    public async Task<byte[]?> GetCoverBytesAsync(
        string providerId,
        MetadataCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(providerId);
        if (provider is null)
        {
            return null;
        }

        try
        {
            return await provider.GetCoverAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>把用户选定的候选应用为本地封面/歌词缓存（不改动音乐文件）。</summary>
    public async Task<ScrapeResult> ApplyAsync(
        Track track,
        string providerId,
        MetadataCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(providerId);
        if (provider is null)
        {
            return new ScrapeResult(false, false, "数据源不可用。");
        }

        AppPaths.EnsureCreated();

        var coverUpdated = await TrySaveCoverAsync(track, provider, candidate, cancellationToken)
            .ConfigureAwait(false);
        var lyricsUpdated = await TrySaveLyricsAsync(track, provider, candidate, cancellationToken)
            .ConfigureAwait(false);

        if (coverUpdated || lyricsUpdated)
        {
            await PersistAsync(track, cancellationToken).ConfigureAwait(false);
        }

        if (lyricsUpdated)
        {
            _lyricsService.Invalidate(track.Id);
        }

        var message = (coverUpdated, lyricsUpdated) switch
        {
            (true, true) => $"已应用{provider.DisplayName}的封面和歌词。",
            (true, false) => provider.SupportsLyrics
                ? $"已应用{provider.DisplayName}的封面，该结果没有歌词。"
                : $"已应用{provider.DisplayName}的封面（此数据源不提供歌词）。",
            (false, true) => $"已应用{provider.DisplayName}的歌词，该结果没有封面。",
            _ => "该结果没有可用的封面或歌词。",
        };

        return new ScrapeResult(coverUpdated, lyricsUpdated, message);
    }

    /// <summary>把界面上编辑后的元数据落库（只写本地曲库，不改音乐文件）。</summary>
    public async Task SaveMetadataAsync(
        Track track,
        string title,
        string artist,
        string album,
        string year,
        CancellationToken cancellationToken = default)
    {
        track.Title = title.Trim();
        track.Artist = artist.Trim();
        track.Album = album.Trim();

        if (int.TryParse(year.Trim(), out var parsedYear))
        {
            track.Year = parsedYear;
        }

        await _libraryStore.UpsertTracksAsync([track], cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TrySaveCoverAsync(
        Track track,
        IMetadataProvider provider,
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        byte[]? data;
        try
        {
            data = await provider.GetCoverAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (data is null || data.Length == 0)
        {
            return false;
        }

        // 文件名带内容哈希：PathToBitmapConverter 按路径缓存且不校验改时间，
        // 名称随内容变化才能让界面刷新到新封面。
        var extension = LooksLikePng(data) ? ".png" : ".jpg";
        var hash = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant()[..8];
        var fullPath = Path.Combine(AppPaths.CoversDir, $"{SafeId(track.Id)}_{hash}{extension}");

        await File.WriteAllBytesAsync(fullPath, data, cancellationToken).ConfigureAwait(false);
        track.CoverPath = fullPath;
        return true;
    }

    private static async Task<bool> TrySaveLyricsAsync(
        Track track,
        IMetadataProvider provider,
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (!provider.SupportsLyrics)
        {
            return false;
        }

        string? lyrics;
        try
        {
            lyrics = await provider.GetLyricsAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(lyrics))
        {
            return false;
        }

        await File.WriteAllTextAsync(
                AppPaths.LyricsFileFor(track.Id), lyrics, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task PersistAsync(Track track, CancellationToken cancellationToken)
    {
        try
        {
            await _libraryStore.UpsertTracksAsync([track], cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 落库失败不影响本次显示（内存中的 CoverPath 已更新）。
        }
    }

    private static string SafeId(string trackId)
        => trackId.Replace(':', '_').Replace('/', '_').Replace('\\', '_');

    private static bool LooksLikePng(byte[] data)
        => data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
}