using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Library;

/// <summary>本地曲库索引（SQLite）。所有音源扫描到的曲目都会汇总到这里。</summary>
public interface ILibraryStore
{
    /// <summary>曲库内容发生变化时触发（可能在后台线程），用于让界面刷新。</summary>
    event System.EventHandler? Changed;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>新增或更新曲目（按 <see cref="Track.Id"/> 覆盖）。</summary>
    Task UpsertTracksAsync(IReadOnlyCollection<Track> tracks, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Track>> GetTracksAsync(string? sourceId = null, CancellationToken cancellationToken = default);

    Task<Track?> GetTrackAsync(string trackId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Track>> SearchTracksAsync(string query, CancellationToken cancellationToken = default);

    Task<int> GetTrackCountAsync(CancellationToken cancellationToken = default);

    /// <summary>某个音源已入库的曲目数，用于判断是否还需要首次扫描。</summary>
    Task<int> GetSourceTrackCountAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>移除某个音源下的全部曲目（音源被删除或重新扫描前调用）。</summary>
    Task RemoveSourceTracksAsync(string sourceId, CancellationToken cancellationToken = default);

    // ---------------- 收藏 ----------------

    /// <summary>已收藏的曲目 Id 集合，用于给列表批量打标记。</summary>
    Task<IReadOnlyCollection<string>> GetFavoriteTrackIdsAsync(CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(string trackId, bool isFavorite, CancellationToken cancellationToken = default);

    // ---------------- 分类 ----------------

    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<Category> CreateCategoryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>删除分类本身以及它下面所有的归类关系。</summary>
    Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Track>> GetCategoryTracksAsync(
        string categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>曲目 Id → 所属分类 Id 集合，用于在列表里标记归属。</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetTrackCategoryMapAsync(
        CancellationToken cancellationToken = default);

    Task AddTrackToCategoryAsync(
        string categoryId,
        string trackId,
        CancellationToken cancellationToken = default);

    Task RemoveTrackFromCategoryAsync(
        string categoryId,
        string trackId,
        CancellationToken cancellationToken = default);
}
