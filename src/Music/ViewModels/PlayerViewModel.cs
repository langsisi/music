using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Controls;
using Music.Models;
using Music.Services;
using Music.Services.Audio;
using Music.Services.Broadcast;
using Music.Services.Cache;
using Music.Services.Library;
using Music.Services.Lyrics;
using Music.Services.Metadata;
using Music.Services.SystemMedia;

namespace Music.ViewModels;

/// <summary>
/// 播放器界面状态：把 <see cref="PlaybackService"/> 的状态映射成可绑定属性，
/// 并负责歌词加载、高亮、对外广播与系统媒体信息推送。
/// </summary>
public partial class PlayerViewModel : ViewModelBase
{
    private const string IdleTitle = "未在播放";
    private const string IdleHint = "从音乐库选择一首歌";

    private readonly PlaybackService _playback;
    private readonly ISettingsStore _settings;
    private readonly ILibraryStore _libraryStore;
    private readonly LyricsService _lyricsService;
    private readonly LyricsBroadcastServer _broadcast;
    private readonly ISystemMediaService _systemMedia;
    private readonly MetadataScrapeService _scraper;
    private readonly IAudioCache _cache;
    private readonly DispatcherTimer _saveTimer;

    private LyricDocument _lyrics = LyricDocument.Empty;
    private IReadOnlyList<string> _lyricTexts = [];
    private string? _lyricsTrackId;
    private string _lastSystemMediaSignature = string.Empty;
    private string _lastMediaLineKey = string.Empty;
    private bool _isFavorite;

    /// <summary>从数据库回填收藏状态时不要反过来再写库。</summary>
    private bool _loadingFavorite;

    /// <summary>
    /// 期望音量。不能直接读引擎的 Volume：音频输出尚未初始化时 VLC 返回 -1，
    /// 会把界面拖到 0。
    /// </summary>
    private double _volume;

    public PlayerViewModel(
        PlaybackService playback,
        ISettingsStore settings,
        ILibraryStore libraryStore,
        LyricsService lyricsService,
        LyricsBroadcastServer broadcast,
        ISystemMediaService systemMedia,
        MetadataScrapeService scraper,
        IAudioCache cache)
    {
        _playback = playback;
        _settings = settings;
        _libraryStore = libraryStore;
        _lyricsService = lyricsService;
        _broadcast = broadcast;
        _systemMedia = systemMedia;
        _scraper = scraper;
        _cache = cache;

        SearchProviders = scraper.Providers
            .Select(provider => new MetadataProviderOption(provider.Id, provider.DisplayName))
            .ToList();
        SelectedSearchProvider = SearchProviders.FirstOrDefault();

        // 拖动音量时不要每帧都写文件。
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _ = SaveSettingsAsync();
        };

        _playback.Changed += (_, _) => Sync();
        _systemMedia.CommandReceived += OnSystemMediaCommand;

        // 启动时恢复上次的音量；引擎不可用时也无害。
        _volume = settings.Current.Volume;
        _playback.Volume = (int)Math.Round(_volume);

        Sync();
    }

    /// <summary>请求展开全屏「正在播放」页（由外壳 MainViewModel 订阅）。</summary>
    public event Action? ExpandRequested;

    /// <summary>请求收起全屏「正在播放」页。</summary>
    public event Action? CollapseRequested;

    public Track? CurrentTrack => _playback.CurrentTrack;

    public string Title => CurrentTrack?.DisplayTitle ?? IdleTitle;

    /// <summary>副标题：优先显示错误提示，其次是「艺术家 · 专辑」。</summary>
    public string Artist => _playback.LastError ?? CurrentTrack?.Subtitle ?? IdleHint;

    public string? CoverPath => CurrentTrack?.CoverPath;

    public bool HasTrack => CurrentTrack is not null;

    public bool IsPlaying => _playback.IsPlaying;

    /// <summary>正在解析取流地址（网络音源首次播放需下载或建立代理连接）。</summary>
    public bool IsBuffering => _playback.IsBuffering;

    public string BufferText => _playback.IsBuffering
        ? $"正在缓冲… {_playback.BufferProgress * 100:0}%"
        : string.Empty;

    public Geometry PlayPauseIcon => IsPlaying ? AppIcons.Pause : AppIcons.Play;

    public double PositionSeconds
    {
        get => _playback.PositionSeconds;
        set
        {
            // 播放中的进度回推会造成值抖动，用阈值过滤掉这类回写。
            if (Math.Abs(value - _playback.PositionSeconds) > 1.0)
            {
                _playback.Seek(value);
            }
        }
    }

    public double DurationSeconds => _playback.DurationSeconds;

    public string PositionText => Format(_playback.PositionSeconds);

    public string DurationText => Format(_playback.DurationSeconds);

    public double Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.5)
            {
                return;
            }

            _volume = value;
            _playback.Volume = (int)Math.Round(value);
            _settings.Current.Volume = value;
            OnPropertyChanged();

            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    public bool IsMuted
    {
        get => _playback.IsMuted;
        set
        {
            if (_playback.IsMuted == value)
            {
                return;
            }

            _playback.IsMuted = value;
            OnPropertyChanged(nameof(VolumeIcon));
        }
    }

    public Geometry VolumeIcon => IsMuted ? AppIcons.VolumeMute : AppIcons.VolumeUp;

    /// <summary>
    /// 播放模式：顺序 → 随机 → 列表循环 → 单曲循环，界面上由一个按钮循环切换。
    /// 播放服务内部仍是「洗牌开关 + 循环模式」两个字段，这里只做组合映射。
    /// </summary>
    public PlayMode PlayMode
    {
        get
        {
            if (_playback.IsShuffleEnabled)
            {
                return PlayMode.Shuffle;
            }

            return _playback.RepeatMode switch
            {
                RepeatMode.One => PlayMode.RepeatOne,
                RepeatMode.All => PlayMode.RepeatAll,
                _ => PlayMode.Sequential,
            };
        }
        set
        {
            if (PlayMode == value)
            {
                return;
            }

            _playback.IsShuffleEnabled = value == PlayMode.Shuffle;
            _playback.RepeatMode = value switch
            {
                PlayMode.RepeatAll => RepeatMode.All,
                PlayMode.RepeatOne => RepeatMode.One,
                _ => RepeatMode.Off,
            };

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlayModeActive));
            OnPropertyChanged(nameof(PlayModeIcon));
            OnPropertyChanged(nameof(PlayModeText));
        }
    }

    /// <summary>非「顺序播放」时按钮高亮，表示随机或循环已开启。</summary>
    public bool IsPlayModeActive => PlayMode != PlayMode.Sequential;

    /// <summary>按钮图标随模式变化；顺序播放用未高亮的循环图标。</summary>
    public Geometry PlayModeIcon => PlayMode switch
    {
        PlayMode.Shuffle => AppIcons.Shuffle,
        PlayMode.RepeatOne => AppIcons.RepeatOne,
        _ => AppIcons.Repeat,
    };

    /// <summary>当前模式名，用于按钮提示。</summary>
    public string PlayModeText => PlayMode switch
    {
        PlayMode.Shuffle => "随机播放",
        PlayMode.RepeatAll => "列表循环",
        PlayMode.RepeatOne => "单曲循环",
        _ => "顺序播放",
    };

    /// <summary>
    /// 当前曲目的收藏状态。写回数据库，界面上的切换只是乐观更新。
    /// </summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteIcon));
            _ = PersistFavoriteAsync(value);
        }
    }

    public Geometry FavoriteIcon => IsFavorite ? AppIcons.Heart : AppIcons.HeartOutline;

    // ---------------- 播放队列 ----------------

    /// <summary>「正在播放列表」的行数据。</summary>
    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = [];

    /// <summary>队列抽屉是否展开。</summary>
    [ObservableProperty]
    private bool _isQueueOpen;

    [ObservableProperty]
    private int _currentQueueIndex = -1;

    /// <summary>队列内容签名，用于避免随播放进度每帧重建队列列表。</summary>
    private string _queueSignature = string.Empty;

    // ---------------- 歌词 ----------------

    public ObservableCollection<LyricLineViewModel> LyricLines { get; } = [];

    public bool HasLyrics => LyricLines.Count > 0;

    [ObservableProperty]
    private int _currentLyricIndex = -1;

    // ---------------- 曲目操作弹层（三个点） ----------------

    /// <summary>底部操作弹层是否展开。</summary>
    [ObservableProperty]
    private bool _isTrackActionsOpen;

    /// <summary>弹层内切换到「添加到歌单」的歌单列表。</summary>
    [ObservableProperty]
    private bool _isPlaylistPickerOpen;

    /// <summary>弹层内的操作状态提示（下载进度等）。</summary>
    [ObservableProperty]
    private string _trackActionStatusText = string.Empty;

    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>本地曲目无需下载，仅网络音源（FTP / Navidrome）可用。在线曲目由「发现」页下载。</summary>
    public bool CanDownload => CurrentTrack is { SourceType: not (MusicSourceType.Local or MusicSourceType.Online) };

    /// <summary>歌单（分类）列表及其归属状态。</summary>
    public ObservableCollection<PlaylistOptionViewModel> Playlists { get; } = [];

    /// <summary>新建歌单输入框内容。</summary>
    [ObservableProperty]
    private string _newPlaylistName = string.Empty;

    // ---------------- 歌词搜索 ----------------

    /// <summary>歌词搜索面板是否展开。</summary>
    [ObservableProperty]
    private bool _isMetadataSearchOpen;

    /// <summary>搜索结果面板是否展开。</summary>
    public bool IsSearchResultsVisible => SearchResults.Count > 0;

    [ObservableProperty]
    private string _metadataSearchStatusText = string.Empty;

    [ObservableProperty]
    private bool _isSearchingMetadata;

    /// <summary>可编辑的歌曲元数据，用于修正错误标题后重新搜索。</summary>
    [ObservableProperty]
    private string _searchTitle = string.Empty;

    [ObservableProperty]
    private string _searchArtist = string.Empty;

    [ObservableProperty]
    private string _searchAlbum = string.Empty;

    [ObservableProperty]
    private string _searchYear = string.Empty;

    public IReadOnlyList<MetadataProviderOption> SearchProviders { get; }

    [ObservableProperty]
    private MetadataProviderOption? _selectedSearchProvider;

    public ObservableCollection<MetadataSearchResultViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    private MetadataSearchResultViewModel? _selectedSearchResult;

    // ---------------- 命令 ----------------

    [RelayCommand]
    private void TogglePlay() => _playback.TogglePlay();

    [RelayCommand]
    private Task Next() => _playback.NextAsync();

    [RelayCommand]
    private Task Previous() => _playback.PreviousAsync();

    [RelayCommand]
    private void CyclePlayMode()
    {
        PlayMode = PlayMode switch
        {
            PlayMode.Sequential => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.RepeatAll,
            PlayMode.RepeatAll => PlayMode.RepeatOne,
            _ => PlayMode.Sequential,
        };
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void ToggleFavorite() => IsFavorite = !IsFavorite;

    [RelayCommand]
    private void Expand() => ExpandRequested?.Invoke();

    [RelayCommand]
    private void Collapse() => CollapseRequested?.Invoke();

    [RelayCommand]
    private void ToggleQueue() => IsQueueOpen = !IsQueueOpen;

    [RelayCommand]
    private Task PlayQueueItem(QueueItemViewModel item) => _playback.PlayAtAsync(item.Index);

    // ---------------- 曲目操作弹层 ----------------

    [RelayCommand]
    private async Task OpenTrackActions()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        IsPlaylistPickerOpen = false;
        TrackActionStatusText = string.Empty;
        IsTrackActionsOpen = true;
        await LoadPlaylistsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CloseTrackActions()
    {
        IsTrackActionsOpen = false;
        IsPlaylistPickerOpen = false;
        TrackActionStatusText = string.Empty;
    }

    [RelayCommand]
    private void OpenPlaylistPicker()
    {
        TrackActionStatusText = string.Empty;
        IsPlaylistPickerOpen = true;
    }

    [RelayCommand]
    private void ClosePlaylistPicker() => IsPlaylistPickerOpen = false;

    /// <summary>加入 / 移出歌单（分类）。</summary>
    [RelayCommand]
    private async Task TogglePlaylist(PlaylistOptionViewModel option)
    {
        if (CurrentTrack?.Id is not { } trackId)
        {
            return;
        }

        var target = !option.IsMember;
        try
        {
            if (target)
            {
                await _libraryStore.AddTrackToCategoryAsync(option.Id, trackId).ConfigureAwait(true);
            }
            else
            {
                await _libraryStore.RemoveTrackFromCategoryAsync(option.Id, trackId).ConfigureAwait(true);
            }

            option.IsMember = target;
            TrackActionStatusText = target
                ? $"已添加到「{option.Name}」。"
                : $"已从「{option.Name}」移出。";
        }
        catch (Exception ex)
        {
            TrackActionStatusText = $"操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreatePlaylist()
    {
        var name = NewPlaylistName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        try
        {
            var created = await _libraryStore.CreateCategoryAsync(name).ConfigureAwait(true);
            NewPlaylistName = string.Empty;

            if (CurrentTrack?.Id is { } trackId)
            {
                await _libraryStore.AddTrackToCategoryAsync(created.Id, trackId).ConfigureAwait(true);
                Playlists.Add(new PlaylistOptionViewModel(created, isMember: true));
                TrackActionStatusText = $"已创建「{name}」并添加。";
            }
        }
        catch (Exception ex)
        {
            TrackActionStatusText = $"新建歌单失败：{ex.Message}";
        }
    }

    /// <summary>把网络曲目下载到「音乐」目录（本地曲目无需下载）。</summary>
    [RelayCommand]
    private async Task DownloadTrack()
    {
        if (CurrentTrack is not { } track || IsDownloading || !CanDownload)
        {
            return;
        }

        IsDownloading = true;
        TrackActionStatusText = "正在下载…";

        try
        {
            // 已在缓存里的曲目会立刻回调 1，只在真正有进度时刷新百分比，避免"点一下就到 100%"。
            var progress = new Progress<double>(value =>
            {
                if (value < 1)
                {
                    TrackActionStatusText = $"正在下载… {value * 100:0}%";
                }
            });

            var cachedPath = await _cache.DownloadAsync(track, progress).ConfigureAwait(true);
            var savedPath = SaveToDownloadFolder(track, cachedPath);
            TrackActionStatusText = $"已下载到：{savedPath}";
        }
        catch (Exception ex)
        {
            TrackActionStatusText = $"下载失败：{ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>把缓存文件按「歌手 - 歌名」另存到用户可见的下载目录，重名时自动加序号。</summary>
    private static string SaveToDownloadFolder(Track track, string cachedPath)
    {
        Directory.CreateDirectory(AppPaths.DownloadDir);

        var extension = Path.GetExtension(cachedPath);
        var baseName = SanitizeFileName($"{track.Artist} - {track.DisplayTitle}");
        if (string.IsNullOrWhiteSpace(baseName) || baseName == "-")
        {
            baseName = SanitizeFileName(track.Id);
        }

        var destination = Path.Combine(AppPaths.DownloadDir, baseName + extension);
        for (var index = 2; File.Exists(destination); index++)
        {
            destination = Path.Combine(AppPaths.DownloadDir, $"{baseName} ({index}){extension}");
        }

        File.Copy(cachedPath, destination, overwrite: false);
        return destination;
    }

    /// <summary>把文件名中的非法字符替换成下划线。</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
    }

    // ---------------- 歌词搜索 ----------------

    /// <summary>打开歌词搜索：用当前曲目的元数据预填表单，可手动修正后重新搜索。</summary>
    [RelayCommand]
    private void OpenMetadataSearch()
    {
        if (CurrentTrack is not { } track)
        {
            return;
        }

        SearchTitle = track.DisplayTitle;
        SearchArtist = track.Artist;
        SearchAlbum = track.Album;
        SearchYear = track.Year > 0 ? track.Year.ToString() : string.Empty;
        SearchResults.Clear();
        SelectedSearchResult = null;
        MetadataSearchStatusText = string.Empty;
        OnPropertyChanged(nameof(IsSearchResultsVisible));

        IsTrackActionsOpen = false;
        IsPlaylistPickerOpen = false;
        IsMetadataSearchOpen = true;
    }

    [RelayCommand]
    private void CloseMetadataSearch() => IsMetadataSearchOpen = false;

    /// <summary>按当前（可编辑的）元数据在指定数据源搜索候选。</summary>
    [RelayCommand]
    private async Task SearchMetadata()
    {
        if (IsSearchingMetadata || SelectedSearchProvider is not { } provider)
        {
            return;
        }

        IsSearchingMetadata = true;
        MetadataSearchStatusText = "正在搜索…";
        SearchResults.Clear();
        SelectedSearchResult = null;
        OnPropertyChanged(nameof(IsSearchResultsVisible));

        try
        {
            var query = new TrackQuery(SearchTitle.Trim(), SearchArtist.Trim(), SearchAlbum.Trim());
            var candidates = await _scraper.SearchAsync(provider.Id, query).ConfigureAwait(true);

            foreach (var candidate in candidates)
            {
                SearchResults.Add(new MetadataSearchResultViewModel(candidate));
            }

            OnPropertyChanged(nameof(IsSearchResultsVisible));

            if (SearchResults.Count == 0)
            {
                MetadataSearchStatusText = $"{provider.Name}没有搜到结果，试试修改标题或换个数据源。";
                return;
            }

            MetadataSearchStatusText = $"找到 {SearchResults.Count} 个结果，点选一条后应用。";
            SelectSearchResult(SearchResults[0]);

            _ = LoadThumbnailsAsync(provider.Id);
        }
        catch (Exception ex)
        {
            MetadataSearchStatusText = $"搜索失败：{ex.Message}";
        }
        finally
        {
            IsSearchingMetadata = false;
        }
    }

    [RelayCommand]
    private void SelectSearchResult(MetadataSearchResultViewModel result)
    {
        foreach (var item in SearchResults)
        {
            item.IsSelected = ReferenceEquals(item, result);
        }

        SelectedSearchResult = result;

        // 选中候选后把它的元数据填进编辑区，点「应用所选」时才会带上这些值写回音源。
        // 年份数据源不一定提供（Year 为 0），此时保留用户自己填的值不变。
        if (!string.IsNullOrWhiteSpace(result.Candidate.Title))
        {
            SearchTitle = result.Candidate.Title;
        }

        if (!string.IsNullOrWhiteSpace(result.Candidate.Artist))
        {
            SearchArtist = result.Candidate.Artist;
        }

        if (!string.IsNullOrWhiteSpace(result.Candidate.Album))
        {
            SearchAlbum = result.Candidate.Album;
        }

        if (result.Candidate.Year > 0)
        {
            SearchYear = result.Candidate.Year.ToString();
        }
    }

    /// <summary>应用所选结果：下载封面/歌词到本地，并保存编辑过的元数据。</summary>
    [RelayCommand]
    private async Task ApplySearchResult()
    {
        if (CurrentTrack is not { } track
            || SelectedSearchProvider is not { } provider
            || SelectedSearchResult is not { } result)
        {
            MetadataSearchStatusText = "请先选择一条搜索结果。";
            return;
        }

        MetadataSearchStatusText = "正在应用…";

        try
        {
            // 写回远端要整文件往返，把阶段进度直接显示在状态栏，避免看起来像卡死。
            var progress = new Progress<string>(text => MetadataSearchStatusText = text);

            // 元数据随候选一起交给 ApplyAsync：封面、歌词、元信息在一次写回里全部落到音源上。
            var outcome = await _scraper
                .ApplyAsync(
                    track,
                    provider.Id,
                    result.Candidate,
                    SearchTitle,
                    SearchArtist,
                    SearchAlbum,
                    SearchYear,
                    progress)
                .ConfigureAwait(true);

            if (_playback.CurrentTrack?.Id == track.Id)
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Artist));
            }

            if (outcome.CoverUpdated)
            {
                OnPropertyChanged(nameof(CoverPath));
            }

            if (outcome.LyricsUpdated)
            {
                await LoadLyricsAsync(track).ConfigureAwait(true);
            }

            MetadataSearchStatusText = outcome.Message;
        }
        catch (Exception ex)
        {
            MetadataSearchStatusText = $"应用失败：{ex.Message}";
        }
    }

    /// <summary>保存当前编辑的元数据：落库并写回音源文件（Navidrome / 在线曲目无实体文件，自动跳过）。</summary>
    [RelayCommand]
    private async Task SaveSearchMetadata()
    {
        if (CurrentTrack is not { } track)
        {
            return;
        }

        try
        {
            var writeBack = await SaveMetadataCoreAsync(track).ConfigureAwait(true);

            MetadataSearchStatusText = writeBack.Status switch
            {
                WriteBackStatus.Succeeded => "已保存并写回音源。",
                WriteBackStatus.Failed => $"已保存到曲库，写回音源失败：{writeBack.Message}",
                _ => writeBack.Message.Length > 0
                    ? $"已保存到曲库，未写回音源：{writeBack.Message}"
                    : "已保存到曲库。",
            };
        }
        catch (Exception ex)
        {
            MetadataSearchStatusText = $"保存失败：{ex.Message}";
        }
    }

    private async Task<WriteBackResult> SaveMetadataCoreAsync(Track track)
    {
        var progress = new Progress<string>(text => MetadataSearchStatusText = text);

        var writeBack = await _scraper
            .SaveMetadataAsync(track, SearchTitle, SearchArtist, SearchAlbum, SearchYear, progress)
            .ConfigureAwait(true);

        if (_playback.CurrentTrack?.Id == track.Id)
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Artist));
        }

        return writeBack;
    }

    private async Task LoadPlaylistsAsync()
    {
        try
        {
            var categories = await _libraryStore.GetCategoriesAsync().ConfigureAwait(true);
            var membership = await _libraryStore.GetTrackCategoryMapAsync().ConfigureAwait(true);

            var currentId = CurrentTrack?.Id;
            var memberIds = currentId is not null && membership.TryGetValue(currentId, out var ids)
                ? ids
                : [];

            Playlists.Clear();
            foreach (var category in categories)
            {
                Playlists.Add(new PlaylistOptionViewModel(category, memberIds.Contains(category.Id)));
            }
        }
        catch (Exception)
        {
            // 歌单读取失败时保持空列表，不影响其它操作。
        }
    }

    private async Task LoadThumbnailsAsync(string providerId)
    {
        var items = SearchResults.ToList();
        foreach (var item in items)
        {
            var bytes = await _scraper.GetCoverBytesAsync(providerId, item.Candidate).ConfigureAwait(true);
            if (bytes is null || bytes.Length == 0)
            {
                continue;
            }

            try
            {
                using var stream = new MemoryStream(bytes);
                item.Cover = new Bitmap(stream);
            }
            catch (Exception)
            {
                // 缩略图解码失败不影响选择。
            }
        }
    }

    // ---------------- 内部同步 ----------------

    private void Sync()
    {
        var track = _playback.CurrentTrack;

        if (track?.Id != _lyricsTrackId)
        {
            _lyricsTrackId = track?.Id;
            _lyrics = LyricDocument.Empty;
            _lyricTexts = [];
            LyricLines.Clear();
            CurrentLyricIndex = -1;
            // 弹层内容都是针对上一首的，切歌后收起，避免误操作。
            IsTrackActionsOpen = false;
            IsPlaylistPickerOpen = false;
            IsMetadataSearchOpen = false;
            OnPropertyChanged(nameof(HasLyrics));
            _ = LoadLyricsAsync(track);
            _ = LoadFavoriteAsync(track);
        }

        RefreshQueue();
        RaiseAll();
        UpdateLyricHighlight();
        Publish();
    }

    /// <summary>
    /// 刷新「正在播放列表」。Changed 事件随播放进度每帧触发，
    /// 因此用「曲目 Id 序列」签名过滤，仅在队列真正变化时重建行集合。
    /// </summary>
    private void RefreshQueue()
    {
        var queue = _playback.Queue;
        var signature = string.Join('|', queue.Select(track => track.Id));

        if (signature != _queueSignature)
        {
            _queueSignature = signature;
            QueueItems.Clear();

            for (var i = 0; i < queue.Count; i++)
            {
                QueueItems.Add(new QueueItemViewModel(queue[i], i));
            }
        }

        CurrentQueueIndex = _playback.CurrentIndex;

        for (var i = 0; i < QueueItems.Count; i++)
        {
            QueueItems[i].IsCurrent = i == CurrentQueueIndex;
        }
    }

    private async Task LoadLyricsAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var lyrics = await _lyricsService.GetLyricsAsync(track).ConfigureAwait(true);

        // 加载期间可能已经切歌了。
        if (_lyricsTrackId != track.Id)
        {
            return;
        }

        _lyrics = lyrics;
        _lyricTexts = lyrics.Lines.Select(line => line.Text).ToList();
        LyricLines.Clear();

        foreach (var line in lyrics.Lines)
        {
            LyricLines.Add(new LyricLineViewModel(line.Text));
        }

        OnPropertyChanged(nameof(HasLyrics));
        UpdateLyricHighlight();
    }

    private void UpdateLyricHighlight()
    {
        var index = _lyrics.IndexAt(_playback.PositionSeconds);
        if (index == CurrentLyricIndex)
        {
            return;
        }

        CurrentLyricIndex = index;
    }

    private async Task LoadFavoriteAsync(Track? track)
    {
        if (track is null)
        {
            _loadingFavorite = true;
            try
            {
                IsFavorite = false;
            }
            finally
            {
                _loadingFavorite = false;
            }

            return;
        }

        var favoriteIds = await _libraryStore.GetFavoriteTrackIdsAsync().ConfigureAwait(true);

        // 加载期间可能已经切歌了。
        if (_playback.CurrentTrack?.Id != track.Id)
        {
            return;
        }

        _loadingFavorite = true;
        try
        {
            IsFavorite = favoriteIds.Contains(track.Id);
        }
        finally
        {
            _loadingFavorite = false;
        }
    }

    private async Task PersistFavoriteAsync(bool isFavorite)
    {
        if (_loadingFavorite || _playback.CurrentTrack?.Id is not { } trackId)
        {
            return;
        }

        try
        {
            await _libraryStore.SetFavoriteAsync(trackId, isFavorite);
        }
        catch (Exception)
        {
            // 收藏写库失败不影响播放。
        }
    }

    /// <summary>把状态推给广播服务与系统媒体中心。</summary>
    private void Publish()
    {
        var track = _playback.CurrentTrack;

        _broadcast.Update(new BroadcastState(
            track?.DisplayTitle ?? IdleTitle,
            track?.DisplayArtist ?? string.Empty,
            track?.Album ?? string.Empty,
            _playback.PositionSeconds,
            _playback.DurationSeconds,
            _playback.IsPlaying,
            _lyricTexts,
            CurrentLyricIndex));

        // 开启歌词广播后，把当前歌词行写进系统媒体的「专辑」字段，蓝牙耳机/车机即可显示。
        // 关闭广播或没有歌词时传 null，平台层恢复真实专辑名。随每个进度 tick 检查，
        // 只有行变化（或开关变化）才真正推送，避免频繁刷元数据。
        string? line = null;
        if (_settings.Current.BroadcastEnabled
            && CurrentLyricIndex >= 0
            && CurrentLyricIndex < _lyricTexts.Count
            && !string.IsNullOrWhiteSpace(_lyricTexts[CurrentLyricIndex]))
        {
            line = _lyricTexts[CurrentLyricIndex];
        }

        var lineKey = $"{track?.Id}|{line}";
        if (lineKey != _lastMediaLineKey)
        {
            _lastMediaLineKey = lineKey;
            _systemMedia.UpdateLine(line);
        }

        // SMTC 只需在曲目或播放状态变化时刷新，不必跟着进度走。
        var signature = $"{track?.Id}|{_playback.IsPlaying}";
        if (signature == _lastSystemMediaSignature)
        {
            return;
        }

        _lastSystemMediaSignature = signature;

        if (track is null)
        {
            _systemMedia.Clear();
            return;
        }

        _systemMedia.Update(new NowPlayingInfo(
            track.DisplayTitle,
            track.DisplayArtist,
            track.Album,
            track.CoverPath,
            _playback.IsPlaying,
            _playback.DurationSeconds,
            _playback.PositionSeconds));
    }

    private void OnSystemMediaCommand(object? sender, MediaControlCommand command)
    {
        // 回调来自系统线程，统一切回 UI 线程。
        Dispatcher.UIThread.Post(() =>
        {
            switch (command)
            {
                case MediaControlCommand.Play when !_playback.IsPlaying:
                case MediaControlCommand.Pause when _playback.IsPlaying:
                case MediaControlCommand.TogglePlay:
                    _playback.TogglePlay();
                    break;

                case MediaControlCommand.Next:
                    _ = _playback.NextAsync();
                    break;

                case MediaControlCommand.Previous:
                    _ = _playback.PreviousAsync();
                    break;
            }
        });
    }

    /// <summary>把播放引擎的最新状态同步到界面绑定。</summary>
    private void RaiseAll()
    {
        OnPropertyChanged(nameof(CurrentTrack));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsBuffering));
        OnPropertyChanged(nameof(BufferText));
        OnPropertyChanged(nameof(PlayPauseIcon));
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumeIcon));
        OnPropertyChanged(nameof(PlayMode));
        OnPropertyChanged(nameof(IsPlayModeActive));
        OnPropertyChanged(nameof(PlayModeIcon));
        OnPropertyChanged(nameof(PlayModeText));
        OnPropertyChanged(nameof(CanDownload));
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception)
        {
            // 写入失败不影响播放。
        }
    }

    private static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }
}

/// <summary>播放模式（界面上由一个按钮循环切换）。后端由洗牌开关与循环模式两个字段组合表达。</summary>
public enum PlayMode
{
    /// <summary>顺序播放：不随机、不循环。</summary>
    Sequential = 0,

    /// <summary>随机播放。</summary>
    Shuffle = 1,

    /// <summary>列表循环。</summary>
    RepeatAll = 2,

    /// <summary>单曲循环。</summary>
    RepeatOne = 3,
}
