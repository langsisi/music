using System;
using System.IO;

namespace Music.iOS;

/// <summary>
/// iOS 沙盒里只有 <c>Documents</c> 能被「文件」App / Finder 看到，
/// 因此把数据目录与音乐导入目录都放在它下面，用户才能自行导入音乐。
/// </summary>
internal static class PlatformPaths
{
    public static string DocumentsDir { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>应用数据根目录（会被 <c>MUSIC_APP_DATA_DIR</c> 指向它）。</summary>
    public static string DataDir { get; } = Path.Combine(DocumentsDir, "MusicData");

    /// <summary>用户放音乐文件、或把文件分享进本应用后的落点。</summary>
    public static string MusicImportDir { get; } = Path.Combine(DocumentsDir, "Music");

    public static void Configure()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(MusicImportDir);

        // AppPaths.ResolveRoot() 优先读这个变量，从而绕开 iOS 上不可见的 Library 目录。
        Environment.SetEnvironmentVariable("MUSIC_APP_DATA_DIR", DataDir);
    }
}