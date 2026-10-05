using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Music.Models;

/// <summary>应用设置，持久化到 <c>settings.json</c>。</summary>
public sealed class AppSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;

    /// <summary>音频缓存上限（MB），0 表示不缓存。</summary>
    public double CacheLimitMb { get; set; } = 2048;

    public double Volume { get; set; } = 80;

    /// <summary>是否开启歌词广播服务（局域网内手机/外部设备可访问）。</summary>
    public bool BroadcastEnabled { get; set; } = true;

    public int BroadcastPort { get; set; } = 8765;

    [JsonPropertyName("sources")]
    public ObservableCollection<MusicSourceConfig> Sources { get; set; } = [];

    /// <summary>上次播放的曲目 Id，用于启动时恢复。</summary>
    public string? LastTrackId { get; set; }

    /// <summary>
    /// 是否已把平台注入的默认目录（Android 的本机音乐 / 下载目录）播种为本地音源。
    /// 只播种一次，用户手动删除后不再自动加回来。
    /// </summary>
    public bool DefaultLocalFoldersSeeded { get; set; }
}
