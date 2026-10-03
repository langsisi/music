using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Models;
using Music.Services.Audio;
using Music.Services.Library;

namespace Music.ViewModels.Pages;

public partial class LibraryViewModel : PageViewModel
{
    private readonly ILibraryStore _libraryStore;
    private readonly PlaybackService _playback;

    /// <summary>本页自己发起的写入同样会触发 Changed，此时不需要重建列表（否则滚动位置会被重置）。</summary>
    private bool _writeInProgress;

    private IReadOnlyList<Category> _categories = [];

    public LibraryViewModel(ILibraryStore libraryStore, PlaybackService playback)
    {
        _libraryStore = libraryStore;
        _playback = playback;

        _libraryStore.Changed += OnLibraryChanged;
        _ = RefreshAsync();
    }

    public override string Title => "音乐库";

    public override string Description => TrackCount > 0
        ? $"共 {TrackCount} 首曲目"
        : "扫描音源后，曲目会汇总到这里";

    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];

    /// <summary>筛选项：全部 / 收藏 / 每个分类。</summary>
    public ObservableCollection<LibraryFilter> Filters { get; } = [];

    [ObservableProperty]
    public partial LibraryFilter? SelectedFilter { get; set; }

    partial void OnSelectedFilterChanged(LibraryFilter? value)
    {
        OnPropertyChanged(nameof(CanDeleteCategory));
        _ = RefreshTracksAsync();
    }

    /// <summary>只有筛选到某个分类时才允许删除该分类。</summary>
    public bool CanDeleteCategory => SelectedFilter?.Kind == LibraryFilterKind.Category;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => _ = RefreshTracksAsync();

    [ObservableProperty]
    public partial string NewCategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TrackCount { get; set; }

    partial void OnTrackCountChanged(int value) => OnPropertyChanged(nameof(Description));

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public bool HasTracks => TrackCount > 0;

    // ---------------- 播放 ----------------

    [RelayCommand]
    private async Task PlayAll()
    {
        if (Tracks.Count > 0)
        {
            await _playback.PlayQueueAsync(Tracks.Select(row => row.Track).ToList(), 0);
        }
    }

    [RelayCommand]
    private async Task PlayTrack(TrackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var queue = Tracks.Select(item => item.Track).ToList();
        var index = Tracks.IndexOf(row);
        await _playback.PlayQueueAsync(queue, index < 0 ? 0 : index);
    }

    // ---------------- 收藏 ----------------

    [RelayCommand]
    private async Task ToggleFavoriteAsync(TrackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsFavorite = !row.IsFavorite;

        await WriteAsync(() => _libraryStore.SetFavoriteAsync(row.Id, row.IsFavorite));

        // 在「收藏」筛选下取消收藏，这一行应当立刻消失。
        if (SelectedFilter?.Kind == LibraryFilterKind.Favorites && !row.IsFavorite)
        {
            await RefreshTracksAsync();
        }
    }

    // ---------------- 分类 ----------------

    [RelayCommand]
    private async Task CreateCategoryAsync()
    {
        var name = NewCategoryName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        var created = await WriteAsync(() => _libraryStore.CreateCategoryAsync(name));
        NewCategoryName = string.Empty;

        await RefreshFiltersAsync();

        // 新建后直接切到该分类，方便马上往里加曲目。
        SelectedFilter = Filters.FirstOrDefault(filter => filter.CategoryId == created.Id);
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedFilter is not { Kind: LibraryFilterKind.Category, CategoryId: { } categoryId })
        {
            return;
        }

        await WriteAsync(() => _libraryStore.DeleteCategoryAsync(categoryId));
        await RefreshFiltersAsync();
        SelectedFilter = Filters.FirstOrDefault();
    }

    /// <summary>由列表行回调：把曲目加入 / 移出分类。</summary>
    private Task WriteTrackCategoryAsync(string categoryId, string trackId, bool isMember)
        => WriteAsync(() => isMember
            ? _libraryStore.AddTrackToCategoryAsync(categoryId, trackId)
            : _libraryStore.RemoveTrackFromCategoryAsync(categoryId, trackId));

    /// <summary>执行一次本页发起的写入，期间忽略由它自己触发的 Changed。</summary>
    private async Task<T> WriteAsync<T>(Func<Task<T>> action)
    {
        _writeInProgress = true;
        try
        {
            return await action();
        }
        finally
        {
            _writeInProgress = false;
        }
    }

    private async Task WriteAsync(Func<Task> action) => await WriteAsync<object?>(async () =>
    {
        await action();
        return null;
    });

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshFiltersAsync();
        await RefreshTracksAsync();
    }

    /// <summary>重建筛选标签：全部 + 收藏 + 所有分类，并尽量保持当前选中项。</summary>
    private async Task RefreshFiltersAsync()
    {
        _categories = await _libraryStore.GetCategoriesAsync();

        var previousCategoryId = SelectedFilter?.CategoryId;
        var previousKind = SelectedFilter?.Kind ?? LibraryFilterKind.All;

        Filters.Clear();
        Filters.Add(new LibraryFilter(LibraryFilterKind.All, "全部"));
        Filters.Add(new LibraryFilter(LibraryFilterKind.Favorites, "收藏"));

        foreach (var category in _categories)
        {
            Filters.Add(new LibraryFilter(LibraryFilterKind.Category, category.Name, category.Id));
        }

        var restored = previousCategoryId is null
            ? Filters.FirstOrDefault(filter => filter.Kind == previousKind)
            : Filters.FirstOrDefault(filter => filter.CategoryId == previousCategoryId);

        // 值没变时 setter 不会触发刷新，这里显式补一次。
        if (restored == SelectedFilter)
        {
            await RefreshTracksAsync();
            return;
        }

        SelectedFilter = restored ?? Filters[0];
    }

    private async Task RefreshTracksAsync()
    {
        var query = SearchText?.Trim() ?? string.Empty;

        IReadOnlyList<Track> tracks;
        if (SelectedFilter is { Kind: LibraryFilterKind.Category, CategoryId: { } categoryId })
        {
            tracks = await _libraryStore.GetCategoryTracksAsync(categoryId);
            if (query.Length > 0)
            {
                tracks = tracks.Where(Matches).ToList();
            }
        }
        else
        {
            tracks = query.Length == 0
                ? await _libraryStore.GetTracksAsync()
                : await _libraryStore.SearchTracksAsync(query);
        }

        var favoriteIds = await _libraryStore.GetFavoriteTrackIdsAsync();
        var categoryMap = await _libraryStore.GetTrackCategoryMapAsync();

        if (SelectedFilter?.Kind == LibraryFilterKind.Favorites)
        {
            tracks = tracks.Where(track => favoriteIds.Contains(track.Id)).ToList();
        }

        Tracks.Clear();
        foreach (var track in tracks)
        {
            var row = new TrackRowViewModel(track, WriteTrackCategoryAsync)
            {
                IsFavorite = favoriteIds.Contains(track.Id),
            };

            row.SetCategories(_categories, categoryMap.GetValueOrDefault(track.Id) ?? []);
            row.CategoryChanged += OnRowCategoryChanged;
            Tracks.Add(row);
        }

        TrackCount = tracks.Count;
        IsEmpty = tracks.Count == 0;
        OnPropertyChanged(nameof(HasTracks));

        bool Matches(Track track)
            => track.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
               || track.Album.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>在分类筛选下把曲目移出该分类后，这一行应当立刻消失。</summary>
    private void OnRowCategoryChanged(TrackRowViewModel row)
    {
        if (SelectedFilter?.Kind == LibraryFilterKind.Category)
        {
            _ = RefreshTracksAsync();
        }
    }

    /// <summary>曲库内容变化（扫描、删除音源、其它页面改收藏）可能在后台线程触发，统一切回 UI 线程刷新。</summary>
    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        if (_writeInProgress)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = RefreshAsync());
    }
}
