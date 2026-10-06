using Avalonia.Controls;

namespace Music.Views.Pages;

public partial class DiscoverView : UserControl
{
    /// <summary>
    /// 页面内容宽度低于此值时改用窄屏排布（搜索按钮换行、下拉竖排并撑满）。
    /// 阈值取在窗口紧凑断点（900）与手机内容宽度之间，随页面自身可用宽度判断更准确。
    /// </summary>
    private const double NarrowBreakpoint = 600;

    public DiscoverView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var isNarrow = e.NewSize.Width < NarrowBreakpoint;

        RootBorder.Classes.Set("narrow", isNarrow);

        // UniformGrid.Columns 不响应样式设置器，只能在这里直接赋值。
        OptionsGrid.Columns = isNarrow ? 1 : 3;
    }
}