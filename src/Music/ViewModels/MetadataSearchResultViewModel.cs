using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Music.Services.Metadata;

namespace Music.ViewModels;

/// <summary>「歌词搜索」结果列表中的一条候选：展示信息 + 选中态 + 异步加载的封面缩略图。</summary>
public partial class MetadataSearchResultViewModel : ObservableObject
{
    public MetadataSearchResultViewModel(MetadataCandidate candidate)
    {
        Candidate = candidate;

        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(candidate.Artist))
        {
            parts.Add(candidate.Artist);
        }

        if (!string.IsNullOrWhiteSpace(candidate.Album))
        {
            parts.Add(candidate.Album);
        }

        Subtitle = string.Join(" · ", parts);
    }

    public MetadataCandidate Candidate { get; }

    public string Title => Candidate.Title;

    public string Subtitle { get; }

    /// <summary>有无歌词的角标文案。</summary>
    public string LyricsBadge => Candidate.HasLyrics ? "歌词" : "无歌词";

    [ObservableProperty]
    private Bitmap? _cover;

    [ObservableProperty]
    private bool _isSelected;
}