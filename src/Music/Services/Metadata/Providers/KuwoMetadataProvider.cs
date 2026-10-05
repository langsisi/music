using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata.Providers;

/// <summary>
/// 酷我音乐：搜索 / 封面（移动端私有接口，尽力而为）。
/// 搜索接口返回的是单引号「类 JSON」且条目内含嵌套对象，用正则宽松提取字段。
/// 歌词接口（m.kuwo.cn songinfoandlrc）已下线、www API 有签名反爬，故不再提供歌词。
/// </summary>
public sealed partial class KuwoMetadataProvider : IMetadataProvider
{
    private const string Referer = "https://www.kuwo.cn/";

    private readonly MetadataHttpClient _http;

    public KuwoMetadataProvider(MetadataHttpClient http) => _http = http;

    public string Id => "kuwo";

    public string DisplayName => "酷我音乐";

    public bool SupportsLyrics => false;

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        TrackQuery query,
        CancellationToken cancellationToken = default)
    {
        var keyword = Uri.EscapeDataString($"{query.Title} {query.Artist}".Trim());
        var url =
            $"https://search.kuwo.cn/r.s?all={keyword}&ft=music&itemset=web_2013&client=kt&pn=0&rn=10&rformat=json&encoding=utf8";

        var body = await _http.GetStringAsync(url, cancellationToken, Referer).ConfigureAwait(false);
        var results = new List<MetadataCandidate>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return results;
        }

        foreach (Match match in ItemRegex().Matches(body))
        {
            var item = match.Value;
            var rid = Field(item, "MUSICRID").Replace("MUSIC_", string.Empty, StringComparison.Ordinal);
            var name = Field(item, "SONGNAME");
            if (string.IsNullOrEmpty(rid) || string.IsNullOrEmpty(name))
            {
                continue;
            }

            var shortPic = Field(item, "web_albumpic_short");
            var cover = string.IsNullOrEmpty(shortPic)
                ? null
                : $"https://img1.kuwo.cn/star/albumcover/{shortPic.TrimStart('/')}";

            results.Add(new MetadataCandidate(
                Id,
                rid,
                Unescape(name),
                Unescape(Field(item, "ARTIST")),
                Unescape(Field(item, "ALBUM")),
                cover,
                HasLyrics: false,
                Year: MetadataYear.FromDate(Field(item, "RELEASEDATE"))));
        }

        return results;
    }

    public Task<byte[]?> GetCoverAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(candidate.CoverUrl)
            ? Task.FromResult<byte[]?>(null)
            : _http.GetBytesAsync(candidate.CoverUrl, cancellationToken, Referer);

    /// <summary>酷我不再提供歌词接口。</summary>
    public Task<string?> GetLyricsAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    private static string Field(string item, string name)
    {
        var match = Regex.Match(item, $@"'{name}'\s*:\s*'([^']*)'");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>酷我部分字段带 HTML 实体（如 <c>&amp;nbsp;</c>），做最小化还原。</summary>
    private static string Unescape(string value)
        => value.Replace("&nbsp;", " ", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Trim();

    // 条目内含一层嵌套对象（如 'audiobookpayinfo':{'download':'0','play':'0'}），
    // 允许嵌套一层大括号，否则匹配不到任何条目。
    [GeneratedRegex(@"\{(?:[^{}]|\{[^{}]*\})*'MUSICRID'\s*:\s*'MUSIC_\d+'(?:[^{}]|\{[^{}]*\})*\}")]
    private static partial Regex ItemRegex();
}
