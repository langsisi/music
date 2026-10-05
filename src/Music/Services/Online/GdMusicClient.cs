using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Online;

/// <summary>GD音乐台接口调用失败（网络、解析或服务端返回错误）。</summary>
public sealed class GdMusicException : Exception
{
    public GdMusicException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// GD音乐台（music.gdstudio.xyz）免费接口客户端。
/// 接口形如 <c>api.php?types=search&amp;source=netease&amp;name=...</c>：
/// <list type="bullet">
/// <item><c>types=search</c> → 歌曲数组，字段含 id / name / artist / album / pic_id / url_id / lyric_id；</item>
/// <item><c>types=url</c> → 播放地址与大小；</item>
/// <item><c>types=pic</c> → 封面地址；</item>
/// <item><c>types=lyric</c> → 歌词文本。</item>
/// </list>
/// 解析用 <see cref="JsonDocument"/> 手工取值，避免反射序列化，保证 AOT / 裁剪安全。
/// </summary>
public sealed class GdMusicClient
{
    private const string BaseUrl = "https://music-api.gdstudio.xyz/api.php";

    /// <summary>该接口在 Cloudflare 之后，缺少 UA 的请求容易被拦截，这里统一带上。</summary>
    private const string UserAgent = "Music/1.0 (Avalonia)";

    private readonly HttpClient _httpClient;

    public GdMusicClient(HttpClient httpClient) => _httpClient = httpClient;

    /// <summary>按关键词搜索歌曲。</summary>
    public async Task<IReadOnlyList<OnlineTrack>> SearchAsync(
        string source,
        string keyword,
        int count = 30,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(
            "search",
            source,
            $"&name={Uri.EscapeDataString(keyword)}&count={count}&pages=1");

        using var document = await GetDocumentAsync(url, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new GdMusicException(DescribeError(document.RootElement));
        }

        var results = new List<OnlineTrack>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = GetString(item, "id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            results.Add(new OnlineTrack
            {
                Source = GetString(item, "source") ?? source,
                Id = id,
                Name = GetString(item, "name") ?? string.Empty,
                Artist = string.Join(" / ", ReadArtists(item)),
                Album = GetString(item, "album") ?? string.Empty,
                PicId = GetString(item, "pic_id") ?? string.Empty,
                UrlId = GetString(item, "url_id") ?? id,
                LyricId = GetString(item, "lyric_id") ?? id,
            });
        }

        return results;
    }

    /// <summary>取可直接播放的流地址；无版权或需要会员时返回 null。</summary>
    public async Task<string?> GetStreamUrlAsync(
        string source,
        string id,
        int bitrate,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl("url", source, $"&id={Uri.EscapeDataString(id)}&br={bitrate}");

        try
        {
            using var document = await GetDocumentAsync(url, cancellationToken).ConfigureAwait(false);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? EmptyToNull(GetString(document.RootElement, "url"))
                : null;
        }
        catch (GdMusicException)
        {
            return null;
        }
    }

    /// <summary>取封面图片字节；取不到时返回 null。</summary>
    public async Task<byte[]?> GetCoverBytesAsync(
        string source,
        string picId,
        int size = 300,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(picId))
        {
            return null;
        }

        // B站结果的 pic_id 本身就是协议相对的图片地址（//i0.hdslb.com/...），
        // 直接下载可少一次 API 调用（官方限频：5 分钟内不超 50 次请求）。
        if (picId.StartsWith("//", StringComparison.Ordinal))
        {
            try
            {
                return await GetBytesAsync($"https:{picId}", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 封面只是锦上添花。
                return null;
            }
        }

        var metaUrl = BuildUrl("pic", source, $"&id={Uri.EscapeDataString(picId)}&size={size}");

        try
        {
            using var document = await GetDocumentAsync(metaUrl, cancellationToken).ConfigureAwait(false);
            var coverUrl = document.RootElement.ValueKind == JsonValueKind.Object
                ? EmptyToNull(GetString(document.RootElement, "url"))
                : null;

            if (coverUrl is null)
            {
                return null;
            }

            return await GetBytesAsync(coverUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 封面只是锦上添花。
            return null;
        }
    }

    /// <summary>取歌词文本（LRC）；无歌词时返回 null。</summary>
    public async Task<string?> GetLyricAsync(
        string source,
        string lyricId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lyricId))
        {
            return null;
        }

        var url = BuildUrl("lyric", source, $"&id={Uri.EscapeDataString(lyricId)}");

        try
        {
            using var document = await GetDocumentAsync(url, cancellationToken).ConfigureAwait(false);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? EmptyToNull(GetString(document.RootElement, "lyric"))
                : null;
        }
        catch (GdMusicException)
        {
            return null;
        }
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetDocumentAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            return await JsonDocument
                .ParseAsync(stream, default, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GdMusicException($"无法连接 GD 音乐台：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GdMusicException("连接 GD 音乐台超时。", ex);
        }
        catch (JsonException ex)
        {
            throw new GdMusicException("GD 音乐台返回的内容无法解析。", ex);
        }
    }

    private static string BuildUrl(string types, string source, string extra)
        => $"{BaseUrl}?types={types}&source={Uri.EscapeDataString(source)}{extra}";

    private static IReadOnlyList<string> ReadArtists(JsonElement item)
    {
        if (!item.TryGetProperty("artist", out var artists) || artists.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var artist in artists.EnumerateArray())
        {
            if (artist.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(artist.GetString()))
            {
                names.Add(artist.GetString()!);
            }
        }

        return names;
    }

    private static string DescribeError(JsonElement root)
    {
        var detail = root.ValueKind == JsonValueKind.Object ? GetString(root, "detail") : null;
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "GD 音乐台返回了非预期的响应。";
        }

        // 服务端对未开放的音源统一返回「Value of `source` is not supported」。
        return detail.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            ? "GD 音乐台暂未开放该音源，请换其他音源试试。"
            : $"GD 音乐台返回错误：{detail}";
    }

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}