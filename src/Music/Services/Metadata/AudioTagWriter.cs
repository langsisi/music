using System;
using System.IO;

namespace Music.Services.Metadata;

/// <summary>
/// 把封面 / 歌词（可选标题、艺术家、专辑）写入音频文件标签。
/// 任一可选参数为 null / 空时保持原值不变。失败静默返回 false（不影响文件本身）。
/// </summary>
public static class AudioTagWriter
{
    public static bool Write(
        string path,
        byte[]? coverBytes,
        string? lyrics,
        string? title = null,
        string? artist = null,
        string? album = null)
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
}
