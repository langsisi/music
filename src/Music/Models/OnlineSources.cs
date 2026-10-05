using System.Collections.Generic;
using System.Linq;

namespace Music.Models;

/// <summary>一个可选在线音源（用于下拉选择）。</summary>
public sealed record OnlineSourceOption(string Id, string Name);

/// <summary>一个可选下载音质（用于下拉选择）。</summary>
public sealed record BitrateOption(int Value, string Name);

/// <summary>
/// GD音乐台当前可用的音源。
/// 说明：官网 API 文档（2026-09-16）列出的 source 参数值还有 tencent、kuwo、tidal、qobuz、
/// apple、ytmusic、spotify 等，但明确标注「部分音乐源暂不开放，建议使用稳定音源」，
/// 实测这些音源均返回「Value of `source` is not supported」，这里只保留当前稳定音源；
/// 服务端开放后在此补一行即可。
/// </summary>
public static class OnlineSources
{
    public static IReadOnlyList<OnlineSourceOption> All { get; } =
    [
        new("netease", "网易云音乐"),
        new("joox", "JOOX"),
        new("bilibili", "B站"),
    ];

    /// <summary>下载时可选的音质。GD 接口按 <c>br</c> 取流，999 返回无损 FLAC。</summary>
    public static IReadOnlyList<BitrateOption> Bitrates { get; } =
    [
        new(128, "标准音质 (128k)"),
        new(192, "高音质 (192k)"),
        new(320, "超高音质 (320k)"),
        new(999, "无损音质 (FLAC)"),
    ];

    public static string DisplayName(string id)
        => All.FirstOrDefault(option => option.Id == id)?.Name ?? id;
}