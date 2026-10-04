using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata.Providers;

/// <summary>咪咕音乐：搜索 / 封面 / 歌词（移动端私有接口，尽力而为）。</summary>
public sealed class MiguMetadataProvider : IMetadataProvider
{
    private const string Referer = "https://m.music.migu.cn/";

    private readonly MetadataHttpClient _http;

    public MiguMetadataProvider(MetadataHttpClient http) => _http = http;

    public string Id => "migu";

    public string DisplayName => "咪咕音乐";

    public bool SupportsLyrics => true;

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        TrackQuery query,
        CancellationToken cancellationToken = default)
    {
        var keyword = Uri.EscapeDataString($"{query.Title} {query.Artist}".Trim());
        var url = $"https://m.music.migu.cn/migu/remoting/scr_search_tag?keyword={keyword}&type=2&rows=10&pgc=1";

        var json = await _http.GetStringAsync(url, cancellationToken, Referer).ConfigureAwait(false);
        var results = new List<MetadataCandidate>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("musics", out var musics)
                || musics.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in musics.EnumerateArray())
            {
                var id = GetString(item, "id");
                var name = GetString(item, "songName");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var cover = GetString(item, "cover");
                results.Add(new MetadataCandidate(
                    Id,
                    id,
                    name,
                    GetString(item, "singerName"),
                    GetString(item, "albumName"),
                    string.IsNullOrEmpty(cover) ? null : cover,
                    HasLyrics: true,
                    Year: GetYear(item)));
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
        var url = $"https://music.migu.cn/v3/api/music/audioPlayer/getLyric?copyrightId={candidate.RemoteId}";
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

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>咪咕搜索结果的发行时间字段名不固定，逐个尝试；都没有则返回 0。</summary>
    private static int GetYear(JsonElement item)
    {
        var year = MetadataYear.FromElement(item, "releaseDate");
        return year > 0 ? year : MetadataYear.FromElement(item, "publishDate");
    }
}
