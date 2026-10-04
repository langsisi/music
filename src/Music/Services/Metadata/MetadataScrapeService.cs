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
/// 缓存到本地（封面 / 歌词缓存）并落库更新封面路径与用户编辑过的元数据；
/// 若开启 <c>ScrapeWriteBack</c>，还会把封面与歌词写回音源本身（本地文件 / FTP / SMB / WebDAV）。
/// </summary>
public sealed class MetadataScrapeService
{
    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly ILibraryStore _libraryStore;
    private readonly LyricsService _lyricsService;
    private readonly SourceWriteBackService _writeBack;

    public MetadataScrapeService(
        IEnumerable<IMetadataProvider> providers,
        ILibraryStore libraryStore,
        LyricsService lyricsService,
        SourceWriteBackService writeBack)
    {
        _providers = providers.ToList();
        _libraryStore = libraryStore;
        _lyricsService = lyricsService;
        _writeBack = writeBack;
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

    /// <summary>
    /// 把用户选定的候选应用为本地封面/歌词缓存，并把「封面 + 歌词 + 编辑后的元数据」一次性写回音源。
    /// 元数据随同一次写回落地，避免对同一个远端文件重复下载上传。
    /// </summary>
    public async Task<ScrapeResult> ApplyAsync(
        Track track,
        string providerId,
        MetadataCandidate candidate,
        string? title = null,
        string? artist = null,
        string? album = null,
        string? year = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(providerId);
        if (provider is null)
        {
            return new ScrapeResult(false, false, "数据源不可用。");
        }

        AppPaths.EnsureCreated();

        // 先把界面上编辑过的元数据落到 track 上，下面一次写回就同时带上元数据。
        if (title is not null)
        {
            track.Title = title.Trim();
        }

        if (artist is not null)
        {
            track.Artist = artist.Trim();
        }

        if (album is not null)
        {
            track.Album = album.Trim();
        }

        if (int.TryParse(year?.Trim(), out var parsedYear))
        {
            track.Year = parsedYear;
        }

        var coverBytes = await TrySaveCoverAsync(track, provider, candidate, cancellationToken)
            .ConfigureAwait(false);
        var lyrics = await TrySaveLyricsAsync(track, provider, candidate, cancellationToken)
            .ConfigureAwait(false);

        var coverUpdated = coverBytes is not null;
        var lyricsUpdated = lyrics is not null;

        await PersistAsync(track, cancellationToken).ConfigureAwait(false);

        if (lyricsUpdated)
        {
            _lyricsService.Invalidate(track.Id);
        }

        var message = BuildMessage(provider, coverUpdated, lyricsUpdated);

        var writeBack = await _writeBack
            .WriteBackAsync(
                track,
                coverBytes,
                lyrics,
                title: track.Title,
                artist: track.Artist,
                album: track.Album,
                progress: progress,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        message = writeBack.Status switch
        {
            WriteBackStatus.Succeeded => $"{message} 封面、歌词与元信息已写回音源。",
            WriteBackStatus.Failed => $"{message} 写回音源失败：{writeBack.Message}",
            _ => writeBack.Message.Length > 0 ? $"{message} 未写回音源：{writeBack.Message}" : message,
        };

        return new ScrapeResult(coverUpdated, lyricsUpdated, message);
    }

    private static string BuildMessage(IMetadataProvider provider, bool coverUpdated, bool lyricsUpdated)
        => (coverUpdated, lyricsUpdated) switch
        {
            (true, true) => $"已应用{provider.DisplayName}的封面和歌词。",
            (true, false) => provider.SupportsLyrics
                ? $"已应用{provider.DisplayName}的封面，该结果没有歌词。"
                : $"已应用{provider.DisplayName}的封面（此数据源不提供歌词）。",
            (false, true) => $"已应用{provider.DisplayName}的歌词，该结果没有封面。",
            _ => "该结果没有可用的封面或歌词。",
        };

    /// <summary>
    /// 把界面上编辑后的元数据落库，并写回音源文件本身的标签。
    /// Navidrome 与在线曲目没有可写入口，由 <see cref="SourceWriteBackService"/> 自动跳过；
    /// 封面 / 歌词传 null，保持文件里的原值不动。
    /// </summary>
    public async Task<WriteBackResult> SaveMetadataAsync(
        Track track,
        string title,
        string artist,
        string album,
        string year,
        IProgress<string>? progress = null,
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

        if (track.Title.Length == 0 && track.Artist.Length == 0 && track.Album.Length == 0)
        {
            return WriteBackResult.Skipped;
        }

        return await _writeBack
            .WriteBackAsync(
                track,
                coverBytes: null,
                lyrics: null,
                title: track.Title,
                artist: track.Artist,
                album: track.Album,
                progress: progress,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>下载封面并写入本地封面缓存，返回封面字节（没有则 null）。</summary>
    private static async Task<byte[]?> TrySaveCoverAsync(
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
            return null;
        }

        if (data is null || data.Length == 0)
        {
            return null;
        }

        // 文件名带内容哈希：PathToBitmapConverter 按路径缓存且不校验改时间，
        // 名称随内容变化才能让界面刷新到新封面。
        var extension = LooksLikePng(data) ? ".png" : ".jpg";
        var hash = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant()[..8];
        var fullPath = Path.Combine(AppPaths.CoversDir, $"{SafeId(track.Id)}_{hash}{extension}");

        await File.WriteAllBytesAsync(fullPath, data, cancellationToken).ConfigureAwait(false);
        track.CoverPath = fullPath;
        return data;
    }

    /// <summary>下载歌词并写入本地歌词缓存，返回歌词文本（没有则 null）。</summary>
    private static async Task<string?> TrySaveLyricsAsync(
        Track track,
        IMetadataProvider provider,
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (!provider.SupportsLyrics)
        {
            return null;
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
            return null;
        }

        if (string.IsNullOrWhiteSpace(lyrics))
        {
            return null;
        }

        await File.WriteAllTextAsync(
                AppPaths.LyricsFileFor(track.Id), lyrics, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return lyrics;
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