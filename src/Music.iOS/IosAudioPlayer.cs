using System;
using System.IO;
using AVFoundation;
using CoreMedia;
using Foundation;
using Music.Services.Audio;

namespace Music.iOS;

/// <summary>
/// 基于 AVFoundation <see cref="AVPlayer"/> 的 iOS 播放实现。
/// 严格满足 <see cref="IAudioPlayer"/> 的语义：方法只在 UI 线程被调用，事件可能来自内部线程。
/// </summary>
/// <remarks>
/// iOS 上 <c>Load()</c> 只会收到本地文件路径或 http(s) URL：
/// FTP 音源已被 Core 的 <c>CachedMediaResolver</c> 提前下载为本地文件，
/// Navidrome 音源给出的是 http(s) 流地址，均为 AVPlayer 原生支持的类型。
/// </remarks>
internal sealed class IosAudioPlayer : NSObject, IAudioPlayer
{
    private const string StatusKeyPath = "status";
    private const string DurationKeyPath = "duration";

    private AVPlayer? _player;
    private AVPlayerItem? _item;

    private NSObject? _timeObserver;
    private NSObject? _endObserver;
    private NSObject? _failObserver;

    private bool _sessionConfigured;
    private long _lastDurationMs;

    public bool IsAvailable => true;

    public bool IsPlaying => _player?.Rate > 0;

    public long PositionMs
    {
        get
        {
            var seconds = _player?.CurrentTime.Seconds ?? 0;
            return double.IsFinite(seconds) ? (long)(seconds * 1000) : 0;
        }
    }

    public long DurationMs
    {
        get
        {
            var seconds = _item?.Duration.Seconds ?? 0;
            return double.IsFinite(seconds) && seconds > 0 ? (long)(seconds * 1000) : 0;
        }
    }

    public bool IsSeekable => _item is { Status: AVPlayerItemStatus.ReadyToPlay } && DurationMs > 0;

    public int Volume
    {
        get => (int)Math.Round((_player?.Volume ?? 1f) * 100);
        set
        {
            if (_player is not null)
            {
                _player.Volume = Math.Clamp(value, 0, 100) / 100f;
            }
        }
    }

    public bool IsMuted
    {
        get => _player?.Muted ?? false;
        set
        {
            if (_player is not null)
            {
                _player.Muted = value;
            }
        }
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<long>? PositionChanged;
    public event EventHandler<long>? DurationChanged;

    public void Load(string pathOrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl);

        // 换曲前必须彻底拆掉上一首的观察者，否则回调会打到已释放的 item 上导致崩溃。
        DetachItem();
        _lastDurationMs = 0;

        if (!IosAudioFormatSupport.IsSupported(pathOrUrl))
        {
            PlaybackFailed?.Invoke(this, $"iOS 不支持该格式（{Path.GetExtension(pathOrUrl)}）。");
            return;
        }

        var url = pathOrUrl.Contains("://", StringComparison.Ordinal)
            ? NSUrl.FromString(pathOrUrl)
            : new NSUrl(pathOrUrl, false);

        if (url is null)
        {
            PlaybackFailed?.Invoke(this, "无法识别的媒体地址。");
            return;
        }

        var item = new AVPlayerItem(url);
        _item = item;

        if (_player is null)
        {
            _player = AVPlayer.FromPlayerItem(item);
        }
        else
        {
            _player.ReplaceCurrentItemWithPlayerItem(item);
        }

        AttachItem(item);
    }

    public void Play()
    {
        ConfigureAudioSession();
        _player?.Play();
    }

    public void Pause() => _player?.Pause();

    public void Stop()
    {
        _player?.Pause();
        Seek(0);
    }

    public void Seek(long positionMs)
    {
        if (_player is null || !IsSeekable)
        {
            return;
        }

        _player.Seek(CMTime.FromSeconds(Math.Max(0, positionMs) / 1000.0, 1000));
    }

    public override void ObserveValue(NSString keyPath, NSObject ofObject, NSDictionary change, IntPtr context)
    {
        if (ofObject is not AVPlayerItem item)
        {
            base.ObserveValue(keyPath, ofObject, change, context);
            return;
        }

        var key = keyPath?.ToString();
        if (key == StatusKeyPath)
        {
            switch (item.Status)
            {
                case AVPlayerItemStatus.ReadyToPlay:
                    EmitDurationIfChanged();
                    break;
                case AVPlayerItemStatus.Failed:
                    ReportItemFailure(item);
                    break;
            }
        }
        else if (key == DurationKeyPath)
        {
            EmitDurationIfChanged();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachItem();

            _player?.Dispose();
            _player = null;
        }

        base.Dispose(disposing);
    }

    private void AttachItem(AVPlayerItem item)
    {
        item.AddObserver(this, new NSString(StatusKeyPath), NSKeyValueObservingOptions.Initial, IntPtr.Zero);
        item.AddObserver(this, new NSString(DurationKeyPath), NSKeyValueObservingOptions.New, IntPtr.Zero);

        // 只观察当前 item：换曲时 DetachItem 会移除。
        _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVPlayerItem.DidPlayToEndTimeNotification, OnPlaybackEnded, item);
        _failObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVPlayerItem.ItemFailedToPlayToEndTimeNotification, OnPlaybackFailed, item);

        // 队列传 null 表示回到主队列；250ms 一拍，足够进度条顺滑。
        _timeObserver = _player?.AddPeriodicTimeObserver(
            CMTime.FromSeconds(0.25, 1000), null, OnPeriodicTick);
    }

    private void DetachItem()
    {
        if (_timeObserver is not null && _player is not null)
        {
            _player.RemoveTimeObserver(_timeObserver);
        }

        _timeObserver = null;

        if (_endObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_endObserver);
            _endObserver = null;
        }

        if (_failObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_failObserver);
            _failObserver = null;
        }

        if (_item is not null)
        {
            _item.RemoveObserver(this, new NSString(StatusKeyPath));
            _item.RemoveObserver(this, new NSString(DurationKeyPath));
            _item.Dispose();
            _item = null;
        }
    }

    private void OnPlaybackEnded(NSNotification notification)
        => PlaybackEnded?.Invoke(this, EventArgs.Empty);

    private void OnPlaybackFailed(NSNotification notification)
    {
        if (_item is not null)
        {
            ReportItemFailure(_item);
        }
    }

    private void OnPeriodicTick(CMTime time)
    {
        PositionChanged?.Invoke(this, PositionMs);

        // 网络流的时长可能晚于 ReadyToPlay 才确定，这里兜底补发一次。
        EmitDurationIfChanged();
    }

    private void EmitDurationIfChanged()
    {
        var durationMs = DurationMs;
        if (durationMs <= 0 || durationMs == _lastDurationMs)
        {
            return;
        }

        _lastDurationMs = durationMs;
        DurationChanged?.Invoke(this, durationMs);
    }

    private void ReportItemFailure(AVPlayerItem item)
    {
        var reason = item.Error?.LocalizedDescription;
        PlaybackFailed?.Invoke(
            this,
            $"播放失败：{(string.IsNullOrEmpty(reason) ? "格式不受支持或文件损坏" : reason)}");
    }

    /// <summary>类别 Playback：支持后台播放且不受静音开关影响；只在首次 Play 时设置，避免启动即抢焦点。</summary>
    private void ConfigureAudioSession()
    {
        if (_sessionConfigured)
        {
            return;
        }

        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Playback);
            session.SetActive(true);
            _sessionConfigured = true;
        }
        catch (Exception)
        {
            // 会话配置失败不致命：Play() 仍会尝试播放，失败会经 PlaybackFailed 上报。
        }
    }
}