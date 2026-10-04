using System;
using System.Collections.Generic;
using System.IO;

namespace Music.iOS;

/// <summary>
/// AVFoundation 不支持的音频格式白名单，用于在 <c>Load()</c> 时提前给出明确提示，
/// 避免用户遇到「静默失败」。
/// </summary>
internal static class IosAudioFormatSupport
{
    private static readonly HashSet<string> UnsupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ogg", ".oga", ".opus", ".wma", ".ape", ".wv", ".dsf", ".dff", ".mka", ".tak",
    };

    /// <summary>按扩展名判断；取不到扩展名（如 Navidrome 的 stream 接口）一律视为支持。</summary>
    public static bool IsSupported(string pathOrUrl)
    {
        var path = pathOrUrl;

        // URL 可能带查询串（Navidrome 的 rest/stream?id=...），先剥离再取扩展名。
        var queryStart = path.IndexOf('?');
        if (queryStart >= 0)
        {
            path = path[..queryStart];
        }

        var extension = Path.GetExtension(path);
        return extension.Length == 0 || !UnsupportedExtensions.Contains(extension);
    }
}