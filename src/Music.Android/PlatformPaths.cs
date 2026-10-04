using System;
using System.Collections.Generic;
using System.Linq;
using Music.Services;

namespace Music.Android;

/// <summary>
/// Android 的文件选择器（StorageProvider）只返回 content:// 而非真实文件路径，
/// 拿来做本地音源扫不出曲目。这里直接把「本机音乐目录」与应用的默认下载目录
/// 通过环境变量注入共享层，由 <see cref="AppPaths"/> 在启动时播种为本地音源。
/// 与 iOS 的 PlatformPaths 保持同一套「环境变量注入」模式。
/// </summary>
internal static class PlatformPaths
{
    public static void Configure()
    {
        try
        {
            var folders = new List<string>();

            // 公共音乐目录（/storage/emulated/0/Music）。
            // 用 global:: 前缀，避免被 Music.Android 命名空间遮蔽。
            AddIfValid(folders, global::Android.OS.Environment
                .GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryMusic)?
                .AbsolutePath);

            // 应用默认下载目录（本机音乐目录不可写时退回应用数据目录）。
            AddIfValid(folders, AppPaths.DownloadDir);

            Environment.SetEnvironmentVariable(
                AppPaths.DefaultLocalFoldersVariable,
                string.Join(';', folders));
        }
        catch (Exception)
        {
            // 注入默认目录失败不应影响启动，退化为「没有默认音源」。
        }
    }

    private static void AddIfValid(List<string> folders, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || folders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        folders.Add(path);
    }
}
