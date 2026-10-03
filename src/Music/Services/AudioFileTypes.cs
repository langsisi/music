using System;
using System.Collections.Generic;
using System.IO;

namespace Music.Services;

/// <summary>可播放的音频扩展名。本地文件夹扫描与 FTP 扫描共用同一份定义。</summary>
public static class AudioFileTypes
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".m4b", ".aac", ".ogg", ".opus",
        ".wav", ".wma", ".ape", ".wv", ".aiff", ".aif", ".dsf", ".dff", ".mp4",
    };

    public static bool IsAudioFile(string path) => Extensions.Contains(Path.GetExtension(path));
}
