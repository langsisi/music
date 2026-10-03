using System;
using System.Collections.Generic;

namespace Music.Models;

/// <summary>一行带时间戳的歌词。</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>解析后的歌词文档，按时间升序排列。</summary>
public sealed class LyricDocument
{
    public static readonly LyricDocument Empty = new([]);

    public LyricDocument(IReadOnlyList<LyricLine> lines) => Lines = lines;

    public IReadOnlyList<LyricLine> Lines { get; }

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>二分查找当前时刻应高亮的行；返回 -1 表示还在第一行之前（前奏）。</summary>
    public int IndexAt(double seconds)
    {
        if (Lines.Count == 0)
        {
            return -1;
        }

        var target = TimeSpan.FromSeconds(seconds);
        var low = 0;
        var high = Lines.Count - 1;
        var result = -1;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (Lines[mid].Time <= target)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }
}
