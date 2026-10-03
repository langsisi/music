using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Music.Models;

namespace Music.Services.Lyrics;

/// <summary>
/// 手写的 LRC 解析器。第三方 LRC 包均已停更且不支持 .NET 10，自研更可控。
/// 支持：一行多个时间戳、<c>[offset:±ms]</c>、两位/三位小数、<c>[ti:][ar:][al:]</c> 等元数据（忽略）。
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTagRegex();

    [GeneratedRegex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();

    [GeneratedRegex(@"^\[[a-zA-Z#]+:.*\]$")]
    private static partial Regex MetadataRegex();

    public static LyricDocument Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return LyricDocument.Empty;
        }

        var offset = TimeSpan.Zero;
        var offsetMatch = OffsetRegex().Match(content);
        if (offsetMatch.Success &&
            int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offsetMs))
        {
            offset = TimeSpan.FromMilliseconds(offsetMs);
        }

        var entries = new List<(TimeSpan Time, string Text)>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            var matches = TimeTagRegex().Matches(line);
            if (matches.Count == 0)
            {
                // 纯元数据行（[ti:]、[ar:] 等）直接忽略。
                continue;
            }

            // 时间戳之后的部分才是歌词正文。
            var text = line[(matches[^1].Index + matches[^1].Length)..].Trim();

            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = 0;

                if (match.Groups[3].Success)
                {
                    var digits = match.Groups[3].Value;
                    // 「.5」是 500ms，「.05」是 50ms，按位数补齐。
                    fraction = digits.Length switch
                    {
                        1 => int.Parse(digits, CultureInfo.InvariantCulture) * 100,
                        2 => int.Parse(digits, CultureInfo.InvariantCulture) * 10,
                        _ => int.Parse(digits[..3], CultureInfo.InvariantCulture),
                    };
                }

                var time = new TimeSpan(0, 0, minutes, seconds, fraction) + offset;
                entries.Add((time, text));
            }
        }

        if (entries.Count == 0)
        {
            return LyricDocument.Empty;
        }

        var lines = entries
            .OrderBy(entry => entry.Time)
            .Select(entry => new LyricLine(entry.Time, entry.Text))
            .ToList();

        return new LyricDocument(lines);
    }
}
