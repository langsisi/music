using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Controls;
using Music.Models;

namespace Music.ViewModels;

/// <summary>把曲目加入 / 移出分类。由页面提供，便于页面统一处理写入与刷新。</summary>
public delegate Task TrackCategoryWriter(string categoryId, string trackId, bool isMember);

/// <summary>
/// 曲库列表中的一行。包一层是为了在 <see cref="Track"/> 之外携带界面状态
/// （是否已收藏、所属分类），曲目模型本身只做数据映射。
/// </summary>
public partial class TrackRowViewModel : ObservableObject
{
    private readonly TrackCategoryWriter _writeCategory;

    public TrackRowViewModel(Track track, TrackCategoryWriter writeCategory)
    {
        Track = track;
        _writeCategory = writeCategory;
        Categories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCategories));
    }

    public Track Track { get; }

    public string Id => Track.Id;

    public string DisplayTitle => Track.DisplayTitle;

    public string Subtitle => Track.Subtitle;

    /// <summary>音源显示名，由页面按曲目来源解析后注入，用于区分来自不同音源的同名曲目。</summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>是否显示音源标签（行尾固定小徽章，不随副标题截断）。</summary>
    public bool HasSourceName => !string.IsNullOrEmpty(SourceName);

    public string DurationText => Track.DurationText;

    public string? CoverPath => Track.CoverPath;

    /// <summary>Navidrome 没有删除接口，其余音源都能从列表里直接删除。</summary>
    public bool CanDelete => Track.SourceType != MusicSourceType.Navidrome;

    /// <summary>
    /// 由音乐库页面注入的「确认删除」命令。列表里的删除按钮只负责弹出确认菜单，
    /// 菜单项通过它执行删除（菜单在弹层里，取不到 ListBox 上的命令，故挂到行上）。
    /// </summary>
    public ICommand? DeleteCommand { get; init; }

    /// <summary>弹层里没有任何分类时，就地新建一个并加入本曲目（同样因为弹层取不到页面命令）。</summary>
    public ICommand? CreateCategoryCommand { get; init; }

    /// <summary>弹层内「新建分类」输入的名称。</summary>
    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    // ---------------- 收藏（写入由页面统一发起，见 LibraryViewModel） ----------------

    [ObservableProperty]
    private bool _isFavorite;

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteIcon));

    public Geometry FavoriteIcon => IsFavorite ? AppIcons.Heart : AppIcons.HeartOutline;

    // ---------------- 分类 ----------------

    public ObservableCollection<TrackCategoryItem> Categories { get; } = [];

    public bool HasCategories => Categories.Count > 0;

    /// <summary>所属分类名称，展示在副标题里；没有归属时为空。</summary>
    public string CategoryText => Categories.Count == 0
        ? string.Empty
        : string.Join("、", Categories.Where(item => item.IsMember).Select(item => item.Name));

    /// <summary>归类关系被本行改动后触发，便于页面在分类筛选下刷新列表。</summary>
    public event Action<TrackRowViewModel>? CategoryChanged;

    public void SetCategories(IReadOnlyList<Category> categories, IReadOnlyCollection<string> memberIds)
    {
        Categories.Clear();
        foreach (var category in categories)
        {
            Categories.Add(new TrackCategoryItem(this, category, memberIds.Contains(category.Id)));
        }

        OnPropertyChanged(nameof(CategoryText));
    }

    internal async Task ApplyCategoryAsync(TrackCategoryItem item, bool isMember)
    {
        item.IsMember = isMember;
        await _writeCategory(item.CategoryId, Id, isMember);

        OnPropertyChanged(nameof(CategoryText));
        CategoryChanged?.Invoke(this);
    }
}

/// <summary>某个曲目与某个分类的归属关系。放在弹层里，因此自己持有切换命令。</summary>
public partial class TrackCategoryItem : ObservableObject
{
    private readonly TrackRowViewModel _owner;

    public TrackCategoryItem(TrackRowViewModel owner, Category category, bool isMember)
    {
        _owner = owner;
        CategoryId = category.Id;
        Name = category.Name;
        IsMember = isMember;
    }

    public string CategoryId { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isMember;

    partial void OnIsMemberChanged(bool value) => OnPropertyChanged(nameof(MarkIcon));

    public Geometry MarkIcon => IsMember ? AppIcons.Heart : AppIcons.Plus;

    [RelayCommand]
    private Task ToggleAsync() => _owner.ApplyCategoryAsync(this, !IsMember);
}
