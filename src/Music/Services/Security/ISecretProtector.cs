namespace Music.Services.Security;

/// <summary>
/// 对敏感字符串（如私有仓库访问令牌）做本机加密存储。
/// 平台实现：Windows 用 DPAPI，Android 用 Keystore；没有实现的平台退化为原样存储。
/// </summary>
public interface ISecretProtector
{
    /// <summary>把明文加密成可安全落盘的字符串；传入空值时返回空串。</summary>
    string Protect(string? plaintext);

    /// <summary>解密 <see cref="Protect"/> 的结果；无法解密（例如换了设备）时返回空串。</summary>
    string Unprotect(string? protectedText);
}