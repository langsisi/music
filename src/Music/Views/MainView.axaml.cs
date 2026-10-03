using Avalonia.Controls;
using Music.ViewModels;

namespace Music.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
        DataContextChanged += (_, _) => ApplyLayout();
    }

    /// <summary>把实际宽度交给外壳，由它决定使用桌面布局还是紧凑布局。</summary>
    private void ApplyLayout()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateLayout(Bounds.Width);
        }
    }
}
