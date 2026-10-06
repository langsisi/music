using Android.App;
using Android.Content;
using Music.Services.SystemMedia;

namespace Music.Android;

/// <summary>
/// 通知栏 / 锁屏媒体卡片按钮的入口：把「上一首 / 播放暂停 / 下一首」转交给
/// <see cref="AndroidSystemMediaService"/>，复用其 CommandReceived 链路。
/// </summary>
/// <remarks>
/// 用显式组件投递 <see cref="PendingIntent"/>，因此 <c>Exported = false</c> 不影响同应用内点击。
/// </remarks>
[BroadcastReceiver(Exported = false)]
public sealed class MediaActionReceiver : BroadcastReceiver
{
    /// <summary>通知按钮动作：上一首。</summary>
    public const string ActionPrevious = "zhusl.music.intent.action.MEDIA_PREVIOUS";

    /// <summary>通知按钮动作：播放 / 暂停切换。</summary>
    public const string ActionPlayPause = "zhusl.music.intent.action.MEDIA_PLAY_PAUSE";

    /// <summary>通知按钮动作：下一首。</summary>
    public const string ActionNext = "zhusl.music.intent.action.MEDIA_NEXT";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var command = intent?.Action switch
        {
            ActionPrevious => MediaControlCommand.Previous,
            ActionNext => MediaControlCommand.Next,
            ActionPlayPause => MediaControlCommand.TogglePlay,
            _ => (MediaControlCommand?)null,
        };

        if (command is not null)
        {
            AndroidSystemMediaService.Current?.DispatchMediaCommand(command.Value);
        }
    }
}