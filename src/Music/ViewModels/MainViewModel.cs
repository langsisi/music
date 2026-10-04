using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Music.Controls;
using Music.Models;
using System.Collections.ObjectModel;
using System.Linq;

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
        Pages.DiscoverViewModel discover,
        Pages.SettingsViewModel settings)
    {
        Player = player;

        NavItems =
        [
            new NavigationItem { Title = "首页", Icon = AppIcons.Home, Page = home },
            new NavigationItem { Title = "音乐库", Icon = AppIcons.Library, Page = library },
            new NavigationItem { Title = "发现", Icon = AppIcons.Explore, Page = discover },
            new NavigationItem { Title = "设置", Icon = AppIcons.Settings, Page = settings },
        ];

        SelectedNavItem = NavItems[0];

        player.ExpandRequested += () => IsNowPlayingOpen = true;
        player.CollapseRequested += () => IsNowPlayingOpen = false;

        // 首页歌单卡片点击后切到音乐库并应用对应筛选。
        home.FavoritesRequested += () =>
        {
            library.SelectFavorites();
            SelectedNavItem = NavItems.First(item => item.Page == library);
        };
        home.CategoryRequested += categoryId =>
        {
            library.SelectCategory(categoryId);
            SelectedNavItem = NavItems.First(item => item.Page == library);
        };
    }

    public PlayerViewModel Player { get; }

    public ObservableCollection<NavigationItem> NavItems { get; }

    [ObservableProperty]
    private NavigationItem? _selectedNavItem;

    partial void OnSelectedNavItemChanged(NavigationItem? value)
    {
        // 发现页首次进入时刷新可用的下载目标（设置里增删音源后回来即为最新）。
        if (value?.Page is Pages.DiscoverViewModel discover)
        {
            discover.Activate();
        }

        OnPropertyChanged(nameof(CurrentPage));
    }

    /// <summary>当前页面，交给 ViewLocator 解析出对应 View。</summary>
    public Pages.PageViewModel? CurrentPage => SelectedNavItem?.Page;

    /// <summary>紧凑布局（窄屏 / 手机）：隐藏左侧导航，改用底部标签栏。</summary>
    [ObservableProperty]
    private bool _isCompact;

    partial void OnIsCompactChanged(bool value) => OnPropertyChanged(nameof(IsWide));

    public bool IsWide => !IsCompact;

    /// <summary>全屏「正在播放」页是否展开。</summary>
    [ObservableProperty]
    private bool _isNowPlayingOpen;

    /// <summary>由 View 在尺寸变化时调用；同时把断点推给各页面，供行内布局使用。</summary>
    public void UpdateLayout(double width)
    {
        var compact = width < CompactBreakpoint;
        IsCompact = compact;

        foreach (var item in NavItems)
        {
            if (item.Page is { } page)
            {
                page.IsWide = !compact;
            }
        }
    }
}
