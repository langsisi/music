#if WINDOWS
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Music.Services.SystemMedia;
using Windows.Media;
using Windows.Storage.Streams;

namespace Music.Desktop;

/// <summary>
/// Windows 系统媒体信息（SMTC）集成：让蓝牙耳机 / 车机显示歌名、艺术家、专辑与封面，并接管媒体键。
/// </summary>
/// <remarks>
/// 说明两点限制：
/// 1. SMTC / AVRCP 协议本身不承载歌词，逐行歌词由内置广播服务提供。
/// 2. 未打包（非 MSIX）应用在部分 Windows 版本上会话可见性不保证，
///    因此这里全部用 try/catch 包裹，失败时静默降级，不影响播放。
/// </remarks>
public sealed class SmtcSystemMediaService : ISystemMediaService
{
    private SystemMediaTransportControls? _controls;
    private bool _unavailable;

    public event EventHandler<MediaControlCommand>? CommandReceived;

    public void Update(NowPlayingInfo info)
    {
        var controls = EnsureControls();
        if (controls is null)
        {
            return;
        }

        try
        {
            var updater = controls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = info.Title;
            updater.MusicProperties.Artist = info.Artist;
            updater.MusicProperties.AlbumTitle = info.Album;

            if (!string.IsNullOrWhiteSpace(info.CoverPath) && System.IO.File.Exists(info.CoverPath))
            {
                try
                {
                    updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(info.CoverPath));
                }
                catch (Exception)
                {
                    // 封面格式不受支持时忽略，不影响其它字段。
                }
            }

            updater.Update();
            controls.PlaybackStatus = info.IsPlaying
                ? MediaPlaybackStatus.Playing
                : MediaPlaybackStatus.Paused;
        }
        catch (Exception)
        {
            _unavailable = true;
        }
    }

    public void Clear()
    {
        if (_controls is null)
        {
            return;
        }

        try
        {
            _controls.PlaybackStatus = MediaPlaybackStatus.Stopped;
            _controls.DisplayUpdater.ClearAll();
        }
        catch (Exception)
        {
            // 忽略。
        }
    }

    private SystemMediaTransportControls? EnsureControls()
    {
        if (_controls is not null)
        {
            return _controls;
        }

        if (_unavailable)
        {
            return null;
        }

        try
        {
            var handle = GetMainWindowHandle();
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            _controls = SystemMediaTransportControlsInterop.GetForWindow(handle);
            _controls.IsEnabled = true;
            _controls.IsPlayEnabled = true;
            _controls.IsPauseEnabled = true;
            _controls.IsNextEnabled = true;
            _controls.IsPreviousEnabled = true;
            _controls.ButtonPressed += OnButtonPressed;

            return _controls;
        }
        catch (Exception)
        {
            _unavailable = true;
            return null;
        }
    }

    private static IntPtr GetMainWindowHandle()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => MediaControlCommand.Play,
            SystemMediaTransportControlsButton.Pause => MediaControlCommand.Pause,
            SystemMediaTransportControlsButton.Next => MediaControlCommand.Next,
            SystemMediaTransportControlsButton.Previous => MediaControlCommand.Previous,
            _ => (MediaControlCommand?)null,
        };

        if (command is null)
        {
            return;
        }

        // SMTC 回调在系统线程触发，交给订阅方切回 UI 线程。
        CommandReceived?.Invoke(this, command.Value);
    }
}
#endif
