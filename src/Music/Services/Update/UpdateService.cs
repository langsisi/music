using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Update;

/// <summary>一次版本检查的结果。</summary>
public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string? LatestVersion,
    UpdateManifest? Manifest,
    string Message);

/// <summary>
/// 在线升级：从内置配置的地址拉取版本清单（私有仓库可带只读令牌鉴权），比较版本号，
/// 下载并校验新版本包，最后交给平台安装器（<see cref="IUpdateInstaller"/>）落地安装。
/// </summary>
public sealed class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IUpdateInstaller _installer;

    /// <summary>
    /// 部分静态托管（例如 GitCode 的 raw 网关 raw.gitcode.com）会拒绝没有浏览器特征的请求，
    /// 直接返回 403 Forbidden。这里带上常见的 UA 与 Accept，保证升级检查能正常拿到清单。
    /// </summary>
    private const string RequestUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", RequestUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        ApplyAuthorization(request);
        return request;
    }

    /// <summary>
    /// 私有仓库鉴权：内置配置填了用户名就用 HTTP Basic（用户名 + 令牌），只填令牌则用 Gitea 的 token 方案。
    /// </summary>
    private static void ApplyAuthorization(HttpRequestMessage request)
    {
        var token = UpdateDefaults.Token;
        if (token.Length == 0)
        {
            return;
        }

        var user = UpdateDefaults.User;
        if (user.Length > 0)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{token}"));
            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {credentials}");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"token {token}");
        }
    }

    public UpdateService(HttpClient httpClient, IUpdateInstaller installer)
    {
        _httpClient = httpClient;
        _installer = installer;
    }

    /// <summary>当前版本（取自入口程序集）。</summary>
    public static Version CurrentVersion { get; } = ResolveCurrentVersion();

    public static string CurrentVersionText =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>由平台安装器决定能否应用内自更新（Windows / Android 支持）。</summary>
    public bool CanSelfUpdate => _installer.CanSelfUpdate;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var feedUrl = UpdateDefaults.FeedUrl;
        if (feedUrl.Length == 0)
        {
            return new UpdateCheckResult(false, CurrentVersionText, null, null, "尚未内置更新地址。");
        }

        UpdateManifest? manifest;
        try
        {
            using var response = await _httpClient
                .SendAsync(CreateRequest(feedUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            // 清单里的键名通常是 camelCase，大小写不敏感由 UpdateJsonContext 的源生成选项表达。
            manifest = await JsonSerializer
                .DeserializeAsync(stream, UpdateJsonContext.Default.UpdateManifest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult(false, CurrentVersionText, null, null, $"无法获取版本信息：{ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(false, CurrentVersionText, null, null, "获取版本信息超时。");
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(false, CurrentVersionText, null, null, "版本信息格式不正确。");
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
        {
            return new UpdateCheckResult(false, CurrentVersionText, null, null, "版本信息缺少版本号。");
        }

        if (!Version.TryParse(manifest.Version, out var latest))
        {
            return new UpdateCheckResult(
                false,
                CurrentVersionText,
                manifest.Version,
                null,
                $"无法识别的版本号：{manifest.Version}");
        }

        if (latest <= CurrentVersion)
        {
            return new UpdateCheckResult(
                false,
                CurrentVersionText,
                manifest.Version,
                null,
                $"已是最新版本（{CurrentVersionText}）。");
        }

        // Android 与桌面用不同的包（apk / zip），先按平台把清单里的地址归一化。
        ApplyPlatformPackage(manifest);

        if (string.IsNullOrWhiteSpace(manifest.Url))
        {
            return new UpdateCheckResult(false, CurrentVersionText, manifest.Version, null, "版本信息里没有下载地址。");
        }

        // 允许清单里写相对路径（以清单地址为基准），这样把 zip 和清单丢在同一个静态目录即可。
        if (!TryResolvePackageUrl(feedUrl, manifest.Url.Trim(), out var packageUrl))
        {
            return new UpdateCheckResult(
                false,
                CurrentVersionText,
                manifest.Version,
                null,
                $"下载地址格式不正确：{manifest.Url}");
        }

        manifest.Url = packageUrl;

        return new UpdateCheckResult(
            true,
            CurrentVersionText,
            manifest.Version,
            manifest,
            $"发现新版本 {manifest.Version}。");
    }

    /// <summary>
    /// 按当前平台归一化包地址：Android 优先用清单里的 androidUrl/androidSha256，
    /// 没有则回退到通用 url/sha256；桌面端始终用通用字段。
    /// </summary>
    private static void ApplyPlatformPackage(UpdateManifest manifest)
    {
        if (OperatingSystem.IsAndroid() && !string.IsNullOrWhiteSpace(manifest.AndroidUrl))
        {
            manifest.Url = manifest.AndroidUrl!;
            manifest.Sha256 = manifest.AndroidSha256 ?? manifest.Sha256;
        }
    }

    /// <summary>
    /// 把清单里的下载地址解析成绝对地址：既支持完整的 URL，也支持相对清单所在目录的路径。
    /// </summary>
    private static bool TryResolvePackageUrl(string feedUrl, string packageUrl, out string resolved)
    {
        resolved = string.Empty;

        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var absolute))
        {
            resolved = absolute.ToString();
            return true;
        }

        if (Uri.TryCreate(feedUrl, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, packageUrl, out var relative))
        {
            resolved = relative.ToString();
            return true;
        }

        return false;
    }

    /// <summary>下载新版本包到更新目录，返回本地路径。清单里给了 sha256 时会校验。</summary>
    public async Task<string> DownloadAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.Url))
        {
            throw new InvalidOperationException("版本信息里没有下载地址。");
        }

        var folder = Path.Combine(AppPaths.Root, "updates");
        Directory.CreateDirectory(folder);

        var safeVersion = string.Concat(manifest.Version.Where(char.IsLetterOrDigit));
        var target = Path.Combine(folder, $"Music-{safeVersion}{ResolvePackageExtension(manifest.Url)}");
        var temp = target + ".tmp";

        using (var response = await _httpClient
            .SendAsync(CreateRequest(manifest.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;

            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false))
            await using (var destination = File.Create(temp))
            {
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
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            var actual = await ComputeSha256Async(temp, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new InvalidOperationException("下载的安装包校验失败，已丢弃。");
            }
        }

        File.Move(temp, target, overwrite: true);
        progress?.Report(1);
        return target;
    }

    /// <summary>从下载地址里取扩展名（.zip / .apk），取不到时按 .zip 处理。</summary>
    private static string ResolvePackageExtension(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension;
            }
        }

        return ".zip";
    }

    /// <summary>
    /// 把已下载的升级包交给平台安装器：
    /// Windows 解压覆盖安装目录并重启；Android 拉起系统安装器让用户确认。
    /// </summary>
    public void Install(string packagePath) => _installer.Install(packagePath);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Version ResolveCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;

        // InformationalVersion 可能带 "+commit" 后缀，取前面的版本号部分。
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var core = informational.Split('+')[0];
            if (Version.TryParse(core, out var parsed))
            {
                return parsed;
            }
        }

        return assembly.GetName().Version ?? new Version(1, 0, 0);
    }
}
