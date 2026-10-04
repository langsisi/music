using System;
using System.Text;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Music.Services.Security;

namespace Music.Android;

/// <summary>
/// Android 上用系统 Keystore 里的 AES-GCM 密钥加密敏感串：密钥留在系统密钥库、不落应用目录，
/// 应用私有目录里的密文即使被读走也无法解密。
/// </summary>
public sealed class KeystoreSecretProtector : ISecretProtector
{
    private const string KeystoreProvider = "AndroidKeyStore";
    private const string KeyAlias = "music_update_token";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int GcmTagBits = 128;
    private const int IvLength = 12;

    public string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        try
        {
            using var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());

            var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(plaintext))!;
            var iv = cipher.GetIV() ?? [];

            // 输出 = IV(12 字节) + 密文，整体 base64。
            var payload = new byte[iv.Length + encrypted.Length];
            Buffer.BlockCopy(iv, 0, payload, 0, iv.Length);
            Buffer.BlockCopy(encrypted, 0, payload, iv.Length, encrypted.Length);
            return Convert.ToBase64String(payload);
        }
        catch (Exception)
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
            var payload = Convert.FromBase64String(protectedText);
            if (payload.Length <= IvLength)
            {
                return protectedText;
            }

            var iv = payload[..IvLength];

            using var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(GcmTagBits, iv));

            var plain = cipher.DoFinal(payload, IvLength, payload.Length - IvLength)!;
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            // 旧版本可能存的是明文，解密失败就按明文用。
            return protectedText;
        }
    }

    /// <summary>取出（不存在则生成）Keystore 里的 AES 密钥。</summary>
    private static IKey GetOrCreateKey()
    {
        var keyStore = KeyStore.GetInstance(KeystoreProvider)!;
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias) && keyStore.GetKey(KeyAlias, null) is { } existing)
        {
            return existing;
        }

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeystoreProvider)!;
        var spec = new KeyGenParameterSpec.Builder(
                KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(256)!
            .Build()!;

        generator.Init(spec);
        return generator.GenerateKey()!;
    }
}