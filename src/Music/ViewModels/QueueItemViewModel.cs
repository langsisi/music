using CommunityToolkit.Mvvm.ComponentModel;
using Music.Models;

namespace Music.ViewModels;

/// <summary>「正在播放列表」中的一行，承载队列位置与曲目信息。</summary>
public partial class QueueItemViewModel : ObservableObject
{
    public QueueItemViewModel(Track track, int index)
    {
        Track = track;
        Index = index;
    }

    public Track Track { get; }

    /// <summary>在播放队列中的位置，用于点击跳转。</summary>
    public int Index { get; }

    public string DisplayTitle => Track.DisplayTitle;

    public string Subtitle => Track.Subtitle;

    public string? CoverPath => Track.CoverPath;

    public string DurationText => Track.DurationText;

    /// <summary>是否为当前正在播放的曲目，用于高亮。</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
