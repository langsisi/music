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

    /// <summary>在线升级的版本清单地址（返回 <see cref="UpdateManifest"/> 的 JSON）。为空表示不检查更新。</summary>
    public string UpdateFeedUrl { get; set; } = string.Empty;

    [JsonPropertyName("sources")]
    public ObservableCollection<MusicSourceConfig> Sources { get; set; } = [];

    /// <summary>上次播放的曲目 Id，用于启动时恢复。</summary>
    public string? LastTrackId { get; set; }
}
