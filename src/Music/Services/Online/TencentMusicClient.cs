using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Security;

namespace Music.Services.Online;

/// <summary>
/// QQ 音乐直连客户端：搜索 / 取流 / 封面 / 歌词，不经过 GD 音乐台。
/// <para>
/// 取流走官方 vkey 两段式：
/// <list type="number">
/// <item><c>fcg_play_single_song.fcg</c> 取 <c>media_mid</c>（构文件名需要）；</item>
/// <item><c>musicu.fcg</c>（GET + <c>data=</c>，无需 sign）按音质批量取 <c>purl</c>，播放地址 = <c>sip[0] + purl</c>。</item>
/// </list>
/// 未登录时 QQ 只放行 128k（更高音质与会员曲返回 <c>result=104003</c>），
/// 因此这里按「请求音质 → 逐级降到 128k」的顺序择优，并支持用配置的 Cookie 解锁高音质 / 会员曲。
/// 解析用 <see cref="JsonDocument"/> 手工取值，保证 AOT / 裁剪安全。
/// </para>
/// </summary>
public sealed class TencentMusicClient
{
    private const string MusicuUrl = "https://u.y.qq.com/cgi-bin/musicu.fcg";
    private const string SearchUrl = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp";
    private const string SongDetailUrl = "https://c.y.qq.com/v8/fcg-bin/fcg_play_single_song.fcg";
    private const string LyricUrl = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg";
    private const string CoverTemplate = "https://y.gtimg.cn/music/photo_new/T002R{0}x{0}M000{1}.jpg";
    private const string Referer = "https://y.qq.com/";

    /// <summary>QQ 接口按手机版客户端校验，UA 必须是这个（百分号形式，原样带上去）。</summary>
    private const string UserAgent = "QQ%E9%9F%B3%E4%B9%90/54409 CFNetwork/901.1 Darwin/17.6.0 (x86_64)";

    /// <summary>取流缺省落点，正常响应里由 <c>sip[0]</c> 覆盖。</summary>
    private const string FallbackSip = "https://aqqmusic.tc.qq.com/";

    /// <summary>按音质从高到低的文件名前缀，QQ 用它区分音质。</summary>
    private static readonly Quality[] Qualities =
    [
        new(999, "F000", ".flac"),
        new(320, "M800", ".mp3"),
        new(192, "C600", ".m4a"),
        new(128, "M500", ".mp3"),
    ];

    private readonly HttpClient _http;
    private readonly ISettingsStore _settings;
    private readonly ISecretProtector _protector;

    public TencentMusicClient(ISettingsStore settings, ISecretProtector protector)
    {
        _settings = settings;
        _protector = protector;

        // 与 MetadataHttpClient 同理：这些是国内接口，走系统代理出境容易被风控。
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>按关键词搜索歌曲。</summary>
    public async Task<IReadOnlyList<OnlineTrack>> SearchAsync(
        string keyword,
        int count,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{SearchUrl}?p=1&n={count}&w={Uri.EscapeDataString(keyword)}&format=json&cr=1&new_json=1";
        var json = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);

        var results = new List<OnlineTrack>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryReadList(document.RootElement, out var list))
            {
                return results;
            }

            foreach (var item in list.EnumerateArray())
            {
                var mid = GetString(item, "mid");
                if (string.IsNullOrEmpty(mid))
                {
                    continue;
                }

                results.Add(new OnlineTrack
                {
                    Source = "tencent",
                    Id = mid,
                    Name = GetString(item, "name"),
                    Artist = ReadArtist(item),
                    Album = ReadAlbum(item, "name"),
                    PicId = ReadAlbum(item, "mid"),
                    UrlId = mid,
                    LyricId = mid,
                });
            }
        }
        catch (JsonException)
        {
            // 忽略损坏响应。
        }

        return results;
    }

    /// <summary>取可直接播放的流地址；无版权或需要会员时返回 null。</summary>
    public async Task<string?> GetStreamUrlAsync(
        string id,
        int bitrate,
        CancellationToken cancellationToken = default)
    {
        var cookie = ReadCookie();

        // 文件名要 media_mid；取不到时退回「songmid 重复两次」这种 QQ 也认的写法。
        var mediaMid = await GetMediaMidAsync(id, cancellationToken).ConfigureAwait(false) ?? id + id;

        var candidates = new List<Quality>();
        foreach (var quality in Qualities)
        {
            if (quality.Bitrate <= bitrate)
            {
                candidates.Add(quality);
            }
        }

        // 请求的音质低于 128k 时至少给一档。
        if (candidates.Count == 0)
        {
            candidates.Add(Qualities[^1]);
        }

        var filenames = new List<string>(candidates.Count);
        foreach (var quality in candidates)
        {
            filenames.Add($"{quality.Prefix}{mediaMid}{quality.Extension}");
        }

        var (sip, purls) = await RequestVkeysAsync(id, filenames, cookie, cancellationToken)
            .ConfigureAwait(false);

        foreach (var filename in filenames)
        {
            if (purls.TryGetValue(filename, out var purl) && !string.IsNullOrEmpty(purl))
            {
                return sip + purl;
            }
        }

        return null;
    }

    /// <summary>取封面图片字节；取不到时返回 null。</summary>
    public async Task<byte[]?> GetCoverBytesAsync(
        string picId,
        int size,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(picId))
        {
            return null;
        }

        var url = string.Format(CultureInfo.InvariantCulture, CoverTemplate, size, picId);

        try
        {
            return await GetBytesAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 封面只是锦上添花。
            return null;
        }
    }

    /// <summary>取歌词文本（LRC）；无歌词时返回 null。</summary>
    public async Task<string?> GetLyricAsync(string lyricId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lyricId))
        {
            return null;
        }

        var url = $"{LyricUrl}?songmid={Uri.EscapeDataString(lyricId)}&format=json&nobase64=1";
        var json = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var lyric = GetString(document.RootElement, "lyric");
            return string.IsNullOrWhiteSpace(lyric) ? null : lyric;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> GetMediaMidAsync(string songMid, CancellationToken cancellationToken)
    {
        var url = $"{SongDetailUrl}?songmid={Uri.EscapeDataString(songMid)}&platform=yqq&format=json";
        var json = await GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.GetArrayLength() > 0
                && data[0].TryGetProperty("file", out var file))
            {
                return EmptyToNull(GetString(file, "media_mid"));
            }
        }
        catch (JsonException)
        {
            // 忽略损坏响应。
        }

        return null;
    }

    /// <summary>批量取 vkey：返回落点域名与「文件名 → purl」的映射。</summary>
    private async Task<(string Sip, Dictionary<string, string> Purls)> RequestVkeysAsync(
        string songMid,
        IReadOnlyList<string> filenames,
        QqCookie cookie,
        CancellationToken cancellationToken)
    {
        var payload = BuildVkeyPayload(songMid, filenames, cookie);
        var url =
            $"{MusicuUrl}?format=json&platform=yqq.json&needNewCode=0&data={Uri.EscapeDataString(payload)}";

        var purls = new Dictionary<string, string>(StringComparer.Ordinal);
        var json = await GetStringAsync(url, cancellationToken, cookie.HeaderValue).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return (FallbackSip, purls);
        }

        var sip = FallbackSip;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("req_0", out var req)
                || !req.TryGetProperty("data", out var data))
            {
                return (sip, purls);
            }

            sip = ReadSip(data) ?? sip;

            if (data.TryGetProperty("midurlinfo", out var infos) && infos.ValueKind == JsonValueKind.Array)
            {
                foreach (var info in infos.EnumerateArray())
                {
                    var filename = GetString(info, "filename");
                    var purl = GetString(info, "purl");
                    if (!string.IsNullOrEmpty(filename) && !string.IsNullOrEmpty(purl))
                    {
                        purls[filename] = purl;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 忽略损坏响应。
        }

        return (sip, purls);
    }

    private static string BuildVkeyPayload(
        string songMid,
        IReadOnlyList<string> filenames,
        QqCookie cookie)
    {
        var builder = new StringBuilder();
        builder.Append("{\"req_0\":{\"module\":\"vkey.GetVkeyServer\",\"method\":\"CgiGetVkey\",\"param\":{");

        // guid 必须是 7 位以内的随机数，长了 QQ 会返回空 purl。
        var guid = Random.Shared.Next(1, 9_999_999).ToString(CultureInfo.InvariantCulture);
        builder.Append("\"guid\":\"").Append(guid).Append("\",");
        builder.Append("\"songmid\":").Append(RepeatJson(songMid, filenames.Count)).Append(',');
        builder.Append("\"filename\":").Append(StringArrayJson(filenames)).Append(',');
        builder.Append("\"songtype\":").Append(RepeatJson("0", filenames.Count, quote: false)).Append(',');
        builder.Append("\"uin\":\"").Append(JsonEscape(cookie.Uin)).Append("\",");
        builder.Append("\"loginflag\":1,\"platform\":\"20\"}");

        if (cookie.HasLogin)
        {
            builder.Append(",\"comm\":{\"uin\":\"").Append(JsonEscape(cookie.Uin))
                .Append("\",\"format\":\"json\",\"ct\":19,\"cv\":0,\"authst\":\"")
                .Append(JsonEscape(cookie.Key)).Append("\"}");
        }

        builder.Append("}}");
        return builder.ToString();
    }

    private static string? ReadSip(JsonElement data)
    {
        if (!data.TryGetProperty("sip", out var sip) || sip.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? first = null;
        foreach (var entry in sip.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = entry.GetString();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            first ??= value;

            // 尽量避开 ws 域名，取一个直连 CDN。
            if (!value.StartsWith("http://ws", StringComparison.OrdinalIgnoreCase))
            {
                return ToHttps(value);
            }
        }

        return first is null ? null : ToHttps(first);
    }

    /// <summary>读取并解密用户配置的 QQ Cookie，解析出 uin 与 qqmusic_key。</summary>
    private QqCookie ReadCookie()
    {
        var protectedText = _settings.Current.QqMusicCookie;
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return QqCookie.Empty;
        }

        var raw = _protector.Unprotect(protectedText);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return QqCookie.Empty;
        }

        string? uin = null;
        string? key = null;

        foreach (var segment in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (uin is null && (name.Equals("uin", StringComparison.OrdinalIgnoreCase)
                                || name.Equals("wxuin", StringComparison.OrdinalIgnoreCase)
                                || name.Equals("luin", StringComparison.OrdinalIgnoreCase)))
            {
                uin = value.TrimStart('o');
            }
            else if (key is null && (name.Equals("qqmusic_key", StringComparison.OrdinalIgnoreCase)
                                     || name.Equals("qm_keyst", StringComparison.OrdinalIgnoreCase)))
            {
                key = value;
            }
        }

        return new QqCookie(uin ?? "0", key ?? string.Empty, raw);
    }

    private async Task<string?> GetStringAsync(
        string url,
        CancellationToken cancellationToken,
        string? cookie = null)
    {
        try
        {
            using var request = CreateRequest(url, cookie);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<byte[]?> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(url, null);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(string url, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", Referer);

        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return request;
    }

    private static bool TryReadList(JsonElement root, out JsonElement list)
    {
        list = default;
        return root.TryGetProperty("data", out var data)
               && data.TryGetProperty("song", out var song)
               && song.TryGetProperty("list", out list)
               && list.ValueKind == JsonValueKind.Array;
    }

    /// <summary>新版搜索响应的专辑是对象，取其中 <c>mid</c> / <c>name</c> 字段。</summary>
    private static string ReadAlbum(JsonElement item, string property)
        => item.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object
            ? GetString(album, property)
            : string.Empty;

    private static string ReadArtist(JsonElement item)
    {
        if (!item.TryGetProperty("singer", out var singers) || singers.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var singer in singers.EnumerateArray())
        {
            var name = GetString(singer, "name");
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return string.Join(" / ", names);
    }

    private static string GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string RepeatJson(string value, int count, bool quote = true)
    {
        var builder = new StringBuilder("[");
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(quote ? $"\"{JsonEscape(value)}\"" : value);
        }

        return builder.Append(']').ToString();
    }

    private static string StringArrayJson(IReadOnlyList<string> values)
    {
        var builder = new StringBuilder("[");
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonEscape(values[index])).Append('"');
        }

        return builder.Append(']').ToString();
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string ToHttps(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + url[7..]
            : url;

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record Quality(int Bitrate, string Prefix, string Extension);

    /// <summary>从 Cookie 里解析出的登录信息。</summary>
    private sealed record QqCookie(string Uin, string Key, string Raw)
    {
        public static QqCookie Empty { get; } = new("0", string.Empty, string.Empty);

        public bool HasLogin => Key.Length > 0;

        /// <summary>原样透传给 HTTP 请求头的 Cookie（QQ 取流会校验 pgv_pvid 等字段）。</summary>
        public string? HeaderValue => Raw.Length > 0 ? Raw : null;
    }
}