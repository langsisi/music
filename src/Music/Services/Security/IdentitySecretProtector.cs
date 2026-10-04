namespace Music.Services.Security;

/// <summary>
/// 不做加密的占位实现：平台没有可用密钥库（或加密失败）时使用，
/// 保证功能可用，只是令牌以明文落盘。
/// </summary>
public sealed class IdentitySecretProtector : ISecretProtector
{
    public string Protect(string? plaintext) => plaintext ?? string.Empty;

    public string Unprotect(string? protectedText) => protectedText ?? string.Empty;
}