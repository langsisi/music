using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Music.Models;

namespace Music.Services.Remote;

/// <summary>
/// 基于 <see cref="HttpClient"/> 的 WebDAV 客户端（PROPFIND 列目录 / GET 下载 / PUT 上传 / MKCOL 建目录）。
/// 只支持 Basic 认证；每请求单独附带认证头，避免污染共享的 HttpClient。
/// 注意：目录列举只用 <c>Depth: 1</c> 逐层展开，因为 <c>Depth: infinity</c> 常被服务端禁用。
/// </summary>
public sealed class WebDavRemoteFileClient : IRemoteFileClient
{
    private const string PropFindBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:">
          <d:prop>
            <d:resourcetype/>
            <d:getcontentlength/>
          </d:prop>
        </d:propfind>
        """;

    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly HttpMethod MkColMethod = new("MKCOL");

    private readonly WebDavSourceConfig _config;
    private readonly HttpClient _httpClient;
    private readonly Uri _rootUri;

    public WebDavRemoteFileClient(WebDavSourceConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
        _rootUri = BuildRootUri(config);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        // 探活打在实际要用的目录（BaseUrl + 起始目录）上，起始目录填错时能立即给出明确失败，
        // 而不是等到同步阶段才发现。
        var probe = BuildCollectionUri(_config.RootPath ?? string.Empty);

        using var response = await SendAsync(PropFindMethod, probe, depth: "0", content: null, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new IOException(
                $"WebDAV 连接失败：{(int)response.StatusCode} {response.ReasonPhrase}（路径 {probe.AbsolutePath}）");
        }
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        var startUri = BuildCollectionUri(rootPath);
        var entries = new List<RemoteEntry>();
        var pending = new Stack<Uri>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(startUri);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryUri = pending.Pop();
            var directoryKey = NormalizeUri(directoryUri);

            // 防止服务端 href 形式不一时反复展开同一个目录。
            if (!visited.Add(directoryKey))
            {
                continue;
            }

            foreach (var item in await PropFindDepthOneAsync(directoryUri, cancellationToken).ConfigureAwait(false))
            {
                var itemKey = NormalizeUri(item.Uri);

                // PROPFIND Depth:1 会把目录自身也回带，跳过。
                if (string.Equals(itemKey, directoryKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(new RemoteEntry(ToRelativePath(item.Uri), item.Size, item.IsDirectory));

                if (item.IsDirectory && !visited.Contains(itemKey))
                {
                    pending.Push(item.Uri);
                }
            }

            progress?.Report(new ScanProgress("正在列出 WebDAV 目录", entries.Count, entries.Count));
        }

        return entries;
    }

    public async Task DownloadAsync(
        string remotePath,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var uri = BuildFileUri(remotePath);
        using var response = await SendAsync(HttpMethod.Get, uri, depth: null, content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[81920];
        long received = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            if (total > 0)
            {
                progress?.Report((double)received / total);
            }
        }

        progress?.Report(1);
    }

    public async Task UploadAsync(
        string remotePath,
        Stream content,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureCollectionsAsync(remotePath, cancellationToken).ConfigureAwait(false);

        var uri = BuildFileUri(remotePath);
        using var request = NewRequest(HttpMethod.Put, uri);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new IOException($"上传失败：{(int)response.StatusCode} {response.ReasonPhrase}");
        }

        progress?.Report(1);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<List<(Uri Uri, bool IsDirectory, long Size)>> PropFindDepthOneAsync(
        Uri directoryUri,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(PropFindMethod, directoryUri, depth: "1", content: PropFindBody, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.MultiStatus)
        {
            throw new IOException($"PROPFIND 失败：{(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var document = XDocument.Load(stream, LoadOptions.None);
        var results = new List<(Uri, bool, long)>();

        foreach (var element in document.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "response", StringComparison.Ordinal))
            {
                continue;
            }

            var hrefElement = FindChild(element, "href");
            if (hrefElement is null || string.IsNullOrWhiteSpace(hrefElement.Value))
            {
                continue;
            }

            // href 可能是绝对 URL，也可能是绝对路径，用当前目录作为基址解析。
            if (!Uri.TryCreate(directoryUri, hrefElement.Value.Trim(), out var itemUri))
            {
                continue;
            }

            var isDirectory = element.Descendants()
                .Any(node => string.Equals(node.Name.LocalName, "collection", StringComparison.Ordinal));

            var sizeText = FindChild(element, "getcontentlength")?.Value;
            _ = long.TryParse(sizeText, out var size);

            results.Add((itemUri, isDirectory, size));
        }

        return results;
    }

    /// <summary>逐级 MKCOL 创建目录；已存在（405）与父级缺失（409）都视为可继续。</summary>
    private async Task EnsureCollectionsAsync(string remotePath, CancellationToken cancellationToken)
    {
        var segments = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
        {
            return;
        }

        var current = _rootUri;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = new Uri(current, Uri.EscapeDataString(segments[i]) + "/");

            using var response = await SendAsync(MkColMethod, current, depth: null, content: null, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.MethodNotAllowed
                or HttpStatusCode.Conflict)
            {
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException($"创建目录失败：{(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        string? depth,
        string? content,
        CancellationToken cancellationToken)
    {
        var request = NewRequest(method, uri);

        if (depth is not null)
        {
            request.Headers.Add("Depth", depth);
        }

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, "application/xml");
        }

        return SendAndDisposeRequestAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAndDisposeRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            // ResponseHeadersRead 让调用方可以流式读取响应体。
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);

        var token = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_config.UserName}:{_config.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        return request;
    }

    private static XElement? FindChild(XElement parent, string localName)
        => parent.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    /// <summary>把 PROPFIND 返回的 href 转成相对根目录、'/' 分隔的路径。</summary>
    private string ToRelativePath(Uri uri)
    {
        var relative = _rootUri.MakeRelativeUri(uri).ToString();
        return Uri.UnescapeDataString(relative).TrimEnd('/');
    }

    private Uri BuildCollectionUri(string rootPath)
    {
        var normalized = NormalizeRelativePath(rootPath);
        return normalized.Length == 0
            ? _rootUri
            : new Uri(_rootUri, normalized + "/");
    }

    private Uri BuildFileUri(string remotePath)
    {
        var normalized = NormalizeRelativePath(remotePath);
        var escaped = string.Join('/', normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        return new Uri(_rootUri, escaped);
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/').Trim('/');

    /// <summary>URI 归一化（去掉 query/fragment 与结尾 '/'），用于判定是否为同一目录。</summary>
    private static string NormalizeUri(Uri uri) => uri.GetLeftPart(UriPartial.Path).TrimEnd('/');

    /// <summary>
    /// WebDAV 服务端根（仅 BaseUrl）。起始目录（<c>RootPath</c>）不并入这里——
    /// 它由 <see cref="ListAsync"/> 的 rootPath 参数应用，与 FTP/SMB 的约定一致；
    /// 若在此并入，起始目录会被应用两次导致 404。
    /// </summary>
    private static Uri BuildRootUri(WebDavSourceConfig config)
    {
        var baseUrl = (config.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"WebDAV 地址无效：{config.BaseUrl}");
        }

        return new Uri(baseUrl + "/");
    }
}
