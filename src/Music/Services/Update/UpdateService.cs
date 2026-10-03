using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
/// 在线升级：从设置的地址拉取版本清单，比较版本号，下载并校验新版本包，
/// 最后交给一个独立进程在应用退出后覆盖安装目录并重启。
/// </summary>
public sealed class UpdateService
{
    private readonly ISettingsStore _settings;
    private readonly HttpClient _httpClient;

    /// <summary>清单里的键名通常是 camelCase，这里不区分大小写。</summary>
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public UpdateService(ISettingsStore settings, HttpClient httpClient)
    {
        _settings = settings;
        _httpClient = httpClient;
    }

    /// <summary>当前版本（取自入口程序集）。</summary>
    public static Version CurrentVersion { get; } = ResolveCurrentVersion();

    public static string CurrentVersionText =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>Android 等平台无法自行覆盖安装，只能走应用商店。</summary>
    public static bool CanSelfUpdate => OperatingSystem.IsWindows();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var feedUrl = _settings.Current.UpdateFeedUrl?.Trim() ?? string.Empty;
        if (feedUrl.Length == 0)
        {
            return new UpdateCheckResult(false, CurrentVersionText, null, null, "尚未配置更新地址。");
        }

        UpdateManifest? manifest;
        try
        {
            await using var stream = await _httpClient
                .GetStreamAsync(feedUrl, cancellationToken)
                .ConfigureAwait(false);

            manifest = await JsonSerializer
                .DeserializeAsync<UpdateManifest>(stream, ManifestOptions, cancellationToken)
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
        var target = Path.Combine(folder, $"Music-{safeVersion}.zip");
        var temp = target + ".tmp";

        using (var response = await _httpClient
            .GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

    /// <summary>
    /// 启动一个独立进程：等当前应用退出 → 解压覆盖安装目录 → 重新启动，然后关闭本应用。
    /// </summary>
    public void ApplyAndRestart(string packagePath)
    {
        if (!CanSelfUpdate)
        {
            throw new NotSupportedException("当前平台不支持自动更新。");
        }

        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var executable = Environment.ProcessPath
            ?? Path.Combine(installDirectory, "Music.Desktop.exe");

        // 必须是独立进程：本进程退出后才能覆盖自己正在使用的文件。
        var command = string.Join(
            "; ",
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue",
            "Start-Sleep -Milliseconds 800",
            $"Expand-Archive -LiteralPath '{packagePath}' -DestinationPath '{installDirectory}' -Force",
            $"Start-Process -FilePath '{executable}'");

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        Process.Start(startInfo);

        // 退出当前实例，把文件锁让给升级脚本。
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

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
