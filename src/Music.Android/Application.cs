using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Music.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // 必须在 Avalonia 初始化 App 之前完成注册：App.Initialize 会用到 AppHost.Services。
            // Android 上不注册系统媒体服务，沿用 Core 的 NoopSystemMediaService（歌名/封面走通知栏后续再做）。
            AppHost.Configure();

            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
