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
    double DurationSeconds,
    double PositionSeconds = 0);

/// <summary>
/// 系统媒体信息（Windows 为 SMTC，Android 为 MediaSession）。
/// SMTC / 蓝牙 AVRCP 协议本身不承载歌词，但车机/耳机一般会显示「专辑」字段，
/// 因此播放时可通过 <see cref="UpdateLine"/> 把当前歌词行写进专辑字段（音流同款做法）。
/// </summary>
public interface ISystemMediaService
{
    event EventHandler<MediaControlCommand>? CommandReceived;

    void Update(NowPlayingInfo info);

    /// <summary>
    /// 用当前歌词行替换专辑字段；传入 null 恢复真实专辑名。
    /// </summary>
    void UpdateLine(string? line);

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

    public void UpdateLine(string? line)
    {
    }

    public void Clear()
    {
    }
}
