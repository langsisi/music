using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata.Providers;

/// <summary>酷狗音乐：搜索 / 封面 / 歌词（移动端私有接口，尽力而为）。</summary>
public sealed class KugouMetadataProvider : IMetadataProvider
{
    private const string Referer = "https://www.kugou.com/";

    private readonly MetadataHttpClient _http;

    public KugouMetadataProvider(MetadataHttpClient http) => _http = http;

    public string Id => "kugou";

    public string DisplayName => "酷狗音乐";

    public bool SupportsLyrics => true;

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        TrackQuery query,
        CancellationToken cancellationToken = default)
    {
        // mobilecdn 主机在本网络下 TLS 握手失败，改用 mobiles 主机（同一接口）。
        var keyword = Uri.EscapeDataString($"{query.Title} {query.Artist}".Trim());
        var url =
            $"https://mobiles.kugou.com/api/v3/search/song?format=json&keyword={keyword}&page=1&pagesize=10&showtype=1";

        var json = await _http.GetStringAsync(url, cancellationToken, Referer).ConfigureAwait(false);
        var results = new List<MetadataCandidate>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("info", out var info)
                || info.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in info.EnumerateArray())
            {
                var hash = GetString(item, "hash");
                var name = GetString(item, "songname");
                if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // mobiles 接口不再返回 albumname/publish_time，专辑字段为 album_name；年份缺失按 0 处理。
                var album = GetString(item, "album_name");
                if (string.IsNullOrEmpty(album))
                {
                    album = GetString(item, "albumname");
                }

                results.Add(new MetadataCandidate(
                    Id,
                    hash,
                    name,
                    GetString(item, "singername"),
                    album,
                    CoverUrl: $"https://imge.kugou.com/stdmusic/480/{hash}.jpg",
                    HasLyrics: true,
                    Year: 0));
            }
        }
        catch (JsonException)
        {
            // 忽略损坏响应。
        }

        return results;
    }

    public Task<byte[]?> GetCoverAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(candidate.CoverUrl)
            ? Task.FromResult<byte[]?>(null)
            : _http.GetBytesAsync(candidate.CoverUrl, cancellationToken, Referer);

    public async Task<string?> GetLyricsAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
    {
        // 先按 hash 找歌词候选，再下载 LRC（返回体为 base64 编码的 LRC 文本）。
        var searchUrl =
            $"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&hash={candidate.RemoteId}";
        var searchJson = await _http.GetStringAsync(searchUrl, cancellationToken, Referer).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(searchJson))
        {
            return null;
        }

        string? lyricId = null;
        string? accessKey = null;

        try
        {
            using var document = JsonDocument.Parse(searchJson);
            if (document.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in candidates.EnumerateArray())
                {
                    var id = GetString(item, "id");
                    var key = GetString(item, "accesskey");
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(key))
                    {
                        lyricId = id;
                        accessKey = key;
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        if (lyricId is null || accessKey is null)
        {
            return null;
        }

        var downloadUrl =
            $"https://lyrics.kugou.com/download?ver=1&client=pc&id={lyricId}&accesskey={accessKey}&fmt=lrc&charset=utf8";
        var downloadJson = await _http.GetStringAsync(downloadUrl, cancellationToken, Referer).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(downloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(downloadJson);
            var content = GetString(document.RootElement, "content");
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            var bytes = Convert.FromBase64String(content);
            var text = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
