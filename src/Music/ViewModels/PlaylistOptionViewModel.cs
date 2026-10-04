using CommunityToolkit.Mvvm.ComponentModel;
using Music.Models;

namespace Music.ViewModels;

/// <summary>「添加到歌单」弹层中的一行：歌单（分类）名 + 当前曲目是否已在此歌单。</summary>
public partial class PlaylistOptionViewModel : ObservableObject
{
    public PlaylistOptionViewModel(Category category, bool isMember)
    {
        Id = category.Id;
        Name = category.Name;
        IsMember = isMember;
    }

    public string Id { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isMember;
}