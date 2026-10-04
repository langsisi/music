using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Models;
using Music.Services.Online;

namespace Music.ViewModels;

/// <summary>
/// 承载在线曲目行的宿主页面（发现页 / 首页）。行视图模型不直接依赖具体页面，
/// 搜索结果与首页推荐才能复用同一套模板与命令。
/// </summary>
public interface IOnlineTrackHost
{
    /// <summary>是否支持下载：发现页可以下载到指定音源，首页推荐只负责播放。</summary>
    bool SupportsDownload { get; }

    /// <summary>以所在列表为播放队列，从被点击的一行开始播放。</summary>
    Task PlayFromAsync(IList<OnlineTrackViewModel> list, OnlineTrackViewModel item);

    /// <summary>把一行曲目下载到宿主选定的目标与音质。</summary>
    Task DownloadAsync(OnlineTrackViewModel item);
}

/// <summary>
/// 一行在线曲目：播放 / 下载命令挂在自己身上，实际动作交给所属的 <see cref="IOnlineTrackHost"/> 执行。
/// </summary>
public partial class OnlineTrackViewModel : ObservableObject
{
    private readonly IOnlineTrackHost _host;
    private readonly IList<OnlineTrackViewModel> _siblings;

    public OnlineTrackViewModel(
        OnlineTrack track,
        IOnlineTrackHost host,
        IList<OnlineTrackViewModel> siblings)
    {
        Track = track;
        _host = host;
        _siblings = siblings;
    }

    public OnlineTrack Track { get; }

    public string DisplayTitle => Track.DisplayTitle;

    public string Subtitle => Track.Subtitle;

    public string SourceName => Track.SourceName;

    /// <summary>列表缩略图，加载完成后才绑定显示。</summary>
    [ObservableProperty]
    private Bitmap? _cover;

    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>下载状态文案：空 = 未下载；「下载中… 42%」/「已下载」/「失败」。</summary>
    [ObservableProperty]
    private string _downloadText = string.Empty;

    public bool HasDownloadText => !string.IsNullOrEmpty(DownloadText);

    partial void OnDownloadTextChanged(string value) => OnPropertyChanged(nameof(HasDownloadText));

    [RelayCommand]
    private Task Play() => _host.PlayFromAsync(_siblings, this);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task Download() => _host.DownloadAsync(this);

    private bool CanDownload() => _host.SupportsDownload;

    // ---------------- 共用工具 ----------------

    /// <summary>把一批在线曲目填充成行（清空后重建），目标集合自身即播放队列。</summary>
    public static void Populate(
        IList<OnlineTrackViewModel> target,
        IReadOnlyList<OnlineTrack> tracks,
        IOnlineTrackHost host)
    {
        target.Clear();
        foreach (var track in tracks)
        {
            target.Add(new OnlineTrackViewModel(track, host, target));
        }
    }

    /// <summary>逐行补封面缩略图；失败或没有封面时留空（显示默认底色）。</summary>
    public static async Task LoadCoversAsync(
        IReadOnlyList<OnlineTrackViewModel> rows,
        OnlineMusicService music,
        int size = 120)
    {
        foreach (var row in rows)
        {
            if (row.Cover is not null)
            {
                continue;
            }

            try
            {
                var bytes = await music.GetCoverBytesAsync(row.Track, size).ConfigureAwait(true);
                if (bytes is { Length: > 0 })
                {
                    using var stream = new MemoryStream(bytes);
                    row.Cover = new Bitmap(stream);
                }
            }
            catch (Exception)
            {
                // 缩略图解码失败不影响列表。
            }
        }
    }
}