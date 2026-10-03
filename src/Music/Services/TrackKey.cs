using System;
using System.Security.Cryptography;
using System.Text;

namespace Music.Services;

/// <summary>生成跨音源稳定的曲目标识。</summary>
public static class TrackKey
{
    /// <summary>
    /// 由「音源 Id + 曲目路径/Id」生成稳定键。
    /// 路径做大小写与分隔符归一化，避免同一文件在不同平台得到不同 Id。
    /// </summary>
    public static string Create(string sourceId, string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
        return $"{sourceId}:{hash[..16]}";
    }
}
