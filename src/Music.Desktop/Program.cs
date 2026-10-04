using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Music.Services.Security;
using Music.Services.SystemMedia;
using Music.Services.Update;

namespace Music.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppHost.Configure(services =>
        {
#if WINDOWS
            // Windows 上用真正的 SMTC 覆盖 Core 里的空实现，让蓝牙耳机/车机显示歌名并可遥控。
            services.AddSingleton<ISystemMediaService, SmtcSystemMediaService>();
            // 令牌用 DPAPI 加密存储；在线升级覆盖安装目录并重启。
            services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
            services.AddSingleton<IUpdateInstaller, WindowsUpdateInstaller>();
#endif
        });

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
