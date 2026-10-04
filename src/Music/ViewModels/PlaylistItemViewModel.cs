using Avalonia.Media;

namespace Music.ViewModels;

/// <summary>首页「歌单」区的一张卡片：收藏合集或用户自建分类。</summary>
public sealed class PlaylistItemViewModel
{
    public required string Name { get; init; }

    public required int Count { get; init; }

    public required Geometry Icon { get; init; }

    /// <summary>收藏合集为 true；自建分类为 false。</summary>
    public required bool IsFavorite { get; init; }

    /// <summary>自建分类的 Id；收藏合集为 null。</summary>
    public string? CategoryId { get; init; }

    public string CountText => Count > 0 ? $"{Count} 首歌曲" : "0 首歌曲";
}
