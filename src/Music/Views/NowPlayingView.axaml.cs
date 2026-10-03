using System;
using System.ComponentModel;
using Avalonia.Controls;
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

    /// <summary>当前歌词行变化时把它滚动到视野中间。</summary>
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

        LyricsList.ScrollIntoView(_player.LyricLines[index]);
    }
}
