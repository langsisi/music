using System;
using System.IO;
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
            // Core 在 iOS 构建（ExcludeLibVlc=true）下不再注册 VlcAudioPlayer，
            // 这里注册 AVFoundation 播放器作为唯一实现。
            services.AddSingleton<IAudioPlayer, IosAudioPlayer>();
            // iOS 沙盒禁止应用内自更新。
            services.AddSingleton<IUpdateInstaller, UnsupportedUpdateInstaller>();
            // Phase 1 复用 Core 的空实现；Phase 2 再换成 MPNowPlayingInfoCenter。
            services.AddSingleton<ISystemMediaService, NoopSystemMediaService>();
        });
        Program.LogStartup("appdelegate: AppHost.Configure 完成（DI 注册，服务均未实例化）");

        // 把 Avalonia 框架日志输出到 Console（NSLog）：真机用爱思助手「实时日志」可查看。
        builder.LogToTrace();

        Program.LogStartup("appdelegate: CustomizeAppBuilder 返回，等待 Avalonia 完成 UI 初始化");
        return base.CustomizeAppBuilder(builder);
    }

    /// <summary>把崩溃堆栈写到 <c>Documents/MusicData/crash.log</c>，方便在「文件」App 里取出。</summary>
    private static void WriteCrashLog(string source, Exception? exception)
    {
        // 堆栈文本单独取：崩溃场景下什么都可能炸，exception.ToString() 自己抛异常也要兜住。
        string dump;
        try
        {
            dump = exception?.ToString() ?? "(没有异常对象)";
        }
        catch (Exception ex)
        {
            dump = "(exception.ToString() 失败：" + ex.Message + ")";
        }

        // 系统侧通道先行：堆栈至少留在 stderr/系统日志，文件写失败线索也不丢。
        DiagLog.Log("CrashLog [" + source + "]: " + dump);

        try
        {
            // 刻意不用 DateTimeOffset 格式化：崩溃可能正出在 ICU/culture 上，
            // 崩溃日志自己再踩一次文化格式化就永远写不出来了。
            var text = "==== [" + source + "] @ +" + Environment.TickCount64 + "ms ====\n"
                     + dump + "\n\n";
            DiagFile.AppendAllText(Path.Combine(PlatformPaths.DataDir, "crash.log"), text);
        }
        catch (Exception)
        {
            // 记日志本身失败就算了，绝不能因为它再抛异常。
        }
    }
}