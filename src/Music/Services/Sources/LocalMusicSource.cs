using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Metadata;

namespace Music.Services.Sources;

/// <summary>扫描本地文件夹，用 TagLibSharp 读取标签与内嵌封面。</summary>
public sealed class LocalMusicSource : IMusicSource
{
    private readonly LocalSourceConfig _config;

    public LocalMusicSource(LocalSourceConfig config) => _config = config;

    public MusicSourceConfig Config => _config;

    public MusicSourceType Type => MusicSourceType.Local;

    public Task<IReadOnlyList<Track>> GetTracksAsync(
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private IReadOnlyList<Track> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var tracks = new List<Track>();
        var folders = _config.Folders
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        progress?.Report(new ScanProgress("正在查找音频文件", 0, 0));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            foreach (var file in EnumerateAudioFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 多个扫描目录可能互相嵌套，去重避免重复入库。
                if (!seen.Add(file))
                {
                    continue;
                }

                var track = TryReadTrack(file);
                if (track is not null)
                {
                    tracks.Add(track);
                }

                if (tracks.Count % 20 == 0)
                {
                    progress?.Report(new ScanProgress("正在读取标签", tracks.Count, 0));
                }
            }
        }

        progress?.Report(new ScanProgress("扫描完成", tracks.Count, tracks.Count));
        return tracks;
    }

    /// <summary>迭代式遍历，避免深层目录递归爆栈，并在遍历时即按扩展名过滤。</summary>
    private static IEnumerable<string> EnumerateAudioFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var directory = stack.Pop();

            string[] subDirectories;
            string[] files;
            try
            {
                subDirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception)
            {
                // 无权限或已被删除的目录直接跳过。
                continue;
            }

            foreach (var file in files)
            {
                if (AudioFileTypes.IsAudioFile(file))
                {
                    yield return file;
                }
            }

            foreach (var subDirectory in subDirectories)
            {
                stack.Push(subDirectory);
            }
        }
    }

    private Track? TryReadTrack(string path)
    {
        var id = TrackKey.Create(_config.Id, path);
        FileInfo info = new(path);

        var track = new Track
        {
            Id = id,
            SourceId = _config.Id,
            SourceType = MusicSourceType.Local,
            Path = path,
            FileSize = info.Exists ? info.Length : 0,
        };

        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;

            track.Title = tag.Title ?? string.Empty;
            track.Artist = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? string.Empty;
            track.Album = tag.Album ?? string.Empty;
            track.Genre = tag.FirstGenre ?? string.Empty;
            track.Year = (int)tag.Year;
            track.TrackNumber = (int)tag.Track;
            track.DurationSeconds = file.Properties?.Duration.TotalSeconds ?? 0;
            track.CoverPath = AudioTagWriter.ExportCover(id, tag.Pictures);
        }
        catch (Exception)
        {
            // 标签损坏或格式不受支持时退化为按文件名展示，不影响入库。
        }

        if (string.IsNullOrWhiteSpace(track.Title))
        {
            track.Title = Path.GetFileNameWithoutExtension(path);
        }

        return track;
    }
}
