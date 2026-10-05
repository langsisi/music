using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata;

/// <summary>刮削搜索的查询条件（取自曲目标签）。</summary>
public sealed record TrackQuery(string Title, string Artist, string Album);

/// <summary>某个数据源返回的一条候选匹配。<see cref="Year"/> 为 0 表示该数据源未提供年份。</summary>
public sealed record MetadataCandidate(
    string ProviderId,
    string RemoteId,
    string Title,
    string Artist,
    string Album,
    string? CoverUrl,
    bool HasLyrics,
    int Year = 0);

/// <summary>
/// 各数据源的时间字段格式不一（日期字符串 / 秒级或毫秒级时间戳），这里统一尝试解析出年份；
/// 解析不出来就返回 0，界面据此保留用户手填的年份。
/// </summary>
internal static class MetadataYear
{
    /// <summary>从「1991-09-24T07:00:00Z」这类日期字符串取前 4 位数字年份。</summary>
    public static int FromDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var year = 0;
        var digits = 0;

        foreach (var ch in value.AsSpan().TrimStart())
        {
            if (!char.IsAsciiDigit(ch))
            {
                break;
            }

            year = (year * 10) + (ch - '0');
            if (++digits == 4)
            {
                break;
            }
        }

        return digits == 4 && year is > 1000 and < 3000 ? year : 0;
    }

    /// <summary>从秒级或毫秒级 Unix 时间戳换算年份。</summary>
    public static int FromUnix(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        // 13 位按毫秒处理，10 位按秒处理。
        var seconds = timestamp > 99_999_999_999 ? timestamp / 1000 : timestamp;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).Year;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    /// <summary>从 JSON 属性读年份：数字按时间戳、字符串按日期或数字时间戳处理；缺失返回 0。</summary>
    public static int FromElement(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt64(out var number):
                return FromUnix(number);

            case JsonValueKind.String:
                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp)
                    ? FromUnix(timestamp)
                    : FromDate(text);

            default:
                return 0;
        }
    }
}

/// <summary>设置页数据源下拉项。</summary>
public sealed record MetadataProviderOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>在线元数据（封面 / 歌词）数据源。</summary>
public interface IMetadataProvider
{
    /// <summary>数据源 Id：netease / qq / kugou / kuwo / itunes。</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>该数据源是否提供歌词（iTunes 仅提供封面）。</summary>
    bool SupportsLyrics { get; }

    Task<IReadOnlyList<MetadataCandidate>> SearchAsync(TrackQuery query, CancellationToken cancellationToken = default);

    /// <summary>下载候选曲目的封面，无封面时返回 null。</summary>
    Task<byte[]?> GetCoverAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>获取候选曲目的歌词原文（LRC 或纯文本），无歌词时返回 null。</summary>
    Task<string?> GetLyricsAsync(MetadataCandidate candidate, CancellationToken cancellationToken = default);
}
