namespace Music.ViewModels;

/// <summary>曲库筛选项的种类。</summary>
public enum LibraryFilterKind
{
    All = 0,
    Favorites = 1,
    Category = 2,
}

/// <summary>曲库页顶部的一个筛选标签：全部 / 收藏 / 某个分类。</summary>
public sealed record LibraryFilter(LibraryFilterKind Kind, string Title, string? CategoryId = null);
