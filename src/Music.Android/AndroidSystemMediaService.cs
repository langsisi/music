using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Music.Services.SystemMedia;

namespace Music.Android;

/// <summary>
/// Android 系统媒体会话：向蓝牙耳机 / 车机的 AVRCP 推送歌名、艺术家、封面与播放状态，
/// 并接管媒体按键。开启歌词广播后，当前歌词行会替换「专辑」字段（音流同款做法），
/// 车机屏幕即可逐行显示歌词。
/// </summary>
/// <remarks>
/// 同时发一条 <c>MediaStyle</c> 前台通知（<see cref="MediaPlaybackService"/>）：Android 11+
/// 的通知栏下拉「正在播放」卡片与锁屏媒体控制要求媒体会话挂在这样一条通知上，
/// 只建会话不发通知的话，除了蓝牙 AVRCP 之外系统 UI 不会显示控制条。
/// 所有 MediaSession / 通知操作都切回主线程；任何平台异常都静默降级，不影响播放。
/// </remarks>
public sealed class AndroidSystemMediaService : ISystemMediaService
{
    private const long Actions =
        PlaybackState.ActionPlay |
        PlaybackState.ActionPause |
        PlaybackState.ActionPlayPause |
        PlaybackState.ActionSkipToNext |
        PlaybackState.ActionSkipToPrevious |
        PlaybackState.ActionStop;

    /// <summary>通知渠道 ID（Android 8+ 必须先建渠道才能发通知）。</summary>
    private const string ChannelId = "music_playback";

    /// <summary>通知栏 / 锁屏上三个按钮的 PendingIntent 请求码。</summary>
    private const int PreviousRequestCode = 1;
    private const int PlayPauseRequestCode = 2;
    private const int NextRequestCode = 3;
    private const int ContentRequestCode = 4;

    private readonly Handler _handler = new(Looper.MainLooper!);
    private MediaSession? _session;
    private SessionCallback? _callback;
    private NowPlayingInfo _info;
    private bool _hasInfo;
    private string? _line;
    private string? _artPath;
    private Bitmap? _art;
    private bool _channelCreated;

    public AndroidSystemMediaService()
    {
        Current = this;
    }

    /// <summary>供通知按钮（<see cref="MediaActionReceiver"/>）转发命令使用。</summary>
    internal static AndroidSystemMediaService? Current { get; private set; }

    public event EventHandler<MediaControlCommand>? CommandReceived;

    /// <summary>由通知按钮转发过来的媒体命令；与媒体按键走同一条 <see cref="CommandReceived"/> 链路。</summary>
    internal void DispatchMediaCommand(MediaControlCommand command)
        => CommandReceived?.Invoke(this, command);

    public void Update(NowPlayingInfo info)
    {
        _info = info;
        _hasInfo = true;
        _line = null;
        _handler.Post(Publish);
    }

    public void UpdateLine(string? line)
    {
        _line = string.IsNullOrWhiteSpace(line) ? null : line;
        if (_hasInfo)
        {
            // 通知文案用艺术家（不随歌词变化），这里只需刷新蓝牙 / 车机的元数据。
            _handler.Post(PublishMetadata);
        }
    }

    public void Clear()
    {
        _hasInfo = false;
        _line = null;

        _handler.Post(() =>
        {
            if (_session is not null)
            {
                try
                {
                    _session.SetPlaybackState(new PlaybackState.Builder()
                        .SetActions(Actions)
                        .SetState(PlaybackStateCode.Stopped, 0, 0, SystemClock.UptimeMillis())
                        .Build());
                    _session.SetMetadata(null);
                }
                catch (Exception)
                {
                    // 忽略。
                }
            }

            StopPlaybackService();
        });
    }

    private MediaSession EnsureSession()
    {
        if (_session is not null)
        {
            return _session;
        }

        var session = new MediaSession(global::Android.App.Application.Context!, "Music");
        _callback = new SessionCallback(command => CommandReceived?.Invoke(this, command));
        session.SetCallback(_callback, _handler);
        session.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
        session.Active = true;
        _session = session;
        return session;
    }

    private void Publish()
    {
        try
        {
            var session = EnsureSession();
            PublishMetadata();
            session.SetPlaybackState(BuildState(_info.PositionSeconds, _info.IsPlaying));
            PublishNotification(session);
        }
        catch (Exception)
        {
            // 系统媒体能力不可用时静默降级。
        }
    }

    private void PublishMetadata()
    {
        if (!_hasInfo)
        {
            return;
        }

        try
        {
            var builder = new MediaMetadata.Builder()
                .PutString(MediaMetadata.MetadataKeyTitle, _info.Title ?? string.Empty)
                .PutString(MediaMetadata.MetadataKeyArtist, _info.Artist ?? string.Empty)
                .PutString(MediaMetadata.MetadataKeyAlbumArtist, _info.Artist ?? string.Empty)
                .PutString(MediaMetadata.MetadataKeyAlbum, _line ?? _info.Album ?? string.Empty)
                .PutLong(MediaMetadata.MetadataKeyDuration,
                    Math.Max(0L, (long)Math.Round(_info.DurationSeconds * 1000)));

            var art = LoadArt(_info.CoverPath);
            if (art is not null)
            {
                // 两个键都写：不同蓝牙协议栈读取的键不一致。
                builder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, art);
                builder.PutBitmap(MediaMetadata.MetadataKeyArt, art);
            }

            EnsureSession().SetMetadata(builder.Build());
        }
        catch (Exception)
        {
            // 封面解码失败等不影响文字字段的推送。
        }
    }

    private PlaybackState BuildState(double positionSeconds, bool isPlaying) =>
        new PlaybackState.Builder()
            .SetActions(Actions)
            .SetState(
                isPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused,
                Math.Max(0L, (long)Math.Round(positionSeconds * 1000)),
                isPlaying ? 1f : 0f,
                SystemClock.UptimeMillis())
            .Build();

    /// <summary>
    /// 发布 / 刷新前台通知。服务未运行时先把它拉起来（Android 8+ 必须先 StartForegroundService）。
    /// </summary>
    private void PublishNotification(MediaSession session)
    {
        if (!_hasInfo)
        {
            return;
        }

        var notification = BuildNotification(session);
        var service = MediaPlaybackService.Current;
        if (service is not null)
        {
            service.SetNotification(notification);
            return;
        }

        MediaPlaybackService.PendingNotification = notification;
        StartPlaybackService();
    }

    private static void StartPlaybackService()
    {
        try
        {
            var context = global::Android.App.Application.Context!;
            var intent = new Intent(context, typeof(MediaPlaybackService));

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception)
        {
            // Android 12+ 对后台启动前台服务有限制；失败时仅表现为没有系统控制条，不影响播放。
        }
    }

    private void StopPlaybackService()
    {
        try
        {
            var context = global::Android.App.Application.Context!;
            context.StopService(new Intent(context, typeof(MediaPlaybackService)));
            NotificationManagerFrom(context)?.Cancel(MediaPlaybackService.NotificationId);
        }
        catch (Exception)
        {
            // 忽略。
        }
    }

    private Notification BuildNotification(MediaSession session)
    {
        var context = global::Android.App.Application.Context!;
        EnsureChannel(context);

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, ChannelId)
            : new Notification.Builder(context);

        builder.SetSmallIcon(Resource.Drawable.ic_stat_music);
        builder.SetContentTitle(_info.Title ?? string.Empty);
        builder.SetContentText(_info.Artist ?? string.Empty);
        builder.SetContentIntent(BuildContentIntent(context));
        builder.SetOngoing(_info.IsPlaying);
        builder.SetShowWhen(false);
        builder.SetVisibility(NotificationVisibility.Public);

        var art = LoadArt(_info.CoverPath);
        if (art is not null)
        {
            builder.SetLargeIcon(art);
        }

        var style = new Notification.MediaStyle();
        style.SetMediaSession(session.SessionToken);
        style.SetShowActionsInCompactView(0, 1, 2);
        builder.SetStyle(style);

        builder.AddAction(BuildAction(
            context, PreviousRequestCode, Resource.Drawable.ic_media_previous, "上一首", MediaActionReceiver.ActionPrevious));

        builder.AddAction(BuildAction(
            context,
            PlayPauseRequestCode,
            _info.IsPlaying ? Resource.Drawable.ic_media_pause : Resource.Drawable.ic_media_play,
            _info.IsPlaying ? "暂停" : "播放",
            MediaActionReceiver.ActionPlayPause));

        builder.AddAction(BuildAction(
            context, NextRequestCode, Resource.Drawable.ic_media_next, "下一首", MediaActionReceiver.ActionNext));

        return builder.Build();
    }

    private static Notification.Action BuildAction(
        Context context, int requestCode, int iconResource, string title, string action)
    {
        var intent = new Intent(context, typeof(MediaActionReceiver));
        intent.SetAction(action);

        var pendingIntent = PendingIntent.GetBroadcast(
            context,
            requestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var icon = Icon.CreateWithResource(context, iconResource);
        return new Notification.Action.Builder(icon, title, pendingIntent).Build();
    }

    private static PendingIntent? BuildContentIntent(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        return PendingIntent.GetActivity(
            context,
            ContentRequestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    /// <summary>Android 8+ 必需：创建低重要性、无声的播放通知渠道（只建一次）。</summary>
    private void EnsureChannel(Context context)
    {
        if (_channelCreated || !OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        try
        {
            var channel = new NotificationChannel(ChannelId, "播放控制", NotificationImportance.Low)
            {
                Description = "显示正在播放的歌曲与播放控制按钮",
            };

            NotificationManagerFrom(context)?.CreateNotificationChannel(channel);
        }
        catch (Exception)
        {
            // 忽略。
        }

        _channelCreated = true;
    }

    private static NotificationManager? NotificationManagerFrom(Context context)
        => context.GetSystemService(Context.NotificationService) as NotificationManager;

    /// <summary>按曲目缓存缩放后的封面位图，避免每次刷新都重新解码。</summary>
    private Bitmap? LoadArt(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            _art?.Dispose();
            _art = null;
            _artPath = null;
            return null;
        }

        if (path == _artPath)
        {
            return _art;
        }

        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, bounds);

            const int maxSize = 480;
            var sample = 1;
            while (bounds.OutWidth / sample > maxSize || bounds.OutHeight / sample > maxSize)
            {
                sample *= 2;
            }

            var options = new BitmapFactory.Options { InSampleSize = sample };
            var bitmap = BitmapFactory.DecodeFile(path, options);

            _art?.Dispose();
            _art = bitmap;
            _artPath = path;
            return bitmap;
        }
        catch (Exception)
        {
            _art?.Dispose();
            _art = null;
            _artPath = null;
            return null;
        }
    }

    private sealed class SessionCallback : MediaSession.Callback
    {
        private readonly Action<MediaControlCommand> _raise;

        public SessionCallback(Action<MediaControlCommand> raise) => _raise = raise;

        public override void OnPlay() => _raise(MediaControlCommand.Play);

        public override void OnPause() => _raise(MediaControlCommand.Pause);

        public override void OnSkipToNext() => _raise(MediaControlCommand.Next);

        public override void OnSkipToPrevious() => _raise(MediaControlCommand.Previous);
    }
}