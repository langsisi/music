using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Controls;
using Music.Models;
using Music.Services;
using Music.Services.Audio;
using Music.Services.Broadcast;
using Music.Services.Library;
using Music.Services.Lyrics;
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
    private readonly DispatcherTimer _saveTimer;

    private LyricDocument _lyrics = LyricDocument.Empty;
    private IReadOnlyList<string> _lyricTexts = [];
    private string? _lyricsTrackId;
    private string _lastSystemMediaSignature = string.Empty;
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
        ISystemMediaService systemMedia)
    {
        _playback = playback;
        _settings = settings;
        _libraryStore = libraryStore;
        _lyricsService = lyricsService;
        _broadcast = broadcast;
        _systemMedia = systemMedia;

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

    public bool IsShuffleEnabled
    {
        get => _playback.IsShuffleEnabled;
        set
        {
            if (_playback.IsShuffleEnabled == value)
            {
                return;
            }

            _playback.IsShuffleEnabled = value;
            OnPropertyChanged();
        }
    }

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

    // ---------------- 歌词 ----------------

    public ObservableCollection<LyricLineViewModel> LyricLines { get; } = [];

    public bool HasLyrics => LyricLines.Count > 0;

    [ObservableProperty]
    public partial int CurrentLyricIndex { get; set; } = -1;

    // ---------------- 命令 ----------------

    [RelayCommand]
    private void TogglePlay() => _playback.TogglePlay();

    [RelayCommand]
    private Task Next() => _playback.NextAsync();

    [RelayCommand]
    private Task Previous() => _playback.PreviousAsync();

    [RelayCommand]
    private void ToggleShuffle() => IsShuffleEnabled = !IsShuffleEnabled;

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void ToggleFavorite() => IsFavorite = !IsFavorite;

    [RelayCommand]
    private void Expand() => ExpandRequested?.Invoke();

    [RelayCommand]
    private void Collapse() => CollapseRequested?.Invoke();

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
            OnPropertyChanged(nameof(HasLyrics));
            _ = LoadLyricsAsync(track);
            _ = LoadFavoriteAsync(track);
        }

        RaiseAll();
        UpdateLyricHighlight();
        Publish();
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
            _playback.DurationSeconds));
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
        OnPropertyChanged(nameof(PlayPauseIcon));
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumeIcon));
        OnPropertyChanged(nameof(IsShuffleEnabled));
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
