using System;
using Microsoft.Extensions.DependencyInjection;
using Music.Services.Library;

namespace Music.Services;

/// <summary>
/// 启动初始化：准备目录、加载设置、建库。
/// 在 <c>App.Initialize()</c> 中同步调用（内部一律 ConfigureAwait(false)，不会与 UI 线程互相等待）。
/// </summary>
public static class AppInitializer
{
    public static void Initialize(IServiceProvider services)
    {
        AppPaths.EnsureCreated();

        var settings = services.GetRequiredService<ISettingsStore>();
        settings.LoadAsync().GetAwaiter().GetResult();

        ThemeService.Apply(settings.Current.ThemeMode);

        var library = services.GetRequiredService<ILibraryStore>();
        library.InitializeAsync().GetAwaiter().GetResult();
    }
}
