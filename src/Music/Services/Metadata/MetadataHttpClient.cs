using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata;

/// <summary>
/// 刮削专用 HTTP 封装：在共享 <see cref="HttpClient"/> 上按请求注入各数据源
/// 所需的 UA / Referer / Cookie（<b>不</b>改全局默认头，Navidrome 复用同一个实例）。
/// 网络错误或非 2xx 一律静默返回 null；仅当调用方取消时抛出。
/// </summary>
public sealed class MetadataHttpClient
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0 Safari/537.36";

    private readonly HttpClient _http;

    public MetadataHttpClient(HttpClient http) => _http = http;

    public async Task<string?> GetStringAsync(
        string url,
        CancellationToken cancellationToken,
        string? referer = null,
        string? cookie = null)
    {
        try
        {
            using var request = CreateRequest(url, referer, cookie);
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

    public async Task<byte[]?> GetBytesAsync(
        string url,
        CancellationToken cancellationToken,
        string? referer = null,
        string? cookie = null)
    {
        try
        {
            using var request = CreateRequest(url, referer, cookie);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
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

    private static HttpRequestMessage CreateRequest(string url, string? referer, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        if (!string.IsNullOrEmpty(referer))
        {
            request.Headers.TryAddWithoutValidation("Referer", referer);
        }

        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return request;
    }
}
