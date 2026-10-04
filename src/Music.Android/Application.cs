using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Music.Services.Security;
using Music.Services.SystemMedia;
using Music.Services.Update;
using System;
using System.IO;
using System.Text;

namespace Music.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            // 先挂异常处理再交给 base：Avalonia 的初始化就发生在 base.OnCreate 里。
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                WriteCrashLog("AppDomain", e.ExceptionObject as Exception);

            AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
                WriteCrashLog("Android", e.Exception);

            // 必须早于 base.OnCreate：Avalonia 初始化（含 AppPaths 静态初始化）发生在其内部，
            // 而默认本地音源目录要靠这里注入的环境变量。
            PlatformPaths.Configure();

            base.OnCreate();
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // 必须在 Avalonia 初始化 App 之前完成注册：App.Initialize 会用到 AppHost.Services。
            // Android 上用 MediaSession 推送歌名/封面/播放状态给蓝牙耳机和车机，
            // 开启歌词广播时当前歌词行会写入专辑字段显示在车机屏幕上。
            AppHost.Configure(services =>
            {
                services.AddSingleton<ISystemMediaService, AndroidSystemMediaService>();
                // 私有仓库令牌用系统 Keystore 加密；升级时下载 apk 并拉起系统安装器。
                services.AddSingleton<ISecretProtector, KeystoreSecretProtector>();
                services.AddSingleton<IUpdateInstaller, AndroidUpdateInstaller>();
            });

            // 不用 WithInterFont()：Inter 不含中文字形，Android 上直接用系统字体更合适。
            return base.CustomizeAppBuilder(builder);
        }

        /// <summary>
        /// 把崩溃堆栈写到应用的外部私有目录，路径是
        /// <c>/sdcard/Android/data/&lt;包名&gt;/files/crash.log</c>，
        /// 用手机文件管理器或数据线就能取出来，不必连 adb。
        /// </summary>
        private void WriteCrashLog(string source, Exception? exception)
        {
            try
            {
                var directory = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir?.AbsolutePath;
                if (directory is null)
                {
                    return;
                }

                var text = new StringBuilder()
                    .AppendLine($"==== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{source}] ====")
                    .AppendLine(exception?.ToString() ?? "(没有异常对象)")
                    .AppendLine()
                    .ToString();

                File.AppendAllText(Path.Combine(directory, "crash.log"), text);
            }
            catch (Exception)
            {
                // 记日志本身失败就算了，绝不能因为它再抛异常。
            }
        }
    }
}
