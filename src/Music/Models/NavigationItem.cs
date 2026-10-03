using Avalonia.Media;
using Music.ViewModels.Pages;

namespace Music.Models;

/// <summary>导航入口：左侧导航栏与底部标签栏共用同一份数据。</summary>
public sealed class NavigationItem
{
    public required string Title { get; init; }

    public required Geometry Icon { get; init; }

    public required PageViewModel Page { get; init; }
}
