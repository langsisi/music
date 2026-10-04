namespace Music.Models;

/// <summary>
/// 在线升级的版本清单，由更新地址返回的 JSON 反序列化得到。
/// 形如：<c>{ "version": "1.1.0", "url": "https://.../Music-1.1.0.zip", "sha256": "...", "notes": "..." }</c>
/// </summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 新版本包的下载地址。可以是完整 URL，也可以是相对清单所在目录的路径
    /// （例如 <c>Music-1.1.0.zip</c>），后者方便把清单和安装包放在同一个静态目录。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>包的 SHA256（十六进制，大小写不敏感）。为空则跳过校验。</summary>
    public string? Sha256 { get; set; }

    /// <summary>
    /// Android 安装包（.apk）地址。Android 端优先用它，为空则回退到 <see cref="Url"/>；
    /// 桌面端始终用 <see cref="Url"/>（zip）。
    /// </summary>
    public string? AndroidUrl { get; set; }

    /// <summary>Android 包的 SHA256，为空则回退到 <see cref="Sha256"/>。</summary>
    public string? AndroidSha256 { get; set; }

    public string? Notes { get; set; }
}
