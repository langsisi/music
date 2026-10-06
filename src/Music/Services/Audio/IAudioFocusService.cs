using System;

namespace Music.Services.Audio;

/// <summary>系统音频焦点变化类型。</summary>
public enum AudioFocusChange
{
    /// <summary>重新获得焦点，可恢复原音量。</summary>
    Gain,

    /// <summary>永久丢失焦点（其它播放器接管），应放弃焦点并停止播放。</summary>
    Loss,

    /// <summary>暂时丢失焦点（来电等），应先暂停播放。</summary>
    LossTransient,

    /// <summary>暂时丢失焦点但允许继续播放，只需压低音量（导航播报等）。</summary>
    LossTransientCanDuck,
}

/// <summary>
/// 系统音频焦点协商。Android 上用于在导航播报、来电等场景让出音频输出（闪避或暂停）；
/// 桌面 / iOS 使用 <see cref="NoopAudioFocusService"/> 空实现。
/// </summary>
public interface IAudioFocusService : IDisposable
{
    /// <summary>焦点变化通知；实现方可能来自系统线程，订阅方需自行切回 UI 线程。</summary>
    event EventHandler<AudioFocusChange>? FocusChanged;

    /// <summary>申请音频焦点，返回是否申请成功。</summary>
    bool RequestFocus();

    /// <summary>释放音频焦点。</summary>
    void AbandonFocus();
}

/// <summary>默认实现：不做任何事（桌面 / iOS 无需音频焦点协商）。</summary>
public sealed class NoopAudioFocusService : IAudioFocusService
{
    public event EventHandler<AudioFocusChange>? FocusChanged
    {
        add { }
        remove { }
    }

    public bool RequestFocus() => true;

    public void AbandonFocus()
    {
    }

    public void Dispose()
    {
    }
}