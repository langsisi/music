#if WINDOWS
using System;
using System.Security.Cryptography;
using System.Text;
using Music.Services.Security;

namespace Music.Desktop;

/// <summary>
/// Windows 上用 DPAPI（CurrentUser 作用域）加密敏感串：密文只能被同一用户的同一账户解开，
/// 拷到别的机器或别的账户都无法解密。
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <summary>附加熵，避免同机其他进程直接拿 DPAPI 解出。</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Music.UpdateToken.v1");

    public string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (CryptographicException)
        {
            // 加密不可用时退回明文，至少保证功能可用。
            return plaintext;
        }
    }

    public string Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return string.Empty;
        }

        try
        {
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception)
        {
            // 可能是旧版本留下的明文（或换机后无法解密），按明文处理。
            return protectedText;
        }
    }
}
#endif