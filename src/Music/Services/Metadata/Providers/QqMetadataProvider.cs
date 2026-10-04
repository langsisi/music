using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata.Providers;

/// <summary>QQ 音乐：搜索 / 封面 / 歌词。</summary>
public sealed class QqMetadataProvider : IMetadataProvider
{
    private const string Referer = "https://y.qq.com/";

    private readonly MetadataHttpClient _http;

    public QqMetadataProvider(MetadataHttpClient http) => _http = http;

    public string Id => "qq";

    public string DisplayName => "QQ 音乐";

    public bool SupportsLyrics => true;

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        TrackQuery query,
        CancellationToken cancellationToken = default)
    {
        var keyword = Uri.EscapeDataString($"{query.Title} {query.Artist}".Trim());
        var url = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=10&w={keyword}&format=json";

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
                || !data.TryGetProperty("song", out var song)
                || !song.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in list.EnumerateArray())
            {
                var mid = GetString(item, "songmid");
                var name = GetString(item, "songname");
                if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var albumMid = GetString(item, "albummid");
                var cover = string.IsNullOrEmpty(albumMid)
                    ? null
                    : $"https://y.gtimg.cn/music/photo_new/T002R300x300M000{albumMid}.jpg";

                results.Add(new MetadataCandidate(
                    Id,
                    mid,
                    name,
                    FirstSinger(item),
                    GetString(item, "albumname"),
                    cover,
                    HasLyrics: true));
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
        var url =
            $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={candidate.RemoteId}&format=json&nobase64=1";
        var json = await _http.GetStringAsync(url, cancellationToken, Referer).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("lyric", out var lyric)
                && lyric.ValueKind == JsonValueKind.String)
            {
                var text = lyric.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch (JsonException)
        {
            // 忽略损坏响应。
        }

        return null;
    }

    private static string FirstSinger(JsonElement item)
    {
        if (item.TryGetProperty("singer", out var singers) && singers.ValueKind == JsonValueKind.Array)
        {
            foreach (var singer in singers.EnumerateArray())
            {
                var name = GetString(singer, "name");
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
        }

        return string.Empty;
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
