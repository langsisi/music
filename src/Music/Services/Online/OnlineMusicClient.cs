using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Online;

/// <summary>
/// 在线音乐取数入口，按 <c>source</c> 分发到具体客户端：
/// QQ 音乐走 <see cref="TencentMusicClient"/> 直连官方接口，其余音源（netease / joox / bilibili）
/// 仍走 <see cref="GdMusicClient"/>。这样上层（搜索 / 播放 / 下载）无需感知音源差异。
/// </summary>
public sealed class OnlineMusicClient
{
    /// <summary>QQ 音乐的音源标识。</summary>
    public const string TencentSource = "tencent";

    private readonly GdMusicClient _gd;
    private readonly TencentMusicClient _tencent;

    public OnlineMusicClient(GdMusicClient gd, TencentMusicClient tencent)
    {
        _gd = gd;
        _tencent = tencent;
    }

    public Task<IReadOnlyList<OnlineTrack>> SearchAsync(
        string source,
        string keyword,
        int count = 30,
        CancellationToken cancellationToken = default)
        => IsTencent(source)
            ? _tencent.SearchAsync(keyword, count, cancellationToken)
            : _gd.SearchAsync(source, keyword, count, cancellationToken);

    public Task<string?> GetStreamUrlAsync(
        string source,
        string id,
        int bitrate,
        CancellationToken cancellationToken = default)
        => IsTencent(source)
            ? _tencent.GetStreamUrlAsync(id, bitrate, cancellationToken)
            : _gd.GetStreamUrlAsync(source, id, bitrate, cancellationToken);

    public Task<byte[]?> GetCoverBytesAsync(
        string source,
        string picId,
        int size = 300,
        CancellationToken cancellationToken = default)
        => IsTencent(source)
            ? _tencent.GetCoverBytesAsync(picId, size, cancellationToken)
            : _gd.GetCoverBytesAsync(source, picId, size, cancellationToken);

    public Task<string?> GetLyricAsync(
        string source,
        string lyricId,
        CancellationToken cancellationToken = default)
        => IsTencent(source)
            ? _tencent.GetLyricAsync(lyricId, cancellationToken)
            : _gd.GetLyricAsync(source, lyricId, cancellationToken);

    private static bool IsTencent(string source)
        => string.Equals(source, TencentSource, StringComparison.OrdinalIgnoreCase);
}