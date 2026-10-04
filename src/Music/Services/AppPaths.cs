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

    /// <summary>在线刮削得到的歌词缓存目录。</summary>
    public static string LyricsDir { get; } = Path.Combine(Root, "lyrics");

    /// <summary>「下载」把曲目另存到的用户可见目录：系统音乐库，不可用时退回应用数据目录。</summary>
    public static string DownloadDir { get; } = ResolveDownloadDir();

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    public static string LibraryDbPath { get; } = Path.Combine(Root, "library.db");

    /// <summary>某曲目刮削歌词的本地缓存路径（曲目 Id 含冒号，需替换为合法文件名字符）。</summary>
    public static string LyricsFileFor(string trackId)
        => Path.Combine(LyricsDir, trackId.Replace(':', '_').Replace('/', '_').Replace('\\', '_') + ".lrc");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(CoversDir);
        Directory.CreateDirectory(LyricsDir);
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

    /// <summary>
    /// 下载目录优先用系统音乐库（用户在自己的「音乐」里就能找到已下载的歌），
    /// 某些平台（如 Android 受限存储）不可写时退回应用数据目录。
    /// </summary>
    private static string ResolveDownloadDir()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (!string.IsNullOrWhiteSpace(music) && IsWritable(music))
        {
            return music;
        }

        return Path.Combine(Root, "Downloads");
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
