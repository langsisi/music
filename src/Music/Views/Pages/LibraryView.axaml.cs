using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Music.ViewModels;
using Music.ViewModels.Pages;

namespace Music.Views.Pages;

public partial class LibraryView : UserControl
{
    /// <summary>左滑露出的删除按钮宽度（需与 XAML 中保持一致）。</summary>
    private const double RevealWidth = 76;

    /// <summary>判定为滑动的水平位移阈值；小于它按普通点击处理，避免误伤行内按钮。</summary>
    private const double DragThreshold = 8;

    /// <summary>当前已滑开、露出删除按钮的那一行内容层。</summary>
    private Control? _openContent;
    private TranslateTransform? _openTransform;

    private Control? _dragContent;
    private TranslateTransform? _dragTransform;
    private Point _origin;
    private double _startOffset;
    private bool _dragging;

    public LibraryView() => InitializeComponent();

    /// <summary>只有手机（紧凑）布局且该行可删除时才允许左滑。</summary>
    private bool CanSwipe(Control content)
        => DataContext is LibraryViewModel { IsCompact: true }
            && content.DataContext is TrackRowViewModel { CanDelete: true };

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 一旦在别处按下，就先把已滑开的行收回去（同一时刻只允许一行展开）。
        CloseOpenRow();

        if (sender is not Control content || !CanSwipe(content))
        {
            return;
        }

        _dragContent = content;
        _dragTransform = content.RenderTransform as TranslateTransform;
        _origin = e.GetPosition(this);
        _startOffset = _dragTransform?.X ?? 0;
        _dragging = false;
    }

    private void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragContent is null || _dragTransform is null)
        {
            return;
        }

        var delta = e.GetPosition(this) - _origin;

        if (!_dragging)
        {
            // 垂直位移更大时交给列表滚动，不抢横向滑动。
            if (Math.Abs(delta.X) < DragThreshold || Math.Abs(delta.X) <= Math.Abs(delta.Y))
            {
                return;
            }

            _dragging = true;
            CloseOpenRow();
            e.Pointer.Capture(_dragContent);
        }

        _dragTransform.X = Math.Clamp(_startOffset + delta.X, -RevealWidth, 0);
        e.Handled = true;
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var content = _dragContent;
        var transform = _dragTransform;
        _dragContent = null;
        _dragTransform = null;

        if (content is null || transform is null || !_dragging)
        {
            _dragging = false;
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (transform.X <= -RevealWidth / 2)
        {
            transform.X = -RevealWidth;
            _openContent = content;
            _openTransform = transform;
        }
        else
        {
            transform.X = 0;

            if (ReferenceEquals(_openContent, content))
            {
                _openContent = null;
                _openTransform = null;
            }
        }
    }

    /// <summary>把已滑开的行收回去（同一时刻只允许一行处于展开状态）。</summary>
    private void CloseOpenRow()
    {
        if (_openTransform is null)
        {
            return;
        }

        _openTransform.X = 0;
        _openContent = null;
        _openTransform = null;
    }
}
