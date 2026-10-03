namespace Music.Models;

/// <summary>
/// 一首曲目（来自任意音源）。<see cref="Id"/> 是跨音源稳定的唯一标识，
/// 收藏 / 分类 / 缓存都基于它关联。
/// </summary>
public sealed class Track
{
    public required string Id { get; init; }

    /// <summary>所属音源配置的 Id。</summary>
    public required string SourceId { get; init; }

    public required MusicSourceType SourceType { get; init; }

    /// <summary>本地绝对路径，或远程 URI。</summary>
    public required string Path { get; init; }

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public double DurationSeconds { get; set; }

    public int TrackNumber { get; set; }

    public int Year { get; set; }

    /// <summary>本地封面文件路径（扫描时从标签内嵌图片导出）。</summary>
    public string? CoverPath { get; set; }

    public long FileSize { get; set; }

    public System.DateTimeOffset AddedUtc { get; set; } = System.DateTimeOffset.UtcNow;

    /// <summary>远程音源侧的原生 Id（如 Navidrome 的 so_xxx）。</summary>
    public string? RemoteId { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title;

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "未知艺术家" : Artist;

    /// <summary>列表副标题：艺术家 · 专辑（自动省略空值）。</summary>
    public string Subtitle
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(2) { DisplayArtist };

            if (!string.IsNullOrWhiteSpace(Album))
            {
                parts.Add(Album);
            }

            return string.Join(" · ", parts);
        }
    }

    public string FileName => System.IO.Path.GetFileName(Path);

    public bool HasCover => !string.IsNullOrEmpty(CoverPath) && System.IO.File.Exists(CoverPath);

    public string DurationText
    {
        get
        {
            var time = System.TimeSpan.FromSeconds(DurationSeconds <= 0 ? 0 : DurationSeconds);
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
                : $"{time.Minutes}:{time.Seconds:D2}";
        }
    }
}
