using Microsoft.Extensions.DependencyInjection;
using Music.Services.Audio;
using Music.Services.Broadcast;
using Music.Services.Cache;
using Music.Services.Dialogs;
using Music.Services.Library;
using Music.Services.Lyrics;
using Music.Services.Media;
using Music.Services.Metadata;
using Music.Services.Metadata.Providers;
using Music.Services.Online;
using Music.Services.Remote;
using Music.Services.Security;
using Music.Services.Sources;
using Music.Services.SystemMedia;
using Music.Services.Update;
using Music.ViewModels;
using Music.ViewModels.Pages;

namespace Music.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册所有与平台无关的 Core 服务与视图模型。
    /// 平台相关实现由各 head 在 <see cref="AppHost.Configure"/> 中追加注册以覆盖默认实现。
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // 基础设施
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ILibraryStore, SqliteLibraryStore>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        // 默认不加密；平台 head 会覆盖成 Windows DPAPI / Android Keystore 实现。
        services.AddSingleton<ISecretProtector, IdentitySecretProtector>();

        // 音源
        // 远程文件客户端用独立 HttpClient：不设 5 分钟超时，避免大文件上传被中断。
        // 显式开启自动解压：移动网络链路上的网关/代理可能对响应做 gzip 压缩，默认的不解压会让
        // WebDAV 的 XML 正文变成压缩字节（表现为 “0x1F is an invalid character”）。
        services.AddSingleton<IRemoteFileClientFactory>(_ =>
            new RemoteFileClientFactory(new System.Net.Http.HttpClient(
                new System.Net.Http.SocketsHttpHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                })
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            }));
        services.AddSingleton<IMusicSourceFactory, MusicSourceFactory>();
        services.AddSingleton<LibrarySyncService>();

        // 播放引擎与缓存
        services.AddSingleton(new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromMinutes(5) });
        services.AddSingleton<IAudioCache, SqliteAudioCache>();
        services.AddSingleton<IAudioPlayer, VlcAudioPlayer>();
        // 本地 HTTP 代理：FTP/SMB/WebDAV 曲目边下边播（不可用时自动回退到整文件下载）。
        services.AddSingleton<LocalMediaProxy>();
        services.AddSingleton<IMediaResolver, CachedMediaResolver>();
        services.AddSingleton<PlaybackService>();

        // 歌词、广播与系统媒体信息
        // 顺序即优先级：本地同名 .lrc / 内嵌歌词 → 在线刮削缓存 → Navidrome 远程。
        services.AddSingleton<ILyricsProvider, LocalLrcProvider>();
        services.AddSingleton<ILyricsProvider, ScrapedLrcProvider>();
        services.AddSingleton<ILyricsProvider, NavidromeLyricsProvider>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<LyricsBroadcastServer>();
        services.AddSingleton<ISystemMediaService, NoopSystemMediaService>();

        // 在线元数据刮削（封面 / 歌词）；注册顺序即设置页下拉顺序。
        services.AddSingleton<MetadataHttpClient>();
        services.AddSingleton<IMetadataProvider, NeteaseMetadataProvider>();
        services.AddSingleton<IMetadataProvider, QqMetadataProvider>();
        services.AddSingleton<IMetadataProvider, KugouMetadataProvider>();
        services.AddSingleton<IMetadataProvider, KuwoMetadataProvider>();
        services.AddSingleton<IMetadataProvider, MiguMetadataProvider>();
        services.AddSingleton<IMetadataProvider, ItunesMetadataProvider>();
        services.AddSingleton<SourceWriteBackService>();
        services.AddSingleton<RemoteMetadataEnricher>();
        services.AddSingleton<MetadataScrapeService>();

        // 在线升级
        // 默认不支持自更新；平台 head 会覆盖成 Windows / Android 安装器。
        services.AddSingleton<IUpdateInstaller, UnsupportedUpdateInstaller>();
        services.AddSingleton<UpdateService>();

        // 在线发现（GD 音乐台）：搜索 / 推荐 + 下载到指定音源
        services.AddSingleton<GdMusicClient>();
        services.AddSingleton<OnlineMusicService>();
        services.AddSingleton<OnlineDownloadService>();

        // 页面
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<DiscoverViewModel>();
        services.AddSingleton<SourcesViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // 播放器与外壳
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
