using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Music.Models;
using Music.Services.Remote;

namespace Music.Services.Cache;

/// <summary>
/// 基于 SQLite 索引 + 文件系统的音频缓存。
/// 索引与曲库共用一个数据库文件；音频本体落在 <see cref="AppPaths.CacheDir"/>。
/// </summary>
public sealed class SqliteAudioCache : IAudioCache
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS Cache (
            Key           TEXT PRIMARY KEY,
            SourceUri     TEXT NOT NULL,
            FilePath      TEXT NOT NULL,
            ContentType   TEXT NULL,
            Bytes         INTEGER NOT NULL DEFAULT 0,
            AddedUtc      TEXT NOT NULL,
            LastPlayedUtc TEXT NOT NULL
        );
        """;

    /// <summary>触发淘汰时预留的余量，避免刚淘汰完又立刻写满。</summary>
    private const double EvictionTargetRatio = 0.9;

    private readonly ISettingsStore _settings;
    private readonly HttpClient _httpClient;
    private readonly IRemoteFileClientFactory _remoteClientFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private string? _pinnedKey;

    public SqliteAudioCache(
        ISettingsStore settings,
        HttpClient httpClient,
        IRemoteFileClientFactory remoteClientFactory)
    {
        _settings = settings;
        _httpClient = httpClient;
        _remoteClientFactory = remoteClientFactory;
    }

    public long MaxBytes => (long)(_settings.Current.CacheLimitMb * 1024 * 1024);

    public bool IsEnabled => MaxBytes > 0;

    private static SqliteConnection CreateConnection()
        => new(new SqliteConnectionStringBuilder { DataSource = AppPaths.LibraryDbPath }.ToString());

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            AppPaths.EnsureCreated();
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = SchemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetPinned(string? trackId) => _pinnedKey = trackId;

    public async Task<string?> TryGetAsync(Track track, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        string? path;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT FilePath FROM Cache WHERE Key = $key";
            command.Parameters.AddWithValue("$key", track.Id);
            path = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        if (path is null)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            // 文件被外部清理掉了，索引也要跟着清理。
            await RemoveEntryAsync(track.Id, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await TouchAsync(track.Id, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<string> DownloadAsync(
        Track track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var cached = await TryGetAsync(track, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            progress?.Report(1);
            return cached;
        }

        AppPaths.EnsureCreated();

        var safeKey = ToSafeFileName(track.Id);
        var folder = Path.Combine(AppPaths.CacheDir, safeKey[..Math.Min(2, safeKey.Length)]);
        Directory.CreateDirectory(folder);

        var tempPath = Path.Combine(folder, safeKey + ".tmp");
        var contentType = await DownloadToFileAsync(track, tempPath, progress, cancellationToken)
            .ConfigureAwait(false);

        var finalPath = Path.Combine(folder, safeKey + ResolveExtension(contentType, track.Path));

        // 原子替换：下载完整后才让正式文件出现，避免播放到半截文件。
        File.Move(tempPath, finalPath, overwrite: true);

        var bytes = new FileInfo(finalPath).Length;
        await UpsertAsync(track, finalPath, contentType, bytes, cancellationToken).ConfigureAwait(false);

        await EvictAsync(cancellationToken).ConfigureAwait(false);
        return finalPath;
    }

    public void DownloadInBackground(Track track)
    {
        if (!IsEnabled)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await DownloadAsync(track).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 后台预取失败不影响正在进行的播放。
            }
        });
    }

    public async Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IFNULL(SUM(Bytes), 0), COUNT(*) FROM Cache";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var total = reader.GetInt64(0);
            var count = reader.GetInt32(1);
            return new CacheStats(total, count);
        }

        return new CacheStats(0, 0);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT FilePath FROM Cache";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                TryDeleteFile(reader.GetString(0));
            }
        }

        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Cache";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> EvictAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var limit = MaxBytes;

        var entries = new System.Collections.Generic.List<(string Key, string Path, long Bytes)>();
        long total = 0;

        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            // LRU：最久未播放的排在最前。
            command.CommandText = "SELECT Key, FilePath, Bytes FROM Cache ORDER BY LastPlayedUtc ASC";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var entry = (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
                entries.Add(entry);
                total += entry.Item3;
            }
        }

        // 上限为 0 表示禁用缓存，直接清空。
        var targetBytes = limit <= 0 ? 0 : (long)(limit * EvictionTargetRatio);
        if (total <= targetBytes)
        {
            return 0;
        }

        var removed = 0;
        foreach (var (key, path, bytes) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 正在播放的条目不能删。
            if (key == _pinnedKey)
            {
                continue;
            }

            TryDeleteFile(path);
            await RemoveEntryAsync(key, cancellationToken).ConfigureAwait(false);

            total -= bytes;
            removed++;

            if (total <= targetBytes)
            {
                break;
            }
        }

        return removed;
    }

    private async Task<string?> DownloadToFileAsync(
        Track track,
        string tempPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        switch (track.SourceType)
        {
            case MusicSourceType.Navidrome:
                return await DownloadHttpAsync(track.Path, tempPath, progress, cancellationToken)
                    .ConfigureAwait(false);

            case MusicSourceType.Ftp:
            case MusicSourceType.Smb:
            case MusicSourceType.WebDav:
                return await DownloadRemoteAsync(track, tempPath, progress, cancellationToken)
                    .ConfigureAwait(false);

            default:
                throw new NotSupportedException($"本地曲目不需要缓存：{track.Path}");
        }
    }

    /// <summary>FTP/SMB/WebDAV 没有 Range 语义，必须整文件下载到缓存后再本地播放。</summary>
    private async Task<string?> DownloadRemoteAsync(
        Track track,
        string tempPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var config = _settings.Current.Sources
            .FirstOrDefault(source => source.Id == track.SourceId)
            ?? throw new InvalidOperationException($"找不到音源配置：{track.SourceId}");

        var remotePath = track.RemoteId
            ?? throw new InvalidOperationException($"远端曲目缺少远端路径：{track.Id}");

        await using var client = _remoteClientFactory.Create(config);
        await using var destination = File.Create(tempPath);
        await client.DownloadAsync(remotePath, destination, progress, cancellationToken).ConfigureAwait(false);

        // 远端协议不返回 Content-Type，扩展名交给 DownloadAsync 从源地址推断。
        return null;
    }

    private async Task<string?> DownloadHttpAsync(
        string url,
        string tempPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var total = response.Content.Headers.ContentLength ?? -1;

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;

                if (total > 0)
                {
                    progress?.Report((double)received / total);
                }
            }
        }

        return contentType;
    }

    private async Task UpsertAsync(
        Track track,
        string filePath,
        string? contentType,
        long bytes,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Cache (Key, SourceUri, FilePath, ContentType, Bytes, AddedUtc, LastPlayedUtc)
            VALUES ($key, $uri, $path, $type, $bytes, $now, $now)
            ON CONFLICT(Key) DO UPDATE SET
                FilePath      = excluded.FilePath,
                ContentType   = excluded.ContentType,
                Bytes         = excluded.Bytes,
                LastPlayedUtc = excluded.LastPlayedUtc;
            """;

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.AddWithValue("$key", track.Id);
        command.Parameters.AddWithValue("$uri", track.Path);
        command.Parameters.AddWithValue("$path", filePath);
        command.Parameters.AddWithValue("$type", (object?)contentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$bytes", bytes);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task TouchAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Cache SET LastPlayedUtc = $now WHERE Key = $key";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveEntryAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Cache WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>曲目 Id 形如 <c>sourceId:hash</c>，冒号在 Windows 文件名中非法。</summary>
    private static string ToSafeFileName(string key) => key.Replace(':', '_');

    private static string ResolveExtension(string? contentType, string sourceUri)
    {
        var fromType = contentType?.ToLowerInvariant() switch
        {
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/flac" or "audio/x-flac" => ".flac",
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a",
            "audio/ogg" or "application/ogg" => ".ogg",
            "audio/opus" => ".opus",
            "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
            "audio/aac" => ".aac",
            "audio/webm" => ".webm",
            _ => null,
        };

        if (fromType is not null)
        {
            return fromType;
        }

        // 回退：从 URI 路径里取扩展名。
        var extension = Path.GetExtension(sourceUri);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 5 ? ".bin" : extension;
    }

    private static void TryDeleteFile(string path)
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
            // 文件被占用时忽略，留待下次淘汰。
        }
    }
}
