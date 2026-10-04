using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Music.Models;
using Music.Services.Media;

namespace Music.Services.Audio;

/// <summary>
/// 播放队列与传输控制。
/// 所有对外事件都会切回 UI 线程抛出，因此订阅方（视图模型）可以直接更新界面。
/// </summary>
public sealed class PlaybackService : IDisposable
{
    /// <summary>「上一首」在播放超过该时长后改为回到开头。</summary>
    private const long RestartThresholdMs = 3000;

    private readonly IAudioPlayer _player;
    private readonly IMediaResolver _resolver;
    private readonly List<Track> _queue = [];
    private readonly List<Track> _recent = [];
    private readonly Random _random = new();
    private int _currentIndex = -1;
    private bool _disposed;

    /// <summary>首页「最近播放」最多保留的曲目数。</summary>
    private const int MaxRecent = 12;

    public PlaybackService(IAudioPlayer player, IMediaResolver resolver)
    {
        _player = player;
        _resolver = resolver;

        _player.PlaybackEnded += OnPlaybackEnded;
        _player.PlaybackFailed += OnPlaybackFailed;
        _player.PositionChanged += OnPositionChanged;
        _player.DurationChanged += OnDurationChanged;
    }

    /// <summary>曲目、播放状态、进度或时长发生变化。</summary>
    public event EventHandler? Changed;

    public Track? CurrentTrack =>
        _currentIndex >= 0 && _currentIndex < _queue.Count ? _queue[_currentIndex] : null;

    public IReadOnlyList<Track> Queue => _queue;

    /// <summary>本次运行期间播放过的曲目，最新的在最前（首页「最近播放」使用）。</summary>
    public IReadOnlyList<Track> RecentTracks => _recent;

    public int CurrentIndex => _currentIndex;

    public bool IsPlaying => _player.IsPlaying;

    public double PositionSeconds => _player.PositionMs / 1000.0;

    public double DurationSeconds => _player.DurationMs / 1000.0;

    public bool IsShuffleEnabled { get; set; }

    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

    /// <summary>最近一次播放错误，供界面提示。</summary>
    public string? LastError { get; private set; }

    public int Volume
    {
        get => _player.Volume;
        set
        {
            _player.Volume = value;
            RaiseChanged();
        }
    }

    public bool IsMuted
    {
        get => _player.IsMuted;
        set
        {
            _player.IsMuted = value;
            RaiseChanged();
        }
    }

    /// <summary>用给定列表作为队列，并从 <paramref name="startIndex"/> 开始播放。</summary>
    public async Task PlayQueueAsync(IEnumerable<Track> tracks, int startIndex)
    {
        _queue.Clear();
        _queue.AddRange(tracks);

        if (_queue.Count == 0)
        {
            _currentIndex = -1;
            RaiseChanged();
            return;
        }

        _currentIndex = Math.Clamp(startIndex, 0, _queue.Count - 1);
        await PlayCurrentAsync().ConfigureAwait(true);
    }

    /// <summary>跳转到队列中指定位置并开始播放；越界忽略。</summary>
    public async Task PlayAtAsync(int index)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        _currentIndex = index;
        await PlayCurrentAsync().ConfigureAwait(true);
    }

    public void TogglePlay()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }

        RaiseChanged();
    }

    public async Task NextAsync()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        _currentIndex = IsShuffleEnabled
            ? PickShuffleIndex()
            : (_currentIndex + 1) % _queue.Count;

        await PlayCurrentAsync().ConfigureAwait(true);
    }

    public async Task PreviousAsync()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        // 播放超过 3 秒时，「上一首」先回到本曲开头。
        if (_player.PositionMs > RestartThresholdMs)
        {
            _player.Seek(0);
            RaiseChanged();
            return;
        }

        _currentIndex = _currentIndex <= 0 ? _queue.Count - 1 : _currentIndex - 1;
        await PlayCurrentAsync().ConfigureAwait(true);
    }

    public void Seek(double seconds)
    {
        _player.Seek((long)(seconds * 1000));
        RaiseChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.PlaybackFailed -= OnPlaybackFailed;
        _player.PositionChanged -= OnPositionChanged;
        _player.DurationChanged -= OnDurationChanged;
        _player.Dispose();
    }

    private async Task PlayCurrentAsync()
    {
        var track = CurrentTrack;
        if (track is null)
        {
            RaiseChanged();
            return;
        }

        try
        {
            var source = await _resolver.ResolveAsync(track).ConfigureAwait(true);
            _player.Load(source);
            _player.Play();
            LastError = null;
            RecordRecent(track);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        RaiseChanged();
    }

    /// <summary>把成功开播的曲目记到最近播放，去重后置顶并限制条数。</summary>
    private void RecordRecent(Track track)
    {
        var existing = _recent.FindIndex(item => item.Id == track.Id);
        if (existing >= 0)
        {
            _recent.RemoveAt(existing);
        }

        _recent.Insert(0, track);

        if (_recent.Count > MaxRecent)
        {
            _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        }
    }

    private int PickShuffleIndex()
    {
        if (_queue.Count <= 1)
        {
            return 0;
        }

        int next;
        do
        {
            next = _random.Next(_queue.Count);
        }
        while (next == _currentIndex);

        return next;
    }

    /// <summary>曲目播完：按循环模式决定重播、下一首或停止。</summary>
    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        // libvlc 回调线程内绝不能直接操作播放器，统一切回 UI 线程再处理。
        Dispatcher.UIThread.Post(() => _ = HandlePlaybackEndedAsync());
    }

    private async Task HandlePlaybackEndedAsync()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        if (RepeatMode == RepeatMode.One)
        {
            _player.Seek(0);
            _player.Play();
            RaiseChanged();
            return;
        }

        var isLast = _currentIndex >= _queue.Count - 1;
        if (isLast && !IsShuffleEnabled && RepeatMode == RepeatMode.Off)
        {
            RaiseChanged();
            return;
        }

        await NextAsync().ConfigureAwait(true);
    }

    private void OnPlaybackFailed(object? sender, string message)
    {
        LastError = message;
        RaiseChanged();
    }

    private void OnPositionChanged(object? sender, long positionMs) => RaiseChanged();

    private void OnDurationChanged(object? sender, long lengthMs) => RaiseChanged();

    private void RaiseChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }
}
