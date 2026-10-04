using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Lyrics;

/// <summary>
/// 在线刮削缓存下来的歌词：读取 <see cref="AppPaths.LyricsFileFor"/> 指向的本地 .lrc。
/// 排在本地同名 .lrc / 内嵌歌词之后、Navidrome 远程歌词之前。
/// </summary>
public sealed class ScrapedLrcProvider : ILyricsProvider
{
    public bool CanHandle(Track track) => File.Exists(AppPaths.LyricsFileFor(track.Id));

    public async Task<string?> GetRawLyricsAsync(Track track, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = AppPaths.LyricsFileFor(track.Id);
            return File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (IOException)
        {
            // 读不到就当没有歌词。
            return null;
        }
    }
}
