namespace Music.ViewModels.Pages;

/// <summary>所有导航页面的基类。命名约定：<c>XxxViewModel</c> 对应 <c>Views.Pages.XxxView</c>。</summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    public virtual string Description => string.Empty;
}
