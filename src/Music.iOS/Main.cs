using System;
using System.IO;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Music.iOS;

/// <summary>
/// 直接 P/Invoke 系统 Foundation 的 NSLog 输出到统一日志：
/// .NET iOS 绑定库并未暴露 NSLog（Foundation.NSLog 不存在，编译期已验证），
/// 而真机上 Console.WriteLine 的 stdout 落到 /dev/null，
/// 只有 NSLog 能进系统日志、被爱思助手「实时日志」检索到。
/// </summary>
internal static class NativeLog
{
    [DllImport(ObjCRuntime.Constants.FoundationLibrary, EntryPoint = "NSLog")]
    private static extern void NSLog(IntPtr format, IntPtr arg);

    internal static void Log(string message)
    {
        try
        {
            // 格式串固定 "%@"，把正文当参数传入：正文里出现 % 字符也不会被误解析。
            using var format = new NSString("%@");
            using var arg = new NSString(message ?? "");
            NSLog(format.Handle, arg.Handle);
        }
        catch
        {
            // 日志只是辅助通道，任何失败都不能影响启动。
        }
    }
}

public static class Program
{
    /// <summary>
    /// 启动阶段日志：真机黑屏时打开 <c>Documents/MusicData/startup.log</c>，
    /// 看最后一行停在哪一步即可定位卡点（每次启动以分隔行开头，多次启动会追加）。
    /// </summary>
    internal static void LogStartup(string stage)
    {
        // 双通道：文件日志（手机「文件」App/爱思可取）+ NSLog（syslog/爱思实时日志/CI 可见）。
        // 刻意不用 DateTime 格式化：culture 相关调用依赖 ICU 全球化数据，
        // AOT/裁剪下若 ICU 缺失会抛异常，导致文件日志永远写不出来；
        // TickCount64 是纯整数、单调递增，足够判断卡点与阶段耗时。
        var line = stage + " @" + Environment.TickCount64 + "ms";
        try
        {
            File.AppendAllText(Path.Combine(PlatformPaths.DataDir, "startup.log"), line + "\n");
        }
        catch (Exception ex)
        {
            // 文件写不进去时把原因带进 syslog，别让线索丢在 catch 里。
            line += " [file failed: " + ex.Message + "]";
        }

        NativeLog.Log("StartupLog: " + line);
    }

    private static void Main(string[] args)
    {
        // 必须最先执行：AppPaths.Root 是静态只读，一旦被其他类型触发就再也改不回来了。
        PlatformPaths.Configure();
        LogStartup("==== 新启动 ====");
        LogStartup("main: PlatformPaths.Configure 完成");

        LogStartup("main: 即将进入 UIApplication.Main（交由 Avalonia 初始化 UI）");
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
