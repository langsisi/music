using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Models;
using Music.Services;
using Music.Services.Broadcast;
using Music.Services.Cache;
using Music.Services.Update;

namespace Music.ViewModels.Pages;

public partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IAudioCache _cache;
    private readonly LyricsBroadcastServer _broadcast;
    private readonly UpdateService _updates;

    /// <summary>构造期间赋值不应触发回写。</summary>
    private bool _loading;

    public SettingsViewModel(
        ISettingsStore settings,
        IAudioCache cache,
        LyricsBroadcastServer broadcast,
        UpdateService updates)
    {
        _settings = settings;
        _cache = cache;
        _broadcast = broadcast;
        _updates = updates;

        ThemeOptions =
        [
            new ThemeOption(AppThemeMode.Dark, "深色"),
            new ThemeOption(AppThemeMode.Light, "浅色"),
            new ThemeOption(AppThemeMode.System, "跟随系统"),
        ];

        _loading = true;
        SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Mode == settings.Current.ThemeMode)
                        ?? ThemeOptions[0];
        CacheLimitMb = settings.Current.CacheLimitMb;
        BroadcastEnabled = settings.Current.BroadcastEnabled;
        BroadcastPort = settings.Current.BroadcastPort;
        UpdateFeedUrl = settings.Current.UpdateFeedUrl;
        _loading = false;

        _ = RefreshCacheStatsAsync();
        ApplyBroadcast();
    }

    public override string Title => "设置";

    // ---------------- 外观 ----------------

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        ThemeService.Apply(value.Mode);
        if (_loading)
        {
            return;
        }

        _settings.Current.ThemeMode = value.Mode;
        Save();
    }

    // ---------------- 缓存 ----------------

    /// <summary>缓存上限（MB），0 表示不缓存。</summary>
    [ObservableProperty]
    public partial double CacheLimitMb { get; set; }

    partial void OnCacheLimitMbChanged(double value)
    {
        OnPropertyChanged(nameof(CacheLimitText));
        if (_loading)
        {
            return;
        }

        _settings.Current.CacheLimitMb = value;
        Save();

        // 调小上限时立即生效，不需要等下次播放。
        _ = ApplyLimitAsync();
    }

    public string CacheLimitText => CacheLimitMb <= 0
        ? "不缓存"
        : $"{CacheLimitMb / 1024.0:0.##} GB";

    [ObservableProperty]
    public partial string CacheUsageText { get; set; } = "统计中…";

    [RelayCommand]
    private async Task ClearCache()
    {
        await _cache.ClearAsync();
        await RefreshCacheStatsAsync();
    }

    [RelayCommand]
    private async Task RefreshCacheStatsAsync()
    {
        var stats = await _cache.GetStatsAsync();
        CacheUsageText = stats.FileCount == 0
            ? "暂无缓存文件"
            : $"已用 {stats.SizeText} · {stats.FileCount} 个文件";
    }

    // ---------------- 歌词广播 ----------------

    /// <summary>局域网歌词广播：手机连蓝牙音箱时打开该网址即可看滚动歌词。</summary>
    [ObservableProperty]
    public partial bool BroadcastEnabled { get; set; }

    partial void OnBroadcastEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.BroadcastEnabled = value;
        Save();
        ApplyBroadcast();
    }

    [ObservableProperty]
    public partial decimal? BroadcastPort { get; set; }

    partial void OnBroadcastPortChanged(decimal? value)
    {
        if (_loading)
        {
            return;
        }

        var port = NormalizePort(value);
        if (port == _settings.Current.BroadcastPort)
        {
            return;
        }

        _settings.Current.BroadcastPort = port;
        Save();
        ApplyBroadcast();
    }

    [ObservableProperty]
    public partial string BroadcastStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BroadcastUrlText { get; set; } = string.Empty;

    public string VersionText => UpdateService.CurrentVersionText;

    // ---------------- 在线升级 ----------------

    /// <summary>版本清单地址，返回形如 {"version":"1.1.0","url":"...","sha256":"..."} 的 JSON。</summary>
    [ObservableProperty]
    public partial string UpdateFeedUrl { get; set; } = string.Empty;

    partial void OnUpdateFeedUrlChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.UpdateFeedUrl = value?.Trim() ?? string.Empty;
        Save();
    }

    [ObservableProperty]
    public partial string UpdateStatusText { get; set; } = "尚未检查更新。";

    [ObservableProperty]
    public partial bool IsCheckingUpdate { get; set; }

    /// <summary>检查到新版本后才有值，用于显示「下载并安装」。</summary>
    [ObservableProperty]
    public partial UpdateManifest? PendingUpdate { get; set; }

    partial void OnPendingUpdateChanged(UpdateManifest? value)
        => OnPropertyChanged(nameof(HasPendingUpdate));

    public bool HasPendingUpdate => PendingUpdate is not null;

    /// <summary>Android 等平台不能自我覆盖安装。</summary>
    public bool CanSelfUpdate => UpdateService.CanSelfUpdate;

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        PendingUpdate = null;
        UpdateStatusText = "正在检查更新…";

        try
        {
            var result = await _updates.CheckAsync();
            PendingUpdate = result.Manifest;
            UpdateStatusText = result.Message;
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (PendingUpdate is not { } manifest)
        {
            return;
        }

        IsCheckingUpdate = true;
        try
        {
            var progress = new Progress<double>(value =>
                UpdateStatusText = $"正在下载新版本… {value * 100:0}%");

            var package = await _updates.DownloadAsync(manifest, progress);

            UpdateStatusText = "下载完成，正在重启应用以完成升级…";
            _updates.ApplyAndRestart(package);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"升级失败：{ex.Message}";
            IsCheckingUpdate = false;
        }
    }

    private void ApplyBroadcast()
    {
        if (BroadcastEnabled)
        {
            _broadcast.Start(NormalizePort(BroadcastPort));
        }
        else
        {
            _broadcast.Stop();
        }

        BroadcastStatusText = _broadcast.IsRunning
            ? "运行中，用手机浏览器打开下面的地址即可跟随显示歌词"
            : "已关闭";

        BroadcastUrlText = _broadcast.IsRunning
            ? string.Join("\n", _broadcast.Urls)
            : string.Empty;
    }

    private static int NormalizePort(decimal? value)
    {
        var port = value is null ? 8765 : (int)decimal.Round(value.Value);
        return Math.Clamp(port, 1, 65535);
    }

    private async Task ApplyLimitAsync()
    {
        await _cache.EvictAsync();
        await RefreshCacheStatsAsync();
    }

    /// <summary>保存失败不应打断界面操作。</summary>
    private void Save() => _ = SaveCoreAsync();

    private async Task SaveCoreAsync()
    {
        try
        {
            await _settings.SaveAsync();
        }
        catch (Exception)
        {
            // 忽略写入失败。
        }
    }
}
