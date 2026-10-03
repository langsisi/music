using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services.Sources;

/// <summary>
/// 统一的音源抽象：把某个音源的全部曲目「同步」进本地索引。
/// 播放时的取流由 <c>IMediaResolver</c>（P5）负责，与本接口解耦。
/// </summary>
public interface IMusicSource
{
    MusicSourceConfig Config { get; }

    MusicSourceType Type { get; }

    Task<IReadOnlyList<Track>> GetTracksAsync(
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>支持「测试连接」的音源（只有远程音源才有意义）。</summary>
public interface IConnectionTestableMusicSource
{
    Task TestConnectionAsync(CancellationToken cancellationToken = default);
}
