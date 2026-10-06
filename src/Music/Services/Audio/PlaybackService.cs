using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Music.Models;
using Music.Services.Cache;
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

    /// <summary>本地代理地址的前缀，用于判断当前播放是否走代理，以便失败时回退。</summary>
    private const string ProxyUrlPrefix = "http://127.0.0.1:";

    private readonly IAudioPlayer _player;
    private readonly IMediaResolver _resolver;
    private readonly IAudioCache _cache;
    private readonly LocalMediaProxy _proxy;
    private readonly List<Track> _queue = [];
    private readonly List<Track> _recent = [];
    private readonly Random _random = new();
    private int _currentIndex = -1;
    private bool _disposed;

    /// <summary>当前解析/缓冲请求；切歌时取消上一个，避免旧结果覆盖新曲目。</summary>
    private CancellationTokenSource? _resolveCts;

    /// <summary>当前交给播放引擎的地址，用于代理失败时判断是否需要回退。</summary>
    private string? _currentSource;

    /// <summary>首页「最近播放」最多保留的曲目数。</summary>
    private const int MaxRecent = 12;

    public PlaybackService(
        IAudioPlayer player,
        IMediaResolver resolver,
        IAudioCache cache,
        LocalMediaProxy proxy)
    {
        _player = player;
        _resolver = resolver;
        _cache = cache;
        _proxy = proxy;

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

    public RepeatMode RepeatMode { get; set; } = RepeatMode.All;

    /// <summary>最近一次播放错误，供界面提示。</summary>
    public string? LastError { get; private set; }

    /// <summary>解析取流地址期间为 true（FTP/SMB/WebDAV 需要先下载或建立代理连接）。</summary>
    public bool IsBuffering { get; private set; }

    /// <summary>缓冲进度 0..1；走本地代理时立即为 1。</summary>
    public double BufferProgress { get; private set; }

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

    /// <summary>
    /// 从当前播放队列（临时播放表）中移除指定位置的曲目。
    /// 移除的不是当前曲目时只更新队列；移除当前曲目时自动接播后一首（已是最后一首则接播新的最后一首）。
    /// </summary>
    public async Task RemoveAtAsync(int index)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        var removedCurrent = index == _currentIndex;
        _queue.RemoveAt(index);

        if (_queue.Count == 0)
        {
            _currentIndex = -1;
            RaiseChanged();
            return;
        }

        if (removedCurrent)
        {
            _currentIndex = Math.Min(index, _queue.Count - 1);
            await PlayCurrentAsync().ConfigureAwait(true);
            return;
        }

        if (index < _currentIndex)
        {
            _currentIndex--;
        }

        RaiseChanged();
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
        _resolveCts?.Cancel();
        _resolveCts = null;
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

        // 快速连点切歌时取消上一次缓冲，避免旧曲目的结果覆盖新曲目。
        _resolveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _resolveCts = cts;

        IsBuffering = true;
        BufferProgress = 0;
        RaiseChanged();

        // 只有仍是当前请求时才把进度写回界面。
        var progress = new Progress<double>(value =>
        {
            if (ReferenceEquals(_resolveCts, cts))
            {
                BufferProgress = value;
                RaiseChanged();
            }
        });

        string? source = null;
        try
        {
            source = await _resolver
                .ResolveAsync(track, progress, cts.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 已被后续的切歌请求取代，不视为错误。
            return;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        // 解析期间又切了歌：丢弃本次结果。
        if (!ReferenceEquals(_resolveCts, cts))
        {
            return;
        }

        IsBuffering = false;
        BufferProgress = source is null ? 0 : 1;

        if (source is not null)
        {
            _currentSource = source;
            _player.Load(source);
            _player.Play();
            LastError = null;
            RecordRecent(track);

            // 提前把队列下一首拉进缓存，切下一首时无需等待下载。
            PrefetchNext();
        }

        RaiseChanged();
    }

    /// <summary>预取队列中的下一首：仅文件协议需要先下载，HTTP 直连无需处理。</summary>
    private void PrefetchNext()
    {
        if (IsShuffleEnabled || _currentIndex < 0 || _currentIndex + 1 >= _queue.Count)
        {
            return;
        }

        if (_queue[_currentIndex + 1] is
            { SourceType: MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav } next)
        {
            _cache.DownloadInBackground(next);
        }
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
            // 播完最后一帧后播放器已处于「已结束」状态，Seek + Play 不一定能重启，交给播放器重装媒体。
            _player.Restart();
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

        // 走本地代理播放失败（个别平台/协议组合不支持），关掉代理并按整文件下载重试一次。
        if (_proxy.Disabled
            || _currentSource is null
            || !_currentSource.StartsWith(ProxyUrlPrefix, StringComparison.Ordinal))
        {
            return;
        }

        _proxy.Disabled = true;
        _currentSource = null;

        // 回调来自 libvlc 内部线程，切回 UI 线程重试。
        Dispatcher.UIThread.Post(() => _ = PlayCurrentAsync());
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
