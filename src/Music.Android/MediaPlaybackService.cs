using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Music.Android;

/// <summary>
/// 媒体播放前台服务：Android 11+ 的通知栏 / 锁屏媒体控制卡片要求媒体会话挂在一个
/// 带 <c>MediaStyle</c> 的前台通知上；同时前台服务也让后台播放不被系统回收。
/// </summary>
/// <remarks>
/// 服务本身不持有播放状态——状态在应用进程内的播放器里，这里只负责把
/// <see cref="AndroidSystemMediaService"/> 构建好的通知发成前台通知。
/// </remarks>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class MediaPlaybackService : Service
{
    /// <summary>前台通知 ID。</summary>
    public const int NotificationId = 1001;

    /// <summary>服务尚未启动时由 <see cref="AndroidSystemMediaService"/> 预置的首个通知。</summary>
    internal static Notification? PendingNotification;

    /// <summary>当前运行中的服务实例（刷新通知用）；未运行时为 null。</summary>
    public static MediaPlaybackService? Current { get; private set; }

    public override void OnCreate()
    {
        base.OnCreate();
        Current = this;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // 必须在 StartForegroundService 后的 5 秒内调用 StartForeground，否则系统 ANR / 崩溃。
        if (PendingNotification is not null)
        {
            ApplyForeground(PendingNotification);
        }

        // 被系统杀死后不自动重启：播放状态在应用进程内，服务单独重启也没有内容可播。
        return StartCommandResult.NotSticky;
    }

    /// <summary>刷新前台通知（播放 / 暂停、切歌时由系统媒体服务调用）。</summary>
    public void SetNotification(Notification notification)
    {
        try
        {
            ApplyForeground(notification);
        }
        catch (Exception)
        {
            // 通知刷新失败不影响播放。
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        PendingNotification = null;

        try
        {
            // StopForeground(StopForegroundFlags) 是 API 24+；更低版本由 NotificationManager.Cancel 兜底清理。
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                StopForeground(StopForegroundFlags.Remove);
            }
        }
        catch (Exception)
        {
            // 忽略。
        }

        base.OnDestroy();
    }

    private void ApplyForeground(Notification notification)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }
}