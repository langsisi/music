using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata.Providers;

/// <summary>
/// 酷我音乐：搜索 / 封面 / 歌词（移动端私有接口，尽力而为）。
/// 搜索接口返回的是单引号「类 JSON」，用正则宽松提取字段。
/// </summary>
public sealed partial class KuwoMetadataProvider : IMetadataProvider
{
    private const string Referer = "https://www.kuwo.cn/";

    private readonly MetadataHttpClient _http;

    public KuwoMetadataProvider(MetadataHttpClient http) => _http = http;

    public string Id => "kuwo";

    public string DisplayName => "酷我音乐";

    public bool SupportsLyrics => true;

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
                HasLyrics: true));
        }

        return results;
    }

    public Task<byte[]?> GetCoverAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(candidate.CoverUrl)
            ? Task.FromResult<byte[]?>(null)
            : _http.GetBytesAsync(candidate.CoverUrl, cancellationToken, Referer);

    public async Task<string?> GetLyricsAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
    {
        var url = $"https://m.kuwo.cn/newh5/singles/songinfoandlrc?musicId={candidate.RemoteId}";
        var json = await _http.GetStringAsync(url, cancellationToken, Referer).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("lrclist", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var line in list.EnumerateArray())
            {
                if (!line.TryGetProperty("time", out var timeNode)
                    || !line.TryGetProperty("lineLyric", out var lyricNode)
                    || lyricNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = lyricNode.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var seconds = ParseSeconds(timeNode);
                var span = TimeSpan.FromSeconds(seconds);
                builder.Append(CultureInfo.InvariantCulture, $"[{span.Minutes:D2}:{span.Seconds:D2}.{span.Milliseconds / 10:D2}]");
                builder.AppendLine(text);
            }

            return builder.Length == 0 ? null : builder.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double ParseSeconds(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Number && node.TryGetDouble(out var number))
        {
            return number;
        }

        return node.ValueKind == JsonValueKind.String
               && double.TryParse(node.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

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

    [GeneratedRegex(@"\{[^{}]*'MUSICRID'\s*:\s*'MUSIC_\d+'[^{}]*\}")]
    private static partial Regex ItemRegex();
}
