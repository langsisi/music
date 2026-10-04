using System;
using System.IO;

namespace Music.Services.Metadata;

/// <summary>
/// 把封面 / 歌词（可选标题、艺术家、专辑、年份）写入音频文件标签。
/// 任一可选参数为 null / 空（年份为 0）时保持原值不变。失败静默返回 false（不影响文件本身）。
/// </summary>
public static class AudioTagWriter
{
    /// <summary>导出封面时可能使用的扩展名，用于反查已导出的封面。</summary>
    private static readonly string[] CoverExtensions = [".jpg", ".png"];

    public static bool Write(
        string path,
        byte[]? coverBytes,
        string? lyrics,
        string? title = null,
        string? artist = null,
        string? album = null,
        int year = 0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var file = TagLib.File.Create(path);

            if (!string.IsNullOrEmpty(title))
            {
                file.Tag.Title = title;
            }

            if (!string.IsNullOrEmpty(artist))
            {
                file.Tag.Performers = [artist];
            }

            if (!string.IsNullOrEmpty(album))
            {
                file.Tag.Album = album;
            }

            if (year > 0)
            {
                file.Tag.Year = (uint)year;
            }

            if (!string.IsNullOrWhiteSpace(lyrics))
            {
                file.Tag.Lyrics = lyrics;
            }

            if (coverBytes is { Length: > 0 })
            {
                file.Tag.Pictures =
                [
                    new TagLib.Picture(new TagLib.ByteVector(coverBytes))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = DetectImageMime(coverBytes),
                        Description = "Cover",
                    },
                ];
            }

            file.Save();
            return true;
        }
        catch (Exception)
        {
            // 标签写入失败不阻止调用方继续。
            return false;
        }
    }

    public static string DetectImageMime(byte[] bytes)
        => bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
                ? "image/png"
                : "image/jpeg";

    /// <summary>
    /// 把标签内嵌封面导出到封面目录，供列表与播放页复用；没有封面时返回 null。
    /// 本地扫描与远程取流后的补齐共用同一份实现。
    /// </summary>
    public static string? ExportCover(string trackId, TagLib.IPicture[] pictures)
    {
        if (pictures.Length == 0)
        {
            return null;
        }

        var data = pictures[0].Data?.Data;
        if (data is null || data.Length == 0)
        {
            return null;
        }

        var extension = pictures[0].MimeType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true
            ? ".png"
            : ".jpg";

        AppPaths.EnsureCreated();
        var fullPath = Path.Combine(AppPaths.CoversDir, CoverBaseName(trackId) + extension);

        // 已经导出过且大小一致就不重复写盘。
        var existing = new FileInfo(fullPath);
        if (existing.Exists && existing.Length == data.Length)
        {
            return fullPath;
        }

        File.WriteAllBytes(fullPath, data);
        return fullPath;
    }

    /// <summary>
    /// 封面目录里是否已有该曲目的封面（.jpg / .png）。用于同步时跳过重复抓取，
    /// 也用于重新扫描后（索引里的封面路径被重置）直接恢复封面。
    /// </summary>
    public static string? FindExistingCover(string trackId)
    {
        var name = CoverBaseName(trackId);

        foreach (var extension in CoverExtensions)
        {
            var path = Path.Combine(AppPaths.CoversDir, name + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string CoverBaseName(string trackId)
        => trackId.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
}
