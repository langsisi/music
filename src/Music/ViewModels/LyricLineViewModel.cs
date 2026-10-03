using CommunityToolkit.Mvvm.ComponentModel;

namespace Music.ViewModels;

/// <summary>歌词列表中的一行。用列表选中态表达「当前行」高亮。</summary>
public partial class LyricLineViewModel : ViewModelBase
{
    public LyricLineViewModel(string text) => Text = text;

    public string Text { get; }
}
