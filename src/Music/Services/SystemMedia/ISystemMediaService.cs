using System;

namespace Music.Services.SystemMedia;

/// <summary>系统媒体控制中心发来的操作（耳机线控、车机按键等）。</summary>
public enum MediaControlCommand
{
    Play,
    Pause,
    TogglePlay,
    Next,
    Previous,
}

/// <summary>推送到系统媒体中心的信息。</summary>
public readonly record struct NowPlayingInfo(
    string Title,
    string Artist,
    string Album,
    string? CoverPath,
    bool IsPlaying,
    double DurationSeconds);

/// <summary>
/// 系统媒体信息（Windows 为 SMTC）。
/// 注意：SMTC / 蓝牙 AVRCP 只承载标题、艺术家、专辑、封面与播放状态，
/// <b>不包含歌词</b>；逐行歌词请走 <c>LyricsBroadcastServer</c>。
/// </summary>
public interface ISystemMediaService
{
    event EventHandler<MediaControlCommand>? CommandReceived;

    void Update(NowPlayingInfo info);

    void Clear();
}

/// <summary>默认实现：不做任何事（非 Windows 平台或平台能力不可用时）。</summary>
public sealed class NoopSystemMediaService : ISystemMediaService
{
    public event EventHandler<MediaControlCommand>? CommandReceived
    {
        add { }
        remove { }
    }

    public void Update(NowPlayingInfo info)
    {
    }

    public void Clear()
    {
    }
}
