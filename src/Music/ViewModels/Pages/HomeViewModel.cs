using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Controls;
using Music.Models;
using Music.Services.Audio;
using Music.Services.Library;

namespace Music.ViewModels.Pages;

public partial class HomeViewModel : PageViewModel
{
    private readonly PlaybackService _playback;
    private readonly ILibraryStore _libraryStore;

    public HomeViewModel(PlaybackService playback, ILibraryStore libraryStore)
    {
        _playback = playback;
        _libraryStore = libraryStore;

        _playback.Changed += OnPlaybackChanged;
        _libraryStore.Changed += OnLibraryChanged;

        _ = RefreshRecentAsync();
        _ = RefreshPlaylistsAsync();
    }

    public override string Title => "首页";

    public override string Description => "最近播放与你的歌单";

    /// <summary>请求切换到音乐库并展示收藏。</summary>
    public event Action? FavoritesRequested;

    /// <summary>请求切换到音乐库并展示指定分类。</summary>
    public event Action<string>? CategoryRequested;

    // ---------------- 最近播放 ----------------

    public ObservableCollection<Track> RecentTracks { get; } = [];

    public bool HasRecentTracks => RecentTracks.Count > 0;

    [RelayCommand]
    private async Task PlayRecentAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var queue = RecentTracks.ToList();
        var index = queue.FindIndex(item => item.Id == track.Id);
        await _playback.PlayQueueAsync(queue, index < 0 ? 0 : index);
    }

    private void OnPlaybackChanged(object? sender, EventArgs e) => _ = RefreshRecentAsync();

    private Task RefreshRecentAsync()
    {
        var snapshot = _playback.RecentTracks;

        // 内容没变化就不重建集合，避免横滑列表闪动。
        if (snapshot.Count == RecentTracks.Count &&
            snapshot.Select((t, i) => t.Id == RecentTracks[i].Id).All(equal => equal))
        {
            return Task.CompletedTask;
        }

        RecentTracks.Clear();
        foreach (var track in snapshot)
        {
            RecentTracks.Add(track);
        }

        OnPropertyChanged(nameof(HasRecentTracks));
        return Task.CompletedTask;
    }

    // ---------------- 歌单 ----------------

    /// <summary>收藏的曲目数，用于首页「我喜欢的」大卡片。</summary>
    [ObservableProperty]
    public partial int FavoriteCount { get; set; }

    public string FavoriteCountText => $"{FavoriteCount} 首歌曲";

    /// <summary>用户自建分类歌单（「我喜欢的」合集单独做成大卡片，不在此集合内）。</summary>
    public ObservableCollection<PlaylistItemViewModel> Playlists { get; } = [];

    public bool HasPlaylists => Playlists.Count > 0;

    [RelayCommand]
    private void OpenFavorites() => FavoritesRequested?.Invoke();

    [RelayCommand]
    private void OpenPlaylist(PlaylistItemViewModel? item)
    {
        if (item?.CategoryId is { } categoryId)
        {
            CategoryRequested?.Invoke(categoryId);
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => _ = RefreshPlaylistsAsync());

    private async Task RefreshPlaylistsAsync()
    {
        var categories = await _libraryStore.GetCategoriesAsync();
        var favoriteIds = await _libraryStore.GetFavoriteTrackIdsAsync();
        FavoriteCount = favoriteIds.Count;
        OnPropertyChanged(nameof(FavoriteCountText));

        var items = new List<PlaylistItemViewModel>();
        foreach (var category in categories)
        {
            var tracks = await _libraryStore.GetCategoryTracksAsync(category.Id);
            items.Add(new PlaylistItemViewModel
            {
                Name = category.Name,
                Count = tracks.Count,
                Icon = AppIcons.MusicNote,
                IsFavorite = false,
                CategoryId = category.Id,
            });
        }

        Playlists.Clear();
        foreach (var item in items)
        {
            Playlists.Add(item);
        }

        OnPropertyChanged(nameof(HasPlaylists));
    }
}
