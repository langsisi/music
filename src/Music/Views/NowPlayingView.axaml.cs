using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Music.ViewModels;

namespace Music.Views;

public partial class NowPlayingView : UserControl
{
    private PlayerViewModel? _player;

    public NowPlayingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
        }

        _player = (DataContext as MainViewModel)?.Player;

        if (_player is not null)
        {
            _player.PropertyChanged += OnPlayerPropertyChanged;
        }
    }

    /// <summary>当前歌词行变化时把它滚动到歌词区正中间。</summary>
    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.CurrentLyricIndex) || _player is null)
        {
            return;
        }

        var index = _player.CurrentLyricIndex;
        if (index < 0 || index >= _player.LyricLines.Count)
        {
            return;
        }

        // 目标行在视野外（拖动进度条跳转）时容器尚未生成，先滚进视野，等布局完成后再定位。
        if (LyricsList.ContainerFromIndex(index) is null)
        {
            LyricsList.ScrollIntoView(_player.LyricLines[index]);
            Dispatcher.UIThread.Post(() => CenterLyric(index), DispatcherPriority.Loaded);
            return;
        }

        CenterLyric(index);
    }

    /// <summary>把指定索引的歌词行滚动到歌词区垂直居中位置。</summary>
    private void CenterLyric(int index)
    {
        if (LyricsList.FindDescendantOfType<ScrollViewer>() is not { } scrollViewer)
        {
            return;
        }

        // 上下留白变化会触发重新布局，等布局完成后按新位置再定位一次。
        if (EnsureCenteringPadding(scrollViewer))
        {
            Dispatcher.UIThread.Post(() => CenterLyric(index), DispatcherPriority.Loaded);
            return;
        }

        if (LyricsList.ContainerFromIndex(index) is not Control container
            || container.TranslatePoint(new Point(0, 0), scrollViewer) is not { } topLeft)
        {
            return;
        }

        // 让该行中心对齐视口中心所需的偏移，并夹在可滚动范围内。
        var centered = scrollViewer.Offset.Y
                       + topLeft.Y
                       + container.Bounds.Height / 2
                       - scrollViewer.Viewport.Height / 2;
        var max = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Clamp(centered, 0, max));
    }

    /// <summary>给歌词列表上下各留半个视口高度的留白，使首尾歌词行同样能滚到正中；返回是否发生了调整。</summary>
    private bool EnsureCenteringPadding(ScrollViewer scrollViewer)
    {
        if (LyricsList.ItemsPanelRoot is not { } panel || scrollViewer.Viewport.Height <= 0)
        {
            return false;
        }

        var pad = scrollViewer.Viewport.Height / 2;
        if (Math.Abs(panel.Margin.Top - pad) < 0.5)
        {
            return false;
        }

        panel.Margin = new Thickness(0, pad, 0, pad);
        return true;
    }
}