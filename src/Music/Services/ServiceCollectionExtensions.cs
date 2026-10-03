using Microsoft.Extensions.DependencyInjection;
using Music.Services.Audio;
using Music.Services.Broadcast;
using Music.Services.Cache;
using Music.Services.Dialogs;
using Music.Services.Ftp;
using Music.Services.Library;
using Music.Services.Lyrics;
using Music.Services.Media;
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

        // 音源
        services.AddSingleton<IFtpFileClientFactory, FluentFtpFileClientFactory>();
        services.AddSingleton<IMusicSourceFactory, MusicSourceFactory>();
        services.AddSingleton<LibrarySyncService>();

        // 播放引擎与缓存
        services.AddSingleton(new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromMinutes(5) });
        services.AddSingleton<IAudioCache, SqliteAudioCache>();
        services.AddSingleton<IAudioPlayer, VlcAudioPlayer>();
        services.AddSingleton<IMediaResolver, CachedMediaResolver>();
        services.AddSingleton<PlaybackService>();

        // 歌词、广播与系统媒体信息
        services.AddSingleton<ILyricsProvider, LocalLrcProvider>();
        services.AddSingleton<ILyricsProvider, NavidromeLyricsProvider>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<LyricsBroadcastServer>();
        services.AddSingleton<ISystemMediaService, NoopSystemMediaService>();

        // 在线升级
        services.AddSingleton<UpdateService>();

        // 页面
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SourcesViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // 播放器与外壳
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
