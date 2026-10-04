using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.iOS;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Music.Services.Audio;
using Music.Services.SystemMedia;
using Music.Services.Update;

namespace Music.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // 先挂异常处理再交给 base：Avalonia 的初始化就发生在 base.CustomizeAppBuilder 内部。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("AppDomain", e.ExceptionObject as Exception);

        // 必须在 Avalonia 初始化 App 之前完成注册：App.Initialize 会用到 AppHost.Services。
        AppHost.Configure(services =>
        {
            // 覆盖 Core 里的 VlcAudioPlayer：后注册者生效，libvlc 不会被解析。
            services.AddSingleton<IAudioPlayer, IosAudioPlayer>();
            // iOS 沙盒禁止应用内自更新。
            services.AddSingleton<IUpdateInstaller, UnsupportedUpdateInstaller>();
            // Phase 1 复用 Core 的空实现；Phase 2 再换成 MPNowPlayingInfoCenter。
            services.AddSingleton<ISystemMediaService, NoopSystemMediaService>();
        });

        return base.CustomizeAppBuilder(builder);
    }

    /// <summary>把崩溃堆栈写到 <c>Documents/MusicData/crash.log</c>，方便在「文件」App 里取出。</summary>
    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            var text = new StringBuilder()
                .AppendLine($"==== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{source}] ====")
                .AppendLine(exception?.ToString() ?? "(没有异常对象)")
                .AppendLine()
                .ToString();

            File.AppendAllText(Path.Combine(PlatformPaths.DataDir, "crash.log"), text);
        }
        catch (Exception)
        {
            // 记日志本身失败就算了，绝不能因为它再抛异常。
        }
    }
}