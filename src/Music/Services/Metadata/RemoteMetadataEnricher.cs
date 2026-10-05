using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Library;
using Music.Services.Lyrics;
using Music.Services.Remote;

namespace Music.Services.Metadata;

/// <summary>
/// 远程音源（FTP / SMB / WebDAV）的封面 / 歌词补齐，分两个时机：
/// <list type="bullet">
/// <item>同步入库时：<see cref="PrefetchCoversAsync"/> 只读文件头部解析内嵌封面，避免播放一次才看到封面；
/// 同时顺带抓取同目录同名 .lrc（几 KB 的小文件），新设备同步完即可显示歌词。</item>
/// <item>首次播放后：<see cref="EnrichInBackground"/> 从已落盘的缓存文件读标签，补齐封面与歌词
/// （内嵌没有歌词时再抓同目录同名 .lrc）。</item>
/// </list>
/// Navidrome 由服务端直接提供封面/歌词，本地音源扫描时已读取，因此都不需要它。
/// </summary>
public sealed class RemoteMetadataEnricher
{
    /// <summary>
    /// 预取封面时先读的头部字节数：内嵌封面基本都在文件头部。
    /// 取 2MB 是为了容纳体积较大的封面（FLAC 里常见 1MB 以上的 PNG），
    /// 只读 1MB 会在图片数据中间截断导致 TagLib 解析不出封面。
    /// </summary>
    private const int CoverProbeBytes = 2 * 1024 * 1024;

    /// <summary>
    /// 预取封面的并发连接数。逐首顺序读取时每首都要等一个网络来回，上百首就会拖很久。
    /// 但也不能开太大：远端 WebDAV/FTP 服务器通常有并发上限，连接一多反而被限流、整体更慢。
    /// </summary>
    private const int CoverPrefetchConcurrency = 4;

    /// <summary>单次封面探测的超时秒数。远程客户端用的 HttpClient 是不超时（Timeout.InfiniteTimeSpan）的，
    /// 一旦某条连接被服务端挂起就会永远卡住、进度不动，因此这里必须自己限时。
    /// </summary>
    private const int CoverProbeTimeoutSeconds = 30;

    /// <summary>抓到的封面攒够这么多就落库一次，让列表陆续显示，而不是整轮结束才一次性出现。</summary>
    private const int CoverUpsertBatch = 20;

    private readonly ILibraryStore _library;
    private readonly LyricsService _lyrics;
    private readonly IRemoteFileClientFactory _clients;
    private readonly ISettingsStore _settings;

    /// <summary>本次运行已经尝试过的曲目，避免每次播放都重复抓取远端 .lrc。</summary>
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);

    public RemoteMetadataEnricher(
        ILibraryStore library,
        LyricsService lyrics,
        IRemoteFileClientFactory clients,
        ISettingsStore settings)
    {
        _library = library;
        _lyrics = lyrics;
        _clients = clients;
        _settings = settings;
    }

    /// <summary>后台补齐，不阻塞播放开始；失败静默忽略。</summary>
    public void EnrichInBackground(Track track, string localPath)
    {
        if (track.SourceType is not (MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav))
        {
            return;
        }

        lock (_attempted)
        {
            if (!_attempted.Add(track.Id))
            {
                return;
            }
        }

        _ = Task.Run(() => EnrichAsync(track, localPath, CancellationToken.None));
    }

    private async Task EnrichAsync(Track track, string localPath, CancellationToken cancellationToken)
    {
        try
        {
            var (coverApplied, hasLyrics) = FillFromTags(track, localPath);

            if (!hasLyrics)
            {
                hasLyrics = await TryFetchSidecarLyricsAsync(track, cancellationToken).ConfigureAwait(false);
            }

            if (coverApplied)
            {
                await _library.UpsertTracksAsync([track], cancellationToken).ConfigureAwait(false);
            }

            if (hasLyrics)
            {
                _lyrics.Invalidate(track.Id);
            }
        }
        catch (Exception)
        {
            // 补齐属于锦上添花，失败不影响播放。
        }
    }

    /// <summary>
    /// 同步阶段预取封面与文件标签：只读远端文件的头部（内嵌封面与 ID3v2/Vorbis 标签一般都在头部），
    /// 不做整文件下载。同一音源开若干条连接并行读取（每个 worker 独占一个连接，SMBLibrary 的客户端非线程安全），
    /// 本地已有封面文件时直接复用；头部取不到时留待首次播放后再补齐。
    /// 顺带用文件里的标题 / 歌手 / 专辑 / 年份覆盖「按文件名派生」的值，
    /// 这样在另一台设备重新扫描时，之前写回文件的元数据能正确读回来。
    /// </summary>
    public async Task<int> PrefetchCoversAsync(
        IReadOnlyList<Track> tracks,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var groups = tracks
            .Where(NeedsProbe)
            .GroupBy(track => track.SourceId)
            .ToList();

        var found = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var config = _settings.Current.Sources.FirstOrDefault(source => source.Id == group.Key);
            if (config is null)
            {
                continue;
            }

            found += await FetchGroupCoversAsync(config, group.ToList(), progress, cancellationToken)
                .ConfigureAwait(false);
        }

        return found;
    }

    /// <summary>
    /// 需要读头部的情形：还没有封面，或文件标签还没读过。
    /// 两样都齐全就不再重复读取，避免每次同步都把整个音源的头部再拉一遍。
    /// </summary>
    private static bool NeedsProbe(Track track)
        => track.SourceType is MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav
            && !string.IsNullOrWhiteSpace(track.RemoteId)
            && (string.IsNullOrEmpty(track.CoverPath) || NeedsTagRead(track));

    /// <summary>
    /// 是否要用文件标签覆盖元数据：标题仍是按文件名派生的（说明从未读过标签、也没被用户编辑过），
    /// 且歌手为空。用户改过标题后，标题与文件名不再一致，这里就会跳过，
    /// 避免「写回没成功」时反被文件里的旧标签把用户编辑的内容冲掉。
    /// </summary>
    private static bool NeedsTagRead(Track track)
        => string.IsNullOrEmpty(track.Artist) && IsDerivedTitle(track);

    /// <summary>标题是否等于文件名的去扩展名形式（远端扫描时的初始值）。</summary>
    private static bool IsDerivedTitle(Track track)
        => !string.IsNullOrWhiteSpace(track.RemoteId)
            && string.Equals(
                track.Title,
                Path.GetFileNameWithoutExtension(track.RemoteId),
                StringComparison.Ordinal);

    /// <summary>
    /// 并行预取一组曲目的封面：开 <see cref="CoverPrefetchConcurrency"/> 条连接，
    /// 每条连接用共享游标领取下一首，读完各自解析封面，最后统一批量落库。
    /// </summary>
    private async Task<int> FetchGroupCoversAsync(
        MusicSourceConfig config,
        IReadOnlyList<Track> tracks,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pending = new List<Track>();
        var pendingLock = new object();
        var found = 0;
        var done = 0;
        var cursor = -1;

        var workers = Math.Min(CoverPrefetchConcurrency, tracks.Count);
        var tasks = new List<Task>(workers);

        for (var i = 0; i < workers; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var client = _clients.Create(config);

                using (var connectCts = CreateProbeCts(cancellationToken))
                {
                    try
                    {
                        await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // 这条连接连不上就不干活，其余连接照常，不阻断同步本身。
                        return;
                    }
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var index = Interlocked.Increment(ref cursor);
                    if (index >= tracks.Count)
                    {
                        return;
                    }

                    var track = tracks[index];

                    // 单首超时/失败只跳过这一首：绝不能让一条连接因某首出错而退出，
                    // 否则并发都退出后进度就会彻底停住。
                    TagProbe probe;
                    try
                    {
                        using var probeCts = CreateProbeCts(cancellationToken);
                        probe = await TryProbeAsync(client, track, probeCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        probe = default;
                    }

                    // 头部没读到封面时，退回本地已导出的封面文件（可能是上一轮同步导出的）。
                    var coverFound = false;
                    if (string.IsNullOrEmpty(track.CoverPath))
                    {
                        var cover = !string.IsNullOrEmpty(probe.Cover)
                            ? probe.Cover
                            : AudioTagWriter.FindExistingCover(track.Id);
                        if (cover is not null)
                        {
                            track.CoverPath = cover;
                            coverFound = true;
                            Interlocked.Increment(ref found);
                        }
                    }

                    // 只用文件标签覆盖「还没读过标签」的曲目，避免把同设备上用户已编辑、
                    // 但尚未成功写回文件的元数据冲掉。
                    var metadataFound = NeedsTagRead(track) && ApplyTagMetadata(track, probe);

                    // 顺带抓远端同名 .lrc（几 KB 的小文件）：新设备同步完就有歌词，
                    // 不用等首次播放后再补齐；远端没有 .lrc 时只是多一次小请求。
                    if (await TryCacheSidecarLyricsAsync(client, track, cancellationToken).ConfigureAwait(false))
                    {
                        _lyrics.Invalidate(track.Id);
                    }

                    List<Track>? flush = null;
                    if (coverFound || metadataFound)
                    {
                        lock (pendingLock)
                        {
                            pending.Add(track);
                            if (pending.Count >= CoverUpsertBatch)
                            {
                                flush = [..pending];
                                pending.Clear();
                            }
                        }
                    }

                    progress?.Report(new ScanProgress(
                        "正在读取曲目信息", Interlocked.Increment(ref done), tracks.Count));

                    if (flush is not null)
                    {
                        await _library.UpsertTracksAsync(flush, cancellationToken).ConfigureAwait(false);
                    }
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        List<Track>? remaining;
        lock (pendingLock)
        {
            remaining = pending.Count > 0 ? [..pending] : null;
            pending.Clear();
        }

        if (remaining is not null)
        {
            await _library.UpsertTracksAsync(remaining, cancellationToken).ConfigureAwait(false);
        }

        return found;
    }

    /// <summary>给单次远端探测加一个时间上限，避免服务端挂起时永久卡住（远程客户端本身不设超时）。</summary>
    private static CancellationTokenSource CreateProbeCts(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(CoverProbeTimeoutSeconds));
        return cts;
    }

    /// <summary>
    /// 读远端文件开头 <see cref="CoverProbeBytes"/> 字节，解析内嵌封面与标题 / 歌手 / 专辑 / 年份。
    /// 同步阶段只做这一轮头部读取：绝大多数标签（ID3v2 / Vorbis）都在文件开头，读满 2MB 足够；
    /// 头部取不到时留到首次播放后由 <see cref="EnrichInBackground"/> 从完整文件补齐，
    /// 避免同步时把整个音源都拉下来。远端不支持 Range 时 OpenReadAsync 会从 0 顺序返回，读到上限即停。
    /// </summary>
    private static async Task<TagProbe> TryProbeAsync(
        IRemoteFileClient client,
        Track track,
        CancellationToken cancellationToken)
    {
        var remotePath = track.RemoteId;
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return default;
        }

        return await ProbeAsync(client, track, remotePath, CoverProbeBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>从远端读取文件开头 <paramref name="limit"/> 字节写成临时文件，交给 TagLib 解析封面与标签。</summary>
    private static async Task<TagProbe> ProbeAsync(
        IRemoteFileClient client,
        Track track,
        string remotePath,
        long limit,
        CancellationToken cancellationToken)
    {
        // 放应用缓存目录：Android 上系统临时目录未必可写。
        AppPaths.EnsureCreated();

        // 扩展名取自远端路径（展示用的 Path 在个别音源上可能被改写），TagLib 靠它判断容器格式。
        var temp = Path.Combine(
            AppPaths.CacheDir,
            $"cover-probe-{Guid.NewGuid():N}{Path.GetExtension(remotePath)}");

        try
        {
            await using (var source = await client
                             .OpenReadAsync(remotePath, 0, limit, cancellationToken)
                             .ConfigureAwait(false))
            {
                await using var file = File.Create(temp);
                await CopyAtMostAsync(source, file, limit, cancellationToken).ConfigureAwait(false);
            }

            // TagLib 解析是同步 CPU / IO 操作，放到线程池避免卡住界面线程。
            return await Task.Run(() => ReadTagsFromFile(temp, track.Id), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 格式不受支持或文件损坏时当作没有标签。
            return default;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>解析出的标签信息：封面本地路径 + 标题 / 歌手 / 专辑 / 年份（取不到时为空）。</summary>
    private readonly record struct TagProbe(string? Cover, string Title, string Artist, string Album, int Year);

    private static TagProbe ReadTagsFromFile(string localPath, string trackId)
    {
        try
        {
            using var file = TagLib.File.Create(localPath);
            var tag = file.Tag;

            return new TagProbe(
                AudioTagWriter.ExportCover(trackId, tag.Pictures),
                tag.Title ?? string.Empty,
                tag.FirstPerformer ?? tag.FirstAlbumArtist ?? string.Empty,
                tag.Album ?? string.Empty,
                (int)tag.Year);
        }
        catch (Exception)
        {
            return default;
        }
    }

    /// <summary>用文件标签里的值覆盖按文件名派生的元数据；没有对应标签的字段保持不动。</summary>
    private static bool ApplyTagMetadata(Track track, TagProbe probe)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(probe.Title) && probe.Title != track.Title)
        {
            track.Title = probe.Title;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(probe.Artist) && probe.Artist != track.Artist)
        {
            track.Artist = probe.Artist;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(probe.Album) && probe.Album != track.Album)
        {
            track.Album = probe.Album;
            changed = true;
        }

        if (probe.Year > 0 && probe.Year != track.Year)
        {
            track.Year = probe.Year;
            changed = true;
        }

        return changed;
    }

    private static async Task CopyAtMostAsync(
        Stream source,
        Stream destination,
        long limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var remaining = limit;

        while (remaining > 0)
        {
            var want = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, want), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 临时文件删不掉不影响结果。
        }
    }

    /// <summary>读取缓存文件的内嵌标签，返回「是否补了封面」与「是否拿到了歌词」。</summary>
    private static (bool CoverApplied, bool HasLyrics) FillFromTags(Track track, string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            return (false, false);
        }

        try
        {
            using var file = TagLib.File.Create(localPath);
            var tag = file.Tag;

            var coverApplied = false;
            if (string.IsNullOrEmpty(track.CoverPath))
            {
                var cover = AudioTagWriter.ExportCover(track.Id, tag.Pictures);
                if (cover is not null)
                {
                    track.CoverPath = cover;
                    coverApplied = true;
                }
            }

            var hasLyrics = false;
            if (!string.IsNullOrWhiteSpace(tag.Lyrics) && !File.Exists(AppPaths.LyricsFileFor(track.Id)))
            {
                AppPaths.EnsureCreated();
                File.WriteAllText(AppPaths.LyricsFileFor(track.Id), tag.Lyrics, Encoding.UTF8);
                hasLyrics = true;
            }

            return (coverApplied, hasLyrics);
        }
        catch (Exception)
        {
            // 标签损坏或格式不受支持时当作没有标签。
            return (false, false);
        }
    }

    /// <summary>内嵌标签里没有歌词时，尝试从音源抓同目录同名 .lrc。</summary>
    private async Task<bool> TryFetchSidecarLyricsAsync(Track track, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(track.RemoteId) || File.Exists(AppPaths.LyricsFileFor(track.Id)))
        {
            return false;
        }

        var config = _settings.Current.Sources.FirstOrDefault(source => source.Id == track.SourceId);
        if (config is null)
        {
            return false;
        }

        try
        {
            await using var client = _clients.Create(config);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return await TryCacheSidecarLyricsAsync(client, track, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 连接失败等异常都当作没有歌词。
            return false;
        }
    }

    /// <summary>
    /// 用已连接的远端客户端下载同目录同名 .lrc 并写入本地歌词缓存；
    /// 远端没有 .lrc 或读取失败都返回 false，不影响调用方继续。
    /// </summary>
    private async Task<bool> TryCacheSidecarLyricsAsync(
        IRemoteFileClient client,
        Track track,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(track.RemoteId) || File.Exists(AppPaths.LyricsFileFor(track.Id)))
        {
            return false;
        }

        var lrcPath = Path.ChangeExtension(track.RemoteId, ".lrc");
        if (string.IsNullOrEmpty(lrcPath))
        {
            return false;
        }

        try
        {
            using var buffer = new MemoryStream();
            await client.DownloadAsync(lrcPath, buffer, null, cancellationToken).ConfigureAwait(false);

            var text = DecodeLyrics(buffer.ToArray());
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            AppPaths.EnsureCreated();
            await File.WriteAllTextAsync(
                    AppPaths.LyricsFileFor(track.Id), text, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // 没有同名 .lrc 或读取失败都当作没有歌词。
            return false;
        }
    }

    /// <summary>解码 .lrc 文本，与本地 .lrc 的读取保持一致：带 BOM 按 BOM，否则按 UTF-8。</summary>
    private static string DecodeLyrics(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
