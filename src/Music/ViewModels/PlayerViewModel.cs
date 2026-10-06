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
using Music.Services.Sources;
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
    private readonly TrackDeleteService _deleteService;
    private readonly SourceWriteBackService _writeBack;
    private readonly DispatcherTimer _saveTimer;

    private LyricDocument _lyrics = LyricDocument.Empty;
    private IReadOnlyList<string> _lyricTexts = [];
    private string? _lyricsTrackId;
    private string _lastSystemMediaSignature = string.Empty;
    private string _lastMediaLineKey = string.Empty;
    private bool _isFavorite;
    private bool _isDeleting;

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
        IAudioCache cache,
        TrackDeleteService deleteService,
        SourceWriteBackService writeBack)
    {
        _playback = playback;
        _settings = settings;
        _libraryStore = libraryStore;
        _lyricsService = lyricsService;
        _broadcast = broadcast;
        _systemMedia = systemMedia;
        _scraper = scraper;
        _cache = cache;
        _deleteService = deleteService;
        _writeBack = writeBack;

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
        // 后台补齐（同步抓远端 .lrc / 首播解析内嵌标签）拿到歌词后自动刷新当前曲目的歌词显示。
        _lyricsService.LyricsArrived += OnLyricsArrived;

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
    /// 播放模式：随机 → 列表循环 → 单曲循环，界面上由一个按钮循环切换。
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
                _ => PlayMode.RepeatAll,
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
            OnPropertyChanged(nameof(IsShuffleMode));
            OnPropertyChanged(nameof(IsRepeatAllMode));
            OnPropertyChanged(nameof(IsRepeatOneMode));
            OnPropertyChanged(nameof(PlayModeText));
        }
    }

    /// <summary>默认的列表循环不高亮；切到随机或单曲循环时高亮，让点击有明确反馈。</summary>
    public bool IsPlayModeActive => PlayMode != PlayMode.RepeatAll;

    /// <summary>
    /// 三种模式各用一个独立 Path 呈现，尺寸按各自几何包围盒设定：
    /// 随机是 16×16 方形，循环/单曲是 18×20 竖长形，避免被方框拉伸而大小不一。
    /// </summary>
    public bool IsShuffleMode => PlayMode == PlayMode.Shuffle;

    public bool IsRepeatAllMode => PlayMode == PlayMode.RepeatAll;

    public bool IsRepeatOneMode => PlayMode == PlayMode.RepeatOne;

    /// <summary>当前模式名，用于按钮提示。</summary>
    public string PlayModeText => PlayMode switch
    {
        PlayMode.Shuffle => "随机播放",
        PlayMode.RepeatOne => "单曲循环",
        _ => "列表循环",
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

    /// <summary>「保存为歌单」输入区是否展开。</summary>
    [ObservableProperty]
    private bool _isQueueSaveOpen;

    /// <summary>保存临时播放表时输入的新歌单名称。</summary>
    [ObservableProperty]
    private string _newQueuePlaylistName = string.Empty;

    [ObservableProperty]
    private string _queueSaveStatusText = string.Empty;

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

    /// <summary>Navidrome 没有删除接口，其余音源均可删除。</summary>
    public bool CanDelete => CurrentTrack is { SourceType: not MusicSourceType.Navidrome };

    /// <summary>删除确认子面板是否展开。</summary>
    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    /// <summary>主操作菜单是否可见（歌单选择与删除确认任一并列子面板展开时隐藏）。</summary>
    public bool IsTrackActionMenuVisible => !IsPlaylistPickerOpen && !IsDeleteConfirmOpen;

    partial void OnIsPlaylistPickerOpenChanged(bool value) => OnPropertyChanged(nameof(IsTrackActionMenuVisible));

    partial void OnIsDeleteConfirmOpenChanged(bool value) => OnPropertyChanged(nameof(IsTrackActionMenuVisible));

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
            PlayMode.Shuffle => PlayMode.RepeatAll,
            PlayMode.RepeatAll => PlayMode.RepeatOne,
            _ => PlayMode.Shuffle,
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

    /// <summary>把曲目从临时播放表移除（不影响曲库与源文件）。</summary>
    [RelayCommand]
    private Task RemoveQueueItem(QueueItemViewModel item) => _playback.RemoveAtAsync(item.Index);

    [RelayCommand]
    private void ToggleQueueSave()
    {
        IsQueueSaveOpen = !IsQueueSaveOpen;
        QueueSaveStatusText = string.Empty;
    }

    /// <summary>把当前临时播放表整体保存成一个新分类（歌单）。</summary>
    [RelayCommand]
    private async Task SaveQueueAsPlaylist()
    {
        var name = NewQueuePlaylistName.Trim();
        var tracks = _playback.Queue.ToList();
        if (name.Length == 0 || tracks.Count == 0)
        {
            return;
        }

        try
        {
            var created = await _libraryStore.CreateCategoryAsync(name).ConfigureAwait(true);

            foreach (var track in tracks)
            {
                // 在线曲目先落库，归类关系才查得出来。
                await EnsureLibraryTrackAsync(track).ConfigureAwait(true);
                await _libraryStore.AddTrackToCategoryAsync(created.Id, track.Id).ConfigureAwait(true);
            }

            NewQueuePlaylistName = string.Empty;
            IsQueueSaveOpen = false;
            QueueSaveStatusText = string.Empty;
        }
        catch (Exception ex)
        {
            QueueSaveStatusText = $"保存失败：{ex.Message}";
        }
    }

    // ---------------- 曲目操作弹层 ----------------

    [RelayCommand]
    private async Task OpenTrackActions()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        IsPlaylistPickerOpen = false;
        IsDeleteConfirmOpen = false;
        TrackActionStatusText = string.Empty;
        IsTrackActionsOpen = true;
        await LoadPlaylistsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CloseTrackActions()
    {
        IsTrackActionsOpen = false;
        IsPlaylistPickerOpen = false;
        IsDeleteConfirmOpen = false;
        TrackActionStatusText = string.Empty;
    }

    [RelayCommand]
    private void OpenPlaylistPicker()
    {
        IsDeleteConfirmOpen = false;
        TrackActionStatusText = string.Empty;
        IsPlaylistPickerOpen = true;
    }

    [RelayCommand]
    private void ClosePlaylistPicker() => IsPlaylistPickerOpen = false;

    /// <summary>加入 / 移出歌单（分类）。</summary>
    [RelayCommand]
    private async Task TogglePlaylist(PlaylistOptionViewModel option)
    {
        if (CurrentTrack is not { } track)
        {
            return;
        }

        var target = !option.IsMember;
        try
        {
            if (target)
            {
                // 在线曲目原先是「不存在于曲库」的，加入歌单前先落一条记录，否则归类关系查不出来。
                await EnsureLibraryTrackAsync(track).ConfigureAwait(true);
                await _libraryStore.AddTrackToCategoryAsync(option.Id, track.Id).ConfigureAwait(true);
            }
            else
            {
                await _libraryStore.RemoveTrackFromCategoryAsync(option.Id, track.Id).ConfigureAwait(true);
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

    /// <summary>
    /// 在线曲目要收藏 / 加入歌单，库里必须先有一条曲目记录，收藏与归类关系才能查出来。
    /// 播放阶段已经把封面、歌词缓存到本地，这里直接把当前曲目（含封面路径）落库。
    /// </summary>
    private async Task EnsureLibraryTrackAsync(Track track)
    {
        if (track.SourceType != MusicSourceType.Online)
        {
            return;
        }

        await _libraryStore.UpsertTracksAsync([track]).ConfigureAwait(true);
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

            if (CurrentTrack is { } track)
            {
                await EnsureLibraryTrackAsync(track).ConfigureAwait(true);
                await _libraryStore.AddTrackToCategoryAsync(created.Id, track.Id).ConfigureAwait(true);
                Playlists.Add(new PlaylistOptionViewModel(created, isMember: true));
                TrackActionStatusText = $"已创建「{name}」并添加。";
            }
        }
        catch (Exception ex)
        {
            TrackActionStatusText = $"新建歌单失败：{ex.Message}";
        }
    }

    /// <summary>打开删除确认子面板（删除源文件不可恢复，必须先确认）。</summary>
    [RelayCommand]
    private void OpenDeleteConfirm()
    {
        if (CurrentTrack is null || !CanDelete)
        {
            return;
        }

        IsPlaylistPickerOpen = false;
        TrackActionStatusText = string.Empty;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private void CloseDeleteConfirm() => IsDeleteConfirmOpen = false;

    /// <summary>删除音乐：删掉源文件（在线曲目仅移出曲库）并从曲库移除。</summary>
    [RelayCommand]
    private async Task DeleteTrack()
    {
        if (CurrentTrack is not { } track || !CanDelete || _isDeleting)
        {
            return;
        }

        _isDeleting = true;
        TrackActionStatusText = "正在删除…";

        try
        {
            var result = await _deleteService.DeleteAsync(track).ConfigureAwait(true);
            TrackActionStatusText = result.Message;

            if (result.Status == DeleteStatus.Succeeded)
            {
                IsDeleteConfirmOpen = false;
                IsTrackActionsOpen = false;
            }
        }
        finally
        {
            _isDeleting = false;
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

        // 选中候选后把它的元数据填进编辑区，点「应用 / 同步」时才会带上这些值。
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

    /// <summary>应用所选结果：下载封面/歌词到本地缓存并保存编辑过的元数据，不写回音源。</summary>
    [RelayCommand]
    private Task ApplySearchResult() => ApplySearchResultCoreAsync(uploadLyricsToSource: false);

    /// <summary>同步所选结果：在「应用」的基础上把歌词 .lrc 上传到音源同目录，供其他设备取用。</summary>
    [RelayCommand]
    private Task SyncSearchResult() => ApplySearchResultCoreAsync(uploadLyricsToSource: true);

    private async Task ApplySearchResultCoreAsync(bool uploadLyricsToSource)
    {
        if (CurrentTrack is not { } track
            || SelectedSearchProvider is not { } provider
            || SelectedSearchResult is not { } result)
        {
            MetadataSearchStatusText = "请先选择一条搜索结果。";
            return;
        }

        MetadataSearchStatusText = uploadLyricsToSource ? "正在应用并同步歌词…" : "正在应用…";

        try
        {
            var progress = new Progress<string>(text => MetadataSearchStatusText = text);

            // 元数据随候选一起交给 ApplyAsync：封面、歌词、元信息落到本地缓存；
            // 同步时再把歌词文件上传到音源（轻量单文件上传）。
            var outcome = await _scraper
                .ApplyAsync(
                    track,
                    provider.Id,
                    result.Candidate,
                    SearchTitle,
                    SearchArtist,
                    SearchAlbum,
                    SearchYear,
                    progress,
                    uploadLyricsToSource: uploadLyricsToSource)
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

            // 歌词刷新由 LyricsService.LyricsArrived 事件触发（ApplyAsync 里歌词落库后 Invalidate）。

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
                WriteBackStatus.Succeeded when writeBack.NewRemotePath is not null
                    => $"已保存并写回音源，云端文件已按标题重命名。{writeBack.Message}",
                WriteBackStatus.Succeeded => writeBack.Message.Length > 0
                    ? $"已保存并写回音源。{writeBack.Message}"
                    : "已保存并写回音源。",
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

            // 切歌后播放代理会释放对上一首远端文件的句柄，借机重试清理之前删除失败的旧文件。
            if (_writeBack.HasPendingDeletes)
            {
                _ = FlushPendingDeletesAfterSwitchAsync();
            }
        }

        RefreshQueue();
        RaiseAll();
        UpdateLyricHighlight();
        Publish();
    }

    /// <summary>等播放代理释放上一首的句柄后再清理登记的远端旧文件；失败静默，下次切歌再试。</summary>
    private async Task FlushPendingDeletesAfterSwitchAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await _writeBack.FlushPendingDeletesAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 后台清理失败不影响界面。
        }
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

    /// <summary>
    /// 歌词在后台补齐（同步时抓远端 .lrc / 首播后解析内嵌标签）完成时自动刷新：
    /// 仅当补齐的正是当前曲目才重新加载；事件可能在后台线程触发，调度回 UI 线程。
    /// </summary>
    private void OnLyricsArrived(string trackId)
    {
        if (trackId != _lyricsTrackId
            || _playback.CurrentTrack is not { } track
            || track.Id != trackId)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = LoadLyricsAsync(track));
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
        if (_loadingFavorite || _playback.CurrentTrack is not { } track)
        {
            return;
        }

        try
        {
            if (isFavorite)
            {
                // 在线曲目先落库，收藏关系才能在曲库「收藏」里查出来。
                await EnsureLibraryTrackAsync(track).ConfigureAwait(true);
            }

            await _libraryStore.SetFavoriteAsync(track.Id, isFavorite).ConfigureAwait(true);
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
        OnPropertyChanged(nameof(IsShuffleMode));
        OnPropertyChanged(nameof(IsRepeatOneMode));
        OnPropertyChanged(nameof(IsRepeatAllMode));
        OnPropertyChanged(nameof(PlayModeText));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanDelete));
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
    /// <summary>随机播放。</summary>
    Shuffle = 0,

    /// <summary>列表循环（默认）。</summary>
    RepeatAll = 1,

    /// <summary>单曲循环。</summary>
    RepeatOne = 2,
}
