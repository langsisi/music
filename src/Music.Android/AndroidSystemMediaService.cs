using System;
using Android.Graphics;
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
/// 所有 MediaSession 操作都切回主线程；任何平台异常都静默降级，不影响播放。
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

    private readonly Handler _handler = new(Looper.MainLooper!);
    private MediaSession? _session;
    private SessionCallback? _callback;
    private NowPlayingInfo _info;
    private bool _hasInfo;
    private string? _line;
    private string? _artPath;
    private Bitmap? _art;

    public event EventHandler<MediaControlCommand>? CommandReceived;

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
            _handler.Post(PublishMetadata);
        }
    }

    public void Clear()
    {
        _hasInfo = false;
        _line = null;

        _handler.Post(() =>
        {
            if (_session is null)
            {
                return;
            }

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
