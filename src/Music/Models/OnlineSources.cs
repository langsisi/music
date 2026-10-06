using System.Collections.Generic;
using System.Linq;

namespace Music.Models;

/// <summary>一个可选在线音源（用于下拉选择）。</summary>
public sealed record OnlineSourceOption(string Id, string Name);

/// <summary>一个可选下载音质（用于下拉选择）。</summary>
public sealed record BitrateOption(int Value, string Name);

/// <summary>
/// 在线发现可选的音源。
/// <list type="bullet">
/// <item>netease / joox / bilibili：走 GD 音乐台公开接口。官网 API 文档（2026-09-16）列出的
/// source 值还有 kuwo、tidal、qobuz、apple、ytmusic、spotify 等，但明确标注「部分音乐源暂不开放」，
/// 实测这些音源均返回「Value of `source` is not supported」，这里只保留当前稳定音源；</item>
/// <item>tencent：GD 音乐台不开放 QQ 音源，改由应用直连 QQ 官方接口（见 <c>TencentMusicClient</c>），
/// 免登录可播免费曲的 128k，配置 Cookie 后可用高音质与会员曲。</item>
/// </list>
/// </summary>
public static class OnlineSources
{
    public static IReadOnlyList<OnlineSourceOption> All { get; } =
    [
        new("netease", "网易云音乐"),
        new("joox", "JOOX"),
        new("bilibili", "B站"),
        new("tencent", "QQ音乐"),
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