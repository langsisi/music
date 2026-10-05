using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UIKit;

namespace Music.iOS;

/// <summary>
/// POSIX 直写文件：AOT 包的 managed File IO 有 JIT 限制（实测 File.AppendAllText
/// 第二次起触发 "Attempting to JIT compile InvokeStub_SafeFileHandle..ctor"，
/// 异常还被各处 catch 吞掉）。启动/崩溃日志是黑屏时的生命线通道，
/// 必须走零 JIT 风险的静态 P/Invoke，不依赖 managed IO 层。
/// </summary>
internal static class DiagFile
{
    private const int O_WRONLY = 0x0001;
    private const int O_CREAT = 0x0200;
    private const int O_APPEND = 0x0400;

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern int open(string path, int flags, int mode);

    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern IntPtr write(int fd, byte[] buffer, IntPtr count);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int close(int fd);

    internal static void AppendAllText(string path, string text)
    {
        var fd = open(path, O_WRONLY | O_CREAT | O_APPEND, 0x180); // 0600：仅应用自身可读写
        if (fd < 0)
        {
            throw new IOException("open 失败 errno=" + Marshal.GetLastWin32Error());
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var written = write(fd, bytes, (IntPtr)bytes.Length);
            if (written != bytes.Length)
            {
                throw new IOException("write 不完整 written=" + written);
            }
        }
        finally
        {
            _ = close(fd);
        }
    }
}

/// <summary>
/// 启动/崩溃日志的系统侧通道。曾直接 P/Invoke 系统 NSLog，真机实测在调用点 SIGABRT 闪退
/// （native 崩溃无法被 try/catch 拦截，详见系统日志 PID 秒级退出记录）；
/// 改用 Console.WriteLine——.NET iOS 的 Console 输出会接到 stderr/系统日志，
/// 零 native 风险，CI 模拟器冒烟的 --console 同样能抓到。
/// </summary>
internal static class DiagLog
{
    internal static void Log(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch
        {
            // 日志只是辅助通道，失败不影响启动。
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
        // 双通道：文件日志（手机「文件」App/爱思可取，主通道）+ Console（stderr/系统日志/CI 可见）。
        // 刻意不用 DateTime 格式化：culture 相关调用依赖 ICU 全球化数据，
        // AOT/裁剪下若 ICU 缺失会抛异常，导致文件日志永远写不出来；
        // TickCount64 是纯整数、单调递增，足够判断卡点与阶段耗时。
        var line = stage + " @" + Environment.TickCount64 + "ms";
        try
        {
            DiagFile.AppendAllText(Path.Combine(PlatformPaths.DataDir, "startup.log"), line + "\n");
        }
        catch (Exception ex)
        {
            // 文件写不进去时把原因带进 stderr，别让线索丢在 catch 里。
            line += " [file failed: " + ex.Message + "]";
        }

        DiagLog.Log("StartupLog: " + line);
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
