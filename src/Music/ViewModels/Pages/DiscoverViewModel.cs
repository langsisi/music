using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Models;
using Music.Services.Audio;
using Music.Services.Online;

namespace Music.ViewModels.Pages;

/// <summary>
/// 发现页：在线搜索（GD 音乐台），可试听或把曲目下载到指定音源。
/// 每日 / 随机推荐已移到首页，这里只保留搜索。
/// </summary>
public partial class DiscoverViewModel : PageViewModel, IOnlineTrackHost
{
    /// <summary>列表缩略图请求的封面尺寸。</summary>
    private const int CoverThumbnailSize = 120;

    private readonly OnlineMusicService _music;
    private readonly OnlineDownloadService _download;
    private readonly PlaybackService _playback;

    public DiscoverViewModel(
        OnlineMusicService music,
        OnlineDownloadService download,
        PlaybackService playback)
    {
        _music = music;
        _download = download;
        _playback = playback;

        Sources = OnlineSources.All;
        SelectedSource = Sources[0];
        Bitrates = OnlineSources.Bitrates;
        SelectedBitrate = Bitrates.FirstOrDefault(option => option.Value == 320) ?? Bitrates[0];

        RefreshTargets();
    }

    public override string Title => "发现";

    public override string Description => "搜索在线音乐，试听或下载到你的音源";

    /// <summary>进入发现页时刷新下载目标（设置里增删音源后回来即为最新）。</summary>
    public void Activate() => RefreshTargets();

    // ---------------- 搜索条件 ----------------

    public IReadOnlyList<OnlineSourceOption> Sources { get; }

    [ObservableProperty]
    private OnlineSourceOption? _selectedSource;

    /// <summary>下载音质选项。</summary>
    public IReadOnlyList<BitrateOption> Bitrates { get; }

    [ObservableProperty]
    private BitrateOption? _selectedBitrate;

    /// <summary>下载目标：本机音乐目录，或已配置的本地文件夹 / FTP 音源。</summary>
    public ObservableCollection<OnlineDownloadTarget> DownloadTargets { get; } = [];

    [ObservableProperty]
    private OnlineDownloadTarget? _selectedDownloadTarget;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // ---------------- 搜索结果 ----------------

    public ObservableCollection<OnlineTrackViewModel> SearchResults { get; } = [];

    public bool HasSearchResults => SearchResults.Count > 0;

    [ObservableProperty]
    private bool _isSearching;

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyResultHint));

    /// <summary>是否已经执行过搜索（用于决定空结果提示的显示）。</summary>
    [ObservableProperty]
    private bool _hasSearched;

    partial void OnHasSearchedChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyResultHint));

    /// <summary>搜过但没有结果且不在搜索中：显示空结果提示。</summary>
    public bool ShowEmptyResultHint => HasSearched && !IsSearching && !HasSearchResults;

    /// <summary>顶部状态提示：搜索进度、下载结果等。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatusText));

    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);

    // ---------------- 命令 ----------------

    [RelayCommand]
    private async Task SearchAsync()
    {
        var keyword = SearchText.Trim();
        if (IsSearching || keyword.Length == 0 || SelectedSource is not { } source)
        {
            return;
        }

        IsSearching = true;
        HasSearched = true;
        StatusText = "正在搜索…";
        SearchResults.Clear();
        RaiseSearchState();

        try
        {
            var results = await _music.SearchAsync(source.Id, keyword).ConfigureAwait(true);
            OnlineTrackViewModel.Populate(SearchResults, results, this);
            RaiseSearchState();

            StatusText = results.Count == 0
                ? $"「{source.Name}」没有搜到结果，换个关键词或音源试试。"
                : $"找到 {results.Count} 首，可直接试听或下载。";

            _ = OnlineTrackViewModel.LoadCoversAsync(SearchResults.ToList(), _music, CoverThumbnailSize);
        }
        catch (Exception ex)
        {
            StatusText = $"搜索失败：{ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>重新读取可用的下载目标（在设置里增删音源后点此刷新）。</summary>
    [RelayCommand]
    private void RefreshTargets()
    {
        var previous = SelectedDownloadTarget?.Id;

        DownloadTargets.Clear();
        foreach (var target in _download.GetTargets())
        {
            DownloadTargets.Add(target);
        }

        SelectedDownloadTarget = DownloadTargets.FirstOrDefault(target => target.Id == previous)
            ?? DownloadTargets.FirstOrDefault();
    }

    // ---------------- IOnlineTrackHost ----------------

    public bool SupportsDownload => true;

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

    public async Task DownloadAsync(OnlineTrackViewModel item)
    {
        if (item.IsDownloading)
        {
            return;
        }

        if (SelectedDownloadTarget is not { } target)
        {
            StatusText = "没有可用的下载目标，请先在「设置 → 音源」里添加。";
            return;
        }

        item.IsDownloading = true;
        item.DownloadText = "下载中…";
        // 窄屏隐藏了行内状态，进度同步到顶部状态栏，点完立刻能看到反馈。
        StatusText = $"正在下载「{item.DisplayTitle}」…";

        try
        {
            var bitrate = SelectedBitrate?.Value ?? 320;
            var progress = new Progress<double>(value =>
            {
                item.DownloadText = $"下载中… {value * 100:0}%";
                StatusText = $"正在下载「{item.DisplayTitle}」… {value * 100:0}%";
            });

            var location = await _download
                .DownloadAsync(item.Track, target, bitrate, progress)
                .ConfigureAwait(true);

            item.DownloadText = "已下载";
            StatusText = $"「{item.DisplayTitle}」已下载到：{location}";
        }
        catch (Exception ex)
        {
            item.DownloadText = "失败";
            StatusText = $"下载「{item.DisplayTitle}」失败：{ex.Message}";
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    // ---------------- 内部实现 ----------------

    private void RaiseSearchState()
    {
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(ShowEmptyResultHint));
    }
}