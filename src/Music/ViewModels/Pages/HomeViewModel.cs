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
using Music.Services.Online;

namespace Music.ViewModels.Pages;

public partial class HomeViewModel : PageViewModel, IOnlineTrackHost
{
    /// <summary>推荐列表缩略图请求的封面尺寸。</summary>
    private const int CoverThumbnailSize = 120;

    private readonly PlaybackService _playback;
    private readonly ILibraryStore _libraryStore;
    private readonly OnlineMusicService _music;

    public HomeViewModel(
        PlaybackService playback,
        ILibraryStore libraryStore,
        OnlineMusicService music)
    {
        _playback = playback;
        _libraryStore = libraryStore;
        _music = music;

        _playback.Changed += OnPlaybackChanged;
        _libraryStore.Changed += OnLibraryChanged;

        _ = RefreshRecentAsync();
        _ = RefreshPlaylistsAsync();
        _ = ReloadDailyAsync();
        _ = ReloadRandomAsync();
    }

    public override string Title => "首页";

    public override string Description => "每日推荐、随机推荐、最近播放与你的歌单";

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

    // ---------------- 在线推荐 ----------------

    public ObservableCollection<OnlineTrackViewModel> DailyRecommendations { get; } = [];

    public ObservableCollection<OnlineTrackViewModel> RandomRecommendations { get; } = [];

    public bool HasDailyRecommendations => DailyRecommendations.Count > 0;

    public bool HasRandomRecommendations => RandomRecommendations.Count > 0;

    [ObservableProperty]
    private bool _isDailyLoading;

    partial void OnIsDailyLoadingChanged(bool value) => RaiseDailyState();

    [ObservableProperty]
    private bool _isRandomLoading;

    partial void OnIsRandomLoadingChanged(bool value) => RaiseRandomState();

    /// <summary>推荐位为空时卡片里的文案（加载中用「正在加载…」，失败/为空引导刷新）。</summary>
    public string DailyHint => IsDailyLoading ? "正在加载…" : "暂无推荐，点右上角刷新";

    public string RandomHint => IsRandomLoading ? "正在加载…" : "暂无推荐，点右上角刷新";

    [RelayCommand]
    private async Task RefreshDailyAsync()
    {
        if (IsDailyLoading)
        {
            return;
        }

        await ReloadDailyAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshRandomAsync()
    {
        if (IsRandomLoading)
        {
            return;
        }

        await ReloadRandomAsync().ConfigureAwait(true);
    }

    private async Task ReloadDailyAsync()
    {
        IsDailyLoading = true;
        try
        {
            var daily = await _music
                .GetDailyRecommendationsAsync(OnlineSources.All[0].Id)
                .ConfigureAwait(true);

            OnlineTrackViewModel.Populate(DailyRecommendations, daily, this);
            RaiseDailyState();

            _ = OnlineTrackViewModel.LoadCoversAsync(DailyRecommendations.ToList(), _music, CoverThumbnailSize);
        }
        catch (Exception)
        {
            // 推荐失败不打扰用户，保留右上角刷新入口。
        }
        finally
        {
            IsDailyLoading = false;
        }
    }

    private async Task ReloadRandomAsync()
    {
        IsRandomLoading = true;
        try
        {
            var random = await _music
                .GetRandomRecommendationsAsync(OnlineSources.All[0].Id)
                .ConfigureAwait(true);

            OnlineTrackViewModel.Populate(RandomRecommendations, random, this);
            RaiseRandomState();

            _ = OnlineTrackViewModel.LoadCoversAsync(RandomRecommendations.ToList(), _music, CoverThumbnailSize);
        }
        catch (Exception)
        {
            // 同上。
        }
        finally
        {
            IsRandomLoading = false;
        }
    }

    private void RaiseDailyState()
    {
        OnPropertyChanged(nameof(HasDailyRecommendations));
        OnPropertyChanged(nameof(DailyHint));
    }

    private void RaiseRandomState()
    {
        OnPropertyChanged(nameof(HasRandomRecommendations));
        OnPropertyChanged(nameof(RandomHint));
    }

    // ---------------- IOnlineTrackHost ----------------

    /// <summary>首页推荐只提供试听；下载需在发现页选择目标音源。</summary>
    public bool SupportsDownload => false;

    public async Task PlayFromAsync(IList<OnlineTrackViewModel> list, OnlineTrackViewModel item)
    {
        if (list.Count == 0)
        {
            return;
        }

        var index = Math.Max(0, list.IndexOf(item));
        var tracks = list.Select(row => row.Track.ToPlayableTrack()).ToList();

        await _music.PrepareForPlaybackAsync(item.Track, tracks[index]).ConfigureAwait(true);
        await _playback.PlayQueueAsync(tracks, index).ConfigureAwait(true);
    }

    public Task DownloadAsync(OnlineTrackViewModel item) => Task.CompletedTask;

    // ---------------- 歌单 ----------------

    /// <summary>收藏的曲目数，用于首页「我喜欢的」大卡片。</summary>
    [ObservableProperty]
    private int _favoriteCount;

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
