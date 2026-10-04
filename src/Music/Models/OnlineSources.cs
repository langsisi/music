using System.Collections.Generic;
using System.Linq;

namespace Music.Models;

/// <summary>一个可选在线音源（用于下拉选择）。</summary>
public sealed record OnlineSourceOption(string Id, string Name);

/// <summary>一个可选下载音质（用于下拉选择）。</summary>
public sealed record BitrateOption(int Value, string Name);

/// <summary>
/// GD音乐台当前可用的音源。
/// 说明：该免费接口的音源由服务端控制，实测目前仅 <c>netease</c> 与 <c>joox</c> 可用，
/// 其余名称会返回「Value of `source` is not supported」。这里只保留可用项，方便后续增删。
/// </summary>
public static class OnlineSources
{
    public static IReadOnlyList<OnlineSourceOption> All { get; } =
    [
        new("netease", "网易云音乐"),
        new("joox", "JOOX"),
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