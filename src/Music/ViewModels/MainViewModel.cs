using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Music.Controls;
using Music.Models;
using System.Collections.ObjectModel;

namespace Music.ViewModels;

/// <summary>
/// 应用外壳：负责导航、自适应布局（桌面 / 紧凑）与全屏播放页的开关。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>与 Tokens.axaml 中的 CompactBreakpoint 保持一致。</summary>
    private const double CompactBreakpoint = 900;

    public MainViewModel(
        PlayerViewModel player,
        Pages.HomeViewModel home,
        Pages.LibraryViewModel library,
        Pages.SourcesViewModel sources,
        Pages.SettingsViewModel settings)
    {
        Player = player;

        NavItems =
        [
            new NavigationItem { Title = "首页", Icon = AppIcons.Home, Page = home },
            new NavigationItem { Title = "音乐库", Icon = AppIcons.Library, Page = library },
            new NavigationItem { Title = "音源", Icon = AppIcons.Sources, Page = sources },
            new NavigationItem { Title = "设置", Icon = AppIcons.Settings, Page = settings },
        ];

        SelectedNavItem = NavItems[0];

        player.ExpandRequested += () => IsNowPlayingOpen = true;
        player.CollapseRequested += () => IsNowPlayingOpen = false;
    }

    public PlayerViewModel Player { get; }

    public ObservableCollection<NavigationItem> NavItems { get; }

    [ObservableProperty]
    public partial NavigationItem? SelectedNavItem { get; set; }

    partial void OnSelectedNavItemChanged(NavigationItem? value)
        => OnPropertyChanged(nameof(CurrentPage));

    /// <summary>当前页面，交给 ViewLocator 解析出对应 View。</summary>
    public Pages.PageViewModel? CurrentPage => SelectedNavItem?.Page;

    /// <summary>紧凑布局（窄屏 / 手机）：隐藏左侧导航，改用底部标签栏。</summary>
    [ObservableProperty]
    public partial bool IsCompact { get; set; }

    partial void OnIsCompactChanged(bool value) => OnPropertyChanged(nameof(IsWide));

    public bool IsWide => !IsCompact;

    /// <summary>全屏「正在播放」页是否展开。</summary>
    [ObservableProperty]
    public partial bool IsNowPlayingOpen { get; set; }

    /// <summary>由 View 在尺寸变化时调用。</summary>
    public void UpdateLayout(double width) => IsCompact = width < CompactBreakpoint;
}
