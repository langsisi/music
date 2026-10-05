using System;
using System.IO;
using UIKit;

namespace Music.iOS;

public static class Program
{
    /// <summary>
    /// 启动阶段日志：真机黑屏时打开 <c>Documents/MusicData/startup.log</c>，
    /// 看最后一行停在哪一步即可定位卡点（每次启动以分隔行开头，多次启动会追加）。
    /// </summary>
    internal static void LogStartup(string stage)
    {
        // 双通道：文件日志（手机「文件」App/爱思可取）+ NSLog（syslog/CI 控制台可见）。
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

        try
        {
            Foundation.NSLog("%@", "StartupLog: " + line);
        }
        catch
        {
            // NSLog 只是第二通道，失败也不能影响启动。
        }
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
