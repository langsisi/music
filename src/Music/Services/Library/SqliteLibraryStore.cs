using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Music.Models;

namespace Music.Services.Library;

public sealed class SqliteLibraryStore : ILibraryStore
{
    /// <summary>
    /// 建表脚本。收藏 / 分类表一并建好，后续阶段直接使用，避免再做迁移。
    /// </summary>
    private const string SchemaSql = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS Tracks (
            Id              TEXT PRIMARY KEY,
            SourceId        TEXT NOT NULL,
            SourceType      INTEGER NOT NULL,
            Path            TEXT NOT NULL,
            Title           TEXT NOT NULL DEFAULT '',
            Artist          TEXT NOT NULL DEFAULT '',
            Album           TEXT NOT NULL DEFAULT '',
            Genre           TEXT NOT NULL DEFAULT '',
            DurationSeconds REAL NOT NULL DEFAULT 0,
            TrackNumber     INTEGER NOT NULL DEFAULT 0,
            Year            INTEGER NOT NULL DEFAULT 0,
            CoverPath       TEXT NULL,
            FileSize        INTEGER NOT NULL DEFAULT 0,
            RemoteId        TEXT NULL,
            AddedUtc        TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Tracks_SourceId ON Tracks (SourceId);
        CREATE INDEX IF NOT EXISTS IX_Tracks_Artist ON Tracks (Artist);

        CREATE TABLE IF NOT EXISTS Favorites (
            TrackId  TEXT PRIMARY KEY,
            AddedUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Categories (
            Id        TEXT PRIMARY KEY,
            Name      TEXT NOT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CategoryTracks (
            CategoryId TEXT NOT NULL,
            TrackId    TEXT NOT NULL,
            PRIMARY KEY (CategoryId, TrackId)
        );
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public event EventHandler? Changed;

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

    public async Task UpsertTracksAsync(
        IReadOnlyCollection<Track> tracks,
        CancellationToken cancellationToken = default)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO Tracks
                    (Id, SourceId, SourceType, Path, Title, Artist, Album, Genre,
                     DurationSeconds, TrackNumber, Year, CoverPath, FileSize, RemoteId, AddedUtc)
                VALUES
                    ($id, $sourceId, $sourceType, $path, $title, $artist, $album, $genre,
                     $duration, $trackNumber, $year, $coverPath, $fileSize, $remoteId, $addedUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    SourceId        = excluded.SourceId,
                    SourceType      = excluded.SourceType,
                    Path            = excluded.Path,
                    Title           = excluded.Title,
                    Artist          = excluded.Artist,
                    Album           = excluded.Album,
                    Genre           = excluded.Genre,
                    DurationSeconds = excluded.DurationSeconds,
                    TrackNumber     = excluded.TrackNumber,
                    Year            = excluded.Year,
                    CoverPath       = excluded.CoverPath,
                    FileSize        = excluded.FileSize,
                    RemoteId        = excluded.RemoteId;
                """;

            var pId = command.Parameters.Add("$id", SqliteType.Text);
            var pSourceId = command.Parameters.Add("$sourceId", SqliteType.Text);
            var pSourceType = command.Parameters.Add("$sourceType", SqliteType.Integer);
            var pPath = command.Parameters.Add("$path", SqliteType.Text);
            var pTitle = command.Parameters.Add("$title", SqliteType.Text);
            var pArtist = command.Parameters.Add("$artist", SqliteType.Text);
            var pAlbum = command.Parameters.Add("$album", SqliteType.Text);
            var pGenre = command.Parameters.Add("$genre", SqliteType.Text);
            var pDuration = command.Parameters.Add("$duration", SqliteType.Real);
            var pTrackNumber = command.Parameters.Add("$trackNumber", SqliteType.Integer);
            var pYear = command.Parameters.Add("$year", SqliteType.Integer);
            var pCoverPath = command.Parameters.Add("$coverPath", SqliteType.Text);
            var pFileSize = command.Parameters.Add("$fileSize", SqliteType.Integer);
            var pRemoteId = command.Parameters.Add("$remoteId", SqliteType.Text);
            var pAddedUtc = command.Parameters.Add("$addedUtc", SqliteType.Text);

            foreach (var track in tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                pId.Value = track.Id;
                pSourceId.Value = track.SourceId;
                pSourceType.Value = (int)track.SourceType;
                pPath.Value = track.Path;
                pTitle.Value = track.Title;
                pArtist.Value = track.Artist;
                pAlbum.Value = track.Album;
                pGenre.Value = track.Genre;
                pDuration.Value = track.DurationSeconds;
                pTrackNumber.Value = track.TrackNumber;
                pYear.Value = track.Year;
                pCoverPath.Value = (object?)track.CoverPath ?? DBNull.Value;
                pFileSize.Value = track.FileSize;
                pRemoteId.Value = (object?)track.RemoteId ?? DBNull.Value;
                pAddedUtc.Value = track.AddedUtc.ToString("O", CultureInfo.InvariantCulture);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<Track>> GetTracksAsync(
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var sql = SelectColumns + " FROM Tracks";
        if (!string.IsNullOrEmpty(sourceId))
        {
            sql += " WHERE SourceId = $sourceId";
        }

        sql += " ORDER BY Artist COLLATE NOCASE, Album COLLATE NOCASE, TrackNumber, Title COLLATE NOCASE";

        return QueryAsync(sql, sourceId, null, cancellationToken);
    }

    public async Task<Track?> GetTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var results = await QueryAsync(
            SelectColumns + " FROM Tracks WHERE Id = $id",
            null,
            trackId,
            cancellationToken).ConfigureAwait(false);

        return results.Count > 0 ? results[0] : null;
    }

    public Task<IReadOnlyList<Track>> SearchTracksAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var like = $"%{query}%";
        return QueryAsync(
            SelectColumns + """
                 FROM Tracks
                 WHERE Title LIKE $like OR Artist LIKE $like OR Album LIKE $like
                 ORDER BY Artist COLLATE NOCASE, Album COLLATE NOCASE, TrackNumber
                """,
            null,
            null,
            cancellationToken,
            like);
    }

    public async Task<int> GetTrackCountAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Tracks";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetSourceTrackCountAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Tracks WHERE SourceId = $sourceId";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task RemoveSourceTracksAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // 顺带清掉收藏与归类关系，避免索引无限增长。
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    DELETE FROM Favorites
                     WHERE TrackId IN (SELECT Id FROM Tracks WHERE SourceId = $sourceId);
                    DELETE FROM CategoryTracks
                     WHERE TrackId IN (SELECT Id FROM Tracks WHERE SourceId = $sourceId);
                    DELETE FROM Tracks WHERE SourceId = $sourceId;
                    """;
                command.Parameters.AddWithValue("$sourceId", sourceId);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- 收藏 ----------------

    public async Task<IReadOnlyCollection<string>> GetFavoriteTrackIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TrackId FROM Favorites";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public async Task SetFavoriteAsync(
        string trackId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            if (isFavorite)
            {
                command.CommandText =
                    "INSERT OR IGNORE INTO Favorites (TrackId, AddedUtc) VALUES ($trackId, $addedUtc)";
                command.Parameters.AddWithValue("$trackId", trackId);
                command.Parameters.AddWithValue(
                    "$addedUtc",
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            }
            else
            {
                command.CommandText = "DELETE FROM Favorites WHERE TrackId = $trackId";
                command.Parameters.AddWithValue("$trackId", trackId);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- 分类 ----------------

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var categories = new List<Category>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, SortOrder FROM Categories ORDER BY SortOrder, Name COLLATE NOCASE";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            categories.Add(new Category
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                SortOrder = reader.GetInt32(2),
            });
        }

        return categories;
    }

    public async Task<Category> CreateCategoryAsync(string name, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Categories (Id, Name, SortOrder)
                VALUES ($id, $name, (SELECT IFNULL(MAX(SortOrder), 0) + 1 FROM Categories));
                """;
            command.Parameters.AddWithValue("$id", category.Id);
            command.Parameters.AddWithValue("$name", category.Name);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return category;
    }

    public async Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM CategoryTracks WHERE CategoryId = $id;
                DELETE FROM Categories WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$id", categoryId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<Track>> GetCategoryTracksAsync(
        string categoryId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<Track>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectColumns}
              FROM Tracks
              JOIN CategoryTracks ON CategoryTracks.TrackId = Tracks.Id
             WHERE CategoryTracks.CategoryId = $categoryId
             ORDER BY Artist COLLATE NOCASE, Album COLLATE NOCASE, TrackNumber, Title COLLATE NOCASE
            """;
        command.Parameters.AddWithValue("$categoryId", categoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadTrack(reader));
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetTrackCategoryMapAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TrackId, CategoryId FROM CategoryTracks";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var trackId = reader.GetString(0);
            if (!map.TryGetValue(trackId, out var categories))
            {
                categories = new HashSet<string>(StringComparer.Ordinal);
                map[trackId] = categories;
            }

            categories.Add(reader.GetString(1));
        }

        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        foreach (var pair in map)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    public Task AddTrackToCategoryAsync(
        string categoryId,
        string trackId,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            "INSERT OR IGNORE INTO CategoryTracks (CategoryId, TrackId) VALUES ($categoryId, $trackId)",
            categoryId,
            trackId,
            cancellationToken);

    public Task RemoveTrackFromCategoryAsync(
        string categoryId,
        string trackId,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            "DELETE FROM CategoryTracks WHERE CategoryId = $categoryId AND TrackId = $trackId",
            categoryId,
            trackId,
            cancellationToken);

    private async Task ExecuteAsync(
        string sql,
        string categoryId,
        string trackId,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$categoryId", categoryId);
            command.Parameters.AddWithValue("$trackId", trackId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private const string SelectColumns = """
        SELECT Id, SourceId, SourceType, Path, Title, Artist, Album, Genre,
               DurationSeconds, TrackNumber, Year, CoverPath, FileSize, RemoteId, AddedUtc
        """;

    private async Task<IReadOnlyList<Track>> QueryAsync(
        string sql,
        string? sourceId,
        string? trackId,
        CancellationToken cancellationToken,
        string? like = null)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<Track>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (sourceId is not null)
        {
            command.Parameters.AddWithValue("$sourceId", sourceId);
        }

        if (trackId is not null)
        {
            command.Parameters.AddWithValue("$id", trackId);
        }

        if (like is not null)
        {
            command.Parameters.AddWithValue("$like", like);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadTrack(reader));
        }

        return results;
    }

    /// <summary>按 <see cref="SelectColumns"/> 的列顺序物化一行曲目。</summary>
    private static Track ReadTrack(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        SourceId = reader.GetString(1),
        SourceType = (MusicSourceType)reader.GetInt32(2),
        Path = reader.GetString(3),
        Title = reader.GetString(4),
        Artist = reader.GetString(5),
        Album = reader.GetString(6),
        Genre = reader.GetString(7),
        DurationSeconds = reader.GetDouble(8),
        TrackNumber = reader.GetInt32(9),
        Year = reader.GetInt32(10),
        CoverPath = reader.IsDBNull(11) ? null : reader.GetString(11),
        FileSize = reader.GetInt64(12),
        RemoteId = reader.IsDBNull(13) ? null : reader.GetString(13),
        AddedUtc = DateTimeOffset.TryParse(
            reader.GetString(14),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var added)
            ? added
            : DateTimeOffset.UtcNow,
    };
}
