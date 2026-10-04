namespace Music.Models;

/// <summary>
/// GD音乐台（music.gdstudio.xyz）搜索结果中的一首在线曲目。
/// 只承载搜索阶段能拿到的信息，播放地址 / 封面 / 歌词在需要时再按 Id 现取。
/// </summary>
public sealed class OnlineTrack
{
    /// <summary>音源标识（如 netease、joox）。</summary>
    public required string Source { get; init; }

    /// <summary>音源内的歌曲 Id。</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Artist { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    /// <summary>封面 Id（取封面用）。</summary>
    public string PicId { get; init; } = string.Empty;

    /// <summary>取流 Id（部分音源与搜索 Id 不同）。</summary>
    public string UrlId { get; init; } = string.Empty;

    /// <summary>歌词 Id（部分音源与搜索 Id 不同）。</summary>
    public string LyricId { get; init; } = string.Empty;

    /// <summary>跨音源唯一键，用于列表去重。</summary>
    public string Key => $"{Source}:{Id}";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? "未知曲目" : Name;

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "未知艺术家" : Artist;

    public string Subtitle => string.IsNullOrWhiteSpace(Album)
        ? DisplayArtist
        : $"{DisplayArtist} · {Album}";

    /// <summary>音源显示名（如「网易云音乐」）。</summary>
    public string SourceName => OnlineSources.DisplayName(Source);

    /// <summary>在线曲目在播放队列里用 <c>source|urlId</c> 表示取流身份。</summary>
    public string StreamIdentity => BuildStreamIdentity(Source, UrlId);

    /// <summary>转成播放器可用的曲目：取流地址在播放时按 <see cref="StreamIdentity"/> 现取。</summary>
    public Track ToPlayableTrack() => new()
    {
        Id = BuildTrackId(Source, Id),
        SourceId = Source,
        SourceType = MusicSourceType.Online,
        Path = string.Empty,
        Title = Name,
        Artist = Artist,
        Album = Album,
        RemoteId = StreamIdentity,
    };

    /// <summary>在线曲目的本地唯一 Id（用于歌词缓存等按 Id 关联的场景）。</summary>
    public static string BuildTrackId(string source, string id) => $"online:{source}:{id}";

    public static string BuildStreamIdentity(string source, string urlId) => $"{source}|{urlId}";

    public static bool TryParseStreamIdentity(string? value, out string source, out string id)
    {
        source = string.Empty;
        id = string.Empty;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var index = value.IndexOf('|');
        if (index <= 0 || index == value.Length - 1)
        {
            return false;
        }

        source = value[..index];
        id = value[(index + 1)..];
        return true;
    }
}