using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Music.Models;
using Music.Services.Library;

namespace Music.Services;

/// <summary>
/// 启动初始化：准备目录、加载设置、播种默认音源、建库。
/// 在 <c>App.Initialize()</c> 中同步调用（内部一律 ConfigureAwait(false)，不会与 UI 线程互相等待）。
/// </summary>
public static class AppInitializer
{
    public static void Initialize(IServiceProvider services)
    {
        AppPaths.EnsureCreated();

        var settings = services.GetRequiredService<ISettingsStore>();
        settings.LoadAsync().GetAwaiter().GetResult();

        SeedDefaultLocalFolders(settings);

        ThemeService.Apply(settings.Current.ThemeMode);

        var library = services.GetRequiredService<ILibraryStore>();
        library.InitializeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 把平台注入的默认目录（Android 上的本机音乐与下载目录）播种为本地音源。
    /// Android 的文件选择器只返回 content:// 拿不到真实路径，故由平台侧直接给目录。
    /// 仅播种一次：用户清空后不再自动加回来。
    /// 注意：只写配置、绝不在这里创建目录——外部存储（如 /storage/emulated/0/Music）
    /// 的写操作在 Android 上可能阻塞主线程，导致启动白屏 / ANR。
    /// </summary>
    private static void SeedDefaultLocalFolders(ISettingsStore settings)
    {
        try
        {
            var current = settings.Current;
            if (current.DefaultLocalFoldersSeeded)
            {
                return;
            }

            var folders = AppPaths.GetDefaultLocalFolders();
            if (folders.Count == 0)
            {
                return;
            }

            var config = current.Sources.OfType<LocalSourceConfig>().FirstOrDefault();
            if (config is null)
            {
                config = new LocalSourceConfig();
                current.Sources.Add(config);
            }

            foreach (var folder in folders)
            {
                if (!config.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    config.Folders.Add(folder);
                }
            }

            current.DefaultLocalFoldersSeeded = true;
            settings.SaveAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 无论播种出什么问题都不能拖垮启动。
        }
    }
}
