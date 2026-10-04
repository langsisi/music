using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata.Providers;

/// <summary>网易云音乐：搜索 / 封面 / 歌词。</summary>
public sealed class NeteaseMetadataProvider : IMetadataProvider
{
    private const string Referer = "https://music.163.com";

    private readonly MetadataHttpClient _http;

    public NeteaseMetadataProvider(MetadataHttpClient http) => _http = http;

    public string Id => "netease";

    public string DisplayName => "网易云音乐";

    public bool SupportsLyrics => true;

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        TrackQuery query,
        CancellationToken cancellationToken = default)
    {
        var keyword = Uri.EscapeDataString($"{query.Title} {query.Artist}".Trim());
        var url = $"https://music.163.com/api/search/get/web?s={keyword}&type=1&limit=10";

        var json = await _http.GetStringAsync(url, cancellationToken, Referer).ConfigureAwait(false);
        var results = new List<MetadataCandidate>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("songs", out var songs)
                || songs.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var song in songs.EnumerateArray())
            {
                var id = GetString(song, "id");
                var name = GetString(song, "name");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var artist = FirstArtist(song);
                string? cover = null;
                var album = string.Empty;
                var year = 0;

                if (song.TryGetProperty("album", out var albumNode) && albumNode.ValueKind == JsonValueKind.Object)
                {
                    cover = GetString(albumNode, "picUrl");
                    album = GetString(albumNode, "name");
                    year = MetadataYear.FromElement(albumNode, "publishTime");
                }

                results.Add(new MetadataCandidate(Id, id, name, artist, album, cover, HasLyrics: true, Year: year));
            }
        }
        catch (JsonException)
        {
            // 返回体异常时按「无结果」处理。
        }

        return results;
    }

    public Task<byte[]?> GetCoverAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(candidate.CoverUrl)
            ? Task.FromResult<byte[]?>(null)
            : _http.GetBytesAsync(candidate.CoverUrl, cancellationToken, Referer);

    public async Task<string?> GetLyricsAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
    {
        var url = $"https://music.163.com/api/song/lyric?id={candidate.RemoteId}&lv=-1&kv=-1&tv=-1";
        var json = await _http.GetStringAsync(url, cancellationToken, Referer).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("lrc", out var lrc))
            {
                var text = GetString(lrc, "lyric");
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch (JsonException)
        {
            // 忽略损坏响应。
        }

        return null;
    }

    private static string FirstArtist(JsonElement song)
    {
        if (song.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
        {
            foreach (var artist in artists.EnumerateArray())
            {
                var name = GetString(artist, "name");
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
