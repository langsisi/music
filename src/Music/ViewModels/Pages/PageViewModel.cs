using CommunityToolkit.Mvvm.ComponentModel;

namespace Music.ViewModels.Pages;

/// <summary>所有导航页面的基类。命名约定：<c>XxxViewModel</c> 对应 <c>Views.Pages.XxxView</c>。</summary>
public abstract partial class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    public virtual string Description => string.Empty;

    /// <summary>
    /// 宽屏（桌面）布局。由外壳在尺寸变化时写入，供页面按断点决定行内操作是否常驻显示
    /// （例如手机端用左滑露出删除按钮，桌面端直接显示）。
    /// </summary>
    [ObservableProperty]
    private bool _isWide = true;

    public bool IsCompact => !IsWide;

    partial void OnIsWideChanged(bool value) => OnPropertyChanged(nameof(IsCompact));
}
