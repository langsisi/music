using System;
using System.IO;

namespace Music.Services;

/// <summary>
/// 应用数据目录（默认 %LOCALAPPDATA%\Music）。
/// 若该位置不可写（受限环境、只读配置等），自动退回到临时目录，保证应用仍能启动。
/// 也可通过环境变量 <c>MUSIC_APP_DATA_DIR</c> 指定（便携模式 / 测试）。
/// </summary>
public static class AppPaths
{
    private const string OverrideVariable = "MUSIC_APP_DATA_DIR";

    public static string Root { get; } = ResolveRoot();

    /// <summary>音频缓存目录。</summary>
    public static string CacheDir { get; } = Path.Combine(Root, "cache");

    /// <summary>从标签中导出的封面文件目录。</summary>
    public static string CoversDir { get; } = Path.Combine(Root, "covers");

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    public static string LibraryDbPath { get; } = Path.Combine(Root, "library.db");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(CoversDir);
    }

    private static string ResolveRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        var preferred = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Music");

        return IsWritable(preferred) ? preferred : Path.Combine(Path.GetTempPath(), "Music");
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
