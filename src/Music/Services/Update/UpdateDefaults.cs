using System;
using System.Text.Json;

namespace Music.Services.Update;

/// <summary>
/// 内置的在线升级配置，从编译进程序集的 <c>update.config.json</c> 读取。
/// 地址与只读令牌都由发布方在构建前写好，终端用户无需在设置里配置。
/// </summary>
public static class UpdateDefaults
{
    private const string ResourceName = "Music.update.config.json";

    /// <summary>版本清单地址（返回 <see cref="Music.Models.UpdateManifest"/> 的 JSON）。</summary>
    public static string FeedUrl { get; }

    /// <summary>私有仓库用户名，与 <see cref="Token"/> 一起做 HTTP Basic 鉴权；为空则用 token 方案。</summary>
    public static string User { get; }

    /// <summary>私有仓库只读令牌。为空则匿名访问（清单/Release 公开时无需令牌）。</summary>
    public static string Token { get; }

    static UpdateDefaults()
    {
        FeedUrl = string.Empty;
        User = string.Empty;
        Token = string.Empty;

        try
        {
            using var stream = typeof(UpdateDefaults).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return;
            }

            // 大小写不敏感由 UpdateJsonContext 的源生成选项表达。
            var config = JsonSerializer.Deserialize(stream, UpdateJsonContext.Default.UpdateConfig);
            if (config is null)
            {
                return;
            }

            FeedUrl = config.FeedUrl?.Trim() ?? string.Empty;
            User = config.User?.Trim() ?? string.Empty;
            Token = config.Token?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            // 配置缺失或损坏时保持空配置：不检查更新，但不影响应用启动。
        }
    }
}

/// <summary><c>update.config.json</c> 的结构；提升为 internal 以便源生成器访问。</summary>
internal sealed class UpdateConfig
{
    public string? FeedUrl { get; set; }

    public string? User { get; set; }

    public string? Token { get; set; }
}
