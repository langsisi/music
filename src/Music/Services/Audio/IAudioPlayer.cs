using System;

namespace Music.Services.Audio;

/// <summary>
/// 音频播放器抽象。实现需保证：<b>所有方法只在 UI 线程调用</b>，
/// 事件可能来自播放器内部线程，由上层负责切回 UI 线程。
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>音频引擎是否可用（原生库加载失败时为 false）。</summary>
    bool IsAvailable { get; }

    bool IsPlaying { get; }

    /// <summary>当前位置（毫秒）。</summary>
    long PositionMs { get; }

    /// <summary>总时长（毫秒）。</summary>
    long DurationMs { get; }

    bool IsSeekable { get; }

    /// <summary>音量 0..100。</summary>
    int Volume { get; set; }

    bool IsMuted { get; set; }

    /// <summary>播放自然结束。</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>播放失败（含引擎不可用）。</summary>
    event EventHandler<string>? PlaybackFailed;

    event EventHandler<long>? PositionChanged;

    event EventHandler<long>? DurationChanged;

    /// <summary>装载媒体（本地路径或 URI），装载后处于暂停态，需要再调用 <see cref="Play"/>。</summary>
    void Load(string pathOrUrl);

    void Play();

    void Pause();

    void Stop();

    void Seek(long positionMs);

    /// <summary>
    /// 从头重新播放当前已装载的媒体（单曲循环用）。
    /// 播放自然结束后播放器处于「已结束」状态，此时 <see cref="Seek"/> 与 <see cref="Play"/> 都可能无效，
    /// 实现需重新装载同一地址再播，保证真正回到开头。
    /// </summary>
    void Restart();
}
