using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Music.Services;
using Music.Services.Sources;
using Music.ViewModels;
using Music.Views;

namespace Music;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // 加载设置、应用主题、准备数据库。内部为同步等待且不捕获 UI 同步上下文，因此不会死锁。
        if (AppHost.IsConfigured)
        {
            AppInitializer.Initialize(AppHost.Services);
        }

#if DEBUG
        // 仅桌面端附加 DevTools：Android/iOS 真机上找不到 DevTools 宿主时会抛
        // DevToolsUnreachableException（主线程），表现为启动白屏 / ANR。
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
        {
            this.AttachDeveloperTools();
        }
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var mainViewModel = AppHost.Services.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () => new MainView { DataContext = mainViewModel };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            singleViewLifetime.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();

        // 后台补齐尚未入库的音源，不阻塞窗口显示。
        _ = SyncMissingSourcesAsync();
    }

    private static async Task SyncMissingSourcesAsync()
    {
        try
        {
            var sync = AppHost.Services.GetRequiredService<LibrarySyncService>();
            await sync.SyncMissingSourcesAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 启动期扫描失败不应影响应用运行。
        }
    }
}
