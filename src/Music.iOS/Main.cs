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
        try
        {
            File.AppendAllText(
                Path.Combine(PlatformPaths.DataDir, "startup.log"),
                $"{DateTime.Now:HH:mm:ss.fff}  {stage}\n");
        }
        catch
        {
            // 记日志失败绝不能影响启动。
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
