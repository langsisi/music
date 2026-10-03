using System;
using LibVLCSharp.Shared;
// 本项目的 Music.Services.Media 命名空间会与 LibVLC 的 Media 类型同名，这里用别名区分。
using VlcMedia = LibVLCSharp.Shared.Media;

namespace Music.Services.Audio;

/// <summary>
/// 基于 LibVLCSharp 的纯音频播放器。
/// 关键约束：
/// <list type="bullet">
/// <item>不创建 VideoView，纯音频播放；同时显式禁用视频输出，避免带封面的容器弹窗。</item>
/// <item>原生库加载失败不抛异常，而是通过 <see cref="PlaybackFailed"/> 上报，保证应用仍可运行。</item>
/// <item>libvlc 的回调在内部线程触发，调用方（PlaybackService）负责切回 UI 线程后再操作播放器。</item>
/// </list>
/// </summary>
public sealed class VlcAudioPlayer : IAudioPlayer
{
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private VlcMedia? _currentMedia;
    private readonly string? _initError;

    public VlcAudioPlayer()
    {
        try
        {
            // 无参重载会按「运行目录 / libvlc / win-<arch>」解析原生库；
            // 若把目录本身传进来，它反而会去该目录下直接找 libvlc.dll。
            Core.Initialize();

            _libVlc = new LibVLC(
                "--no-video",
                "--no-video-title-show",
                "--no-snapshot-preview",
                "--no-stats",
                "--network-caching=3000",
                "--file-caching=1000");

            _mediaPlayer = new MediaPlayer(_libVlc);
            _mediaPlayer.TimeChanged += OnTimeChanged;
            _mediaPlayer.LengthChanged += OnLengthChanged;
            _mediaPlayer.EndReached += OnEndReached;
            _mediaPlayer.EncounteredError += OnEncounteredError;
        }
        catch (Exception ex)
        {
            // 交由 PlaybackFailed 上报，不阻断应用启动。
            _initError = ex.Message;
        }
    }

    public bool IsAvailable => _mediaPlayer is not null;

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

    public long PositionMs => _mediaPlayer?.Time ?? 0;

    public long DurationMs => _mediaPlayer?.Length ?? 0;

    public bool IsSeekable => _mediaPlayer?.IsSeekable ?? false;

    public int Volume
    {
        get => _mediaPlayer?.Volume ?? 0;
        set
        {
            if (_mediaPlayer is not null)
            {
                _mediaPlayer.Volume = value;
            }
        }
    }

    public bool IsMuted
    {
        get => _mediaPlayer?.Mute ?? false;
        set
        {
            if (_mediaPlayer is not null)
            {
                _mediaPlayer.Mute = value;
            }
        }
    }

    public event EventHandler? PlaybackEnded;

    public event EventHandler<string>? PlaybackFailed;

    public event EventHandler<long>? PositionChanged;

    public event EventHandler<long>? DurationChanged;

    public void Load(string pathOrUrl)
    {
        if (_mediaPlayer is null || _libVlc is null)
        {
            ReportUnavailable();
            return;
        }

        // 严格顺序：先停止 → 释放上一首 Media → 再挂载新 Media，避免 libvlc 仍引用已释放对象。
        _mediaPlayer.Stop();
        _currentMedia?.Dispose();

        // 本地路径用 FromPath，ftp:// 与 http:// 等 MRL 用 FromLocation。
        var fromType = pathOrUrl.Contains("://", StringComparison.Ordinal)
            ? FromType.FromLocation
            : FromType.FromPath;

        _currentMedia = new VlcMedia(_libVlc, pathOrUrl, fromType);
        _currentMedia.AddOption(":no-video");
        _mediaPlayer.Media = _currentMedia;
    }

    public void Play()
    {
        if (_mediaPlayer is null)
        {
            ReportUnavailable();
            return;
        }

        if (_mediaPlayer.Media is null)
        {
            return;
        }

        _mediaPlayer.Play();
    }

    public void Pause() => _mediaPlayer?.SetPause(true);

    public void Stop() => _mediaPlayer?.Stop();

    public void Seek(long positionMs)
    {
        if (_mediaPlayer is null || !_mediaPlayer.IsSeekable)
        {
            return;
        }

        _mediaPlayer.Time = positionMs;
    }

    public void Dispose()
    {
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        _currentMedia?.Dispose();
        _currentMedia = null;

        _libVlc?.Dispose();
        _libVlc = null;
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        => PositionChanged?.Invoke(this, e.Time);

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        => DurationChanged?.Invoke(this, e.Length);

    private void OnEndReached(object? sender, EventArgs e)
        => PlaybackEnded?.Invoke(this, EventArgs.Empty);

    private void OnEncounteredError(object? sender, EventArgs e)
        => PlaybackFailed?.Invoke(this, "播放失败：文件可能损坏，或格式不受支持。");

    private void ReportUnavailable()
        => PlaybackFailed?.Invoke(this, $"音频引擎不可用：{_initError ?? "未知原因"}");
}
