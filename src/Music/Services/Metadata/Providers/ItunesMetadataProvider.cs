using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata.Providers;

/// <summary>iTunes / Apple Music：仅提供搜索与封面（无歌词）。</summary>
public sealed class ItunesMetadataProvider : IMetadataProvider
{
    private readonly MetadataHttpClient _http;

    public ItunesMetadataProvider(MetadataHttpClient http) => _http = http;

    public string Id => "itunes";

    public string DisplayName => "iTunes / Apple Music";

    public bool SupportsLyrics => false;

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        TrackQuery query,
        CancellationToken cancellationToken = default)
    {
        var keyword = Uri.EscapeDataString($"{query.Title} {query.Artist}".Trim());
        var url = $"https://itunes.apple.com/search?term={keyword}&entity=song&limit=10&country=cn";

        var json = await _http.GetStringAsync(url, cancellationToken, referer: "https://music.apple.com/")
            .ConfigureAwait(false);
        var results = new List<MetadataCandidate>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in items.EnumerateArray())
            {
                var id = item.TryGetProperty("trackId", out var trackId)
                         && trackId.ValueKind == JsonValueKind.Number
                    ? trackId.GetInt64().ToString()
                    : string.Empty;
                var name = GetString(item, "trackName");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var artwork = GetString(item, "artworkUrl100");
                var cover = string.IsNullOrEmpty(artwork)
                    ? null
                    : artwork.Replace("100x100bb", "600x600bb");

                results.Add(new MetadataCandidate(
                    Id,
                    id,
                    name,
                    GetString(item, "artistName"),
                    GetString(item, "collectionName"),
                    cover,
                    HasLyrics: false));
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
            : _http.GetBytesAsync(candidate.CoverUrl, cancellationToken, referer: "https://music.apple.com/");

    /// <summary>iTunes 不提供歌词。</summary>
    public Task<string?> GetLyricsAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
