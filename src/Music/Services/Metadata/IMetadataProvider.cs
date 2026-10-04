using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Metadata;

/// <summary>刮削搜索的查询条件（取自曲目标签）。</summary>
public sealed record TrackQuery(string Title, string Artist, string Album);

/// <summary>某个数据源返回的一条候选匹配。</summary>
public sealed record MetadataCandidate(
    string ProviderId,
    string RemoteId,
    string Title,
    string Artist,
    string Album,
    string? CoverUrl,
    bool HasLyrics);

/// <summary>设置页数据源下拉项。</summary>
public sealed record MetadataProviderOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>在线元数据（封面 / 歌词）数据源。</summary>
public interface IMetadataProvider
{
    /// <summary>数据源 Id：netease / qq / kugou / kuwo / migu / itunes。</summary>
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
