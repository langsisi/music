using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Lyrics;

/// <summary>歌词来源（本地 .lrc / 内嵌标签 / Navidrome 等）。</summary>
public interface ILyricsProvider
{
    bool CanHandle(Track track);

    /// <summary>返回原始歌词文本（可能是 LRC 或纯文本），没有则返回 null。</summary>
    Task<string?> GetRawLyricsAsync(Track track, CancellationToken cancellationToken = default);
}

/// <summary>
/// 本地曲目的歌词：优先同名 <c>.lrc</c> 文件，其次读取音频标签里的内嵌歌词。
/// </summary>
public sealed class LocalLrcProvider : ILyricsProvider
{
    public bool CanHandle(Track track) => track.SourceType == MusicSourceType.Local;

    public async Task<string?> GetRawLyricsAsync(Track track, CancellationToken cancellationToken = default)
    {
        var fromFile = await TryReadSidecarLyricsAsync(track.Path, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(fromFile))
        {
            return fromFile;
        }

        return TryReadEmbeddedLyrics(track.Path);
    }

    private static async Task<string?> TryReadSidecarLyricsAsync(string audioPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var baseName = Path.Combine(directory, Path.GetFileNameWithoutExtension(audioPath));

        foreach (var candidate in new[] { baseName + ".lrc", baseName + ".LRC" })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return await File.ReadAllTextAsync(candidate, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                // 读不到就当没有歌词。
            }
        }

        return null;
    }

    private static string? TryReadEmbeddedLyrics(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            var lyrics = file.Tag.Lyrics;
            return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
        }
        catch (System.Exception)
        {
            return null;
        }
    }
}
