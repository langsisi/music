using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace Music.Controls;

/// <summary>
/// 单行文本控件：内容超出可用宽度时左右往返滚动（滚到尾部再滚回开头），长度够时与普通
/// 文本表现一致。
/// 关键点：文本用「无限宽度」的 <see cref="TextLayout"/> 自行绘制，整段文字都会被画出来，
/// 平移才能把尾部带进可见区域；控件只上报可用宽度、不会撑宽父容器，溢出部分由外层的
/// <c>ClipToBounds</c> 裁掉，因此外层容器需要开启裁剪。
/// </summary>
public class MarqueeText : Control
{
    /// <summary>滚动速度（像素/秒）。</summary>
    private const double Speed = 38;

    /// <summary>每一端的停顿时长（秒）。</summary>
    private const double HoldSeconds = 0.7;

    /// <summary>单程时长下限（秒），避免短距离滚动过快。</summary>
    private const double MinTravelSeconds = 0.9;

    /// <summary>单程时长上限（秒），避免超长文本单程滚得太久。</summary>
    private const double MaxTravelSeconds = 4.0;

    /// <summary>每帧时长（秒），需与下方定时器间隔一致。</summary>
    private const double FrameSeconds = 0.033;

    /// <summary>正在滚动的实例，共用一个定时器。</summary>
    private static readonly List<MarqueeText> Scrolling = [];

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(FrameSeconds) };

    public static readonly StyledProperty<string?> TextProperty =
        TextBlock.TextProperty.AddOwner<MarqueeText>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<MarqueeText>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<MarqueeText>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<MarqueeText>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<MarqueeText>();

    private readonly TranslateTransform _translate = new();

    /// <summary>按完整文字（不限宽）排版的结果，一直复用，避免每帧重建。</summary>
    private TextLayout? _layout;

    /// <summary>文本在不受限宽度下的完整宽度。</summary>
    private double _naturalWidth;

    /// <summary>可见区域宽度（外层容器给到的裁剪宽度）。</summary>
    private double _available;

    /// <summary>需要平移的距离（文本宽度 - 可见宽度）。</summary>
    private double _distance;

    private double _elapsed;
    private bool _isScrolling;

    static MarqueeText()
    {
        Timer.Tick += (_, _) => AdvanceAll();
    }

    public MarqueeText()
    {
        // 渲染宽度会超过可用宽度，不能自我裁剪，交给外层容器裁。
        ClipToBounds = false;
        RenderTransform = _translate;

        AttachedToVisualTree += (_, _) => Update();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _layout ??= new TextLayout(
            Text ?? string.Empty,
            new Typeface(FontFamily, FontStyle.Normal, FontWeight),
            FontSize,
            Foreground ?? Brushes.Black,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            maxWidth: double.PositiveInfinity);

        _naturalWidth = _layout.Width;
        var natural = new Size(_layout.Width, _layout.Height);

        if (double.IsInfinity(availableSize.Width))
        {
            return natural;
        }

        // 只上报可用宽度，长文本不会把父容器撑宽。
        return new Size(Math.Min(natural.Width, Math.Max(0, availableSize.Width)), natural.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _available = finalSize.Width;
        Update();
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (_layout is null)
        {
            return;
        }

        // 这里画的是完整文字（可能宽于可见区域），平移由 RenderTransform 完成，
        // 超出部分由外层 ClipToBounds 裁掉。
        _layout.Draw(context, new Point(0, 0));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty
            || change.Property == FontFamilyProperty
            || change.Property == FontSizeProperty
            || change.Property == FontWeightProperty
            || change.Property == ForegroundProperty)
        {
            // 排版结果要按新样式重建，并等这一轮布局跑完才能拿到新的完整宽度。
            _layout = null;
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (change.Property == IsVisibleProperty)
        {
            Update();
        }
    }

    /// <summary>按「文本宽度 vs 可见宽度」决定是否滚动。</summary>
    private void Update()
    {
        var available = _available;

        // 可见宽度是按布局推算出来的，可能比真实裁剪区略大；把行程放大 1/3 并额外留
        // 一点余量，宁可多滚一点，也要保证文字尾部一定被带进可见区域。
        var distance = (_naturalWidth - available) * 4.0 / 3.0 + available * 0.2;

        // 宽屏/紧凑两套布局共用同一个模板，隐藏的那一份不必滚动。
        if (!IsVisible || available <= 1 || distance <= 1)
        {
            Stop();
            return;
        }

        // 已在滚动时只跟随布局更新滚动距离（过滤亚像素抖动），绝不重置
        // _elapsed / 平移量，否则动画会被反复打断、走不到回滚相位。
        if (_isScrolling)
        {
            if (Math.Abs(distance - _distance) > 2)
            {
                _distance = distance;
            }

            return;
        }

        _distance = distance;
        _elapsed = 0;
        _translate.X = 0;
        _isScrolling = true;
        Scrolling.Add(this);
        Timer.Start();
    }

    private void Stop()
    {
        _translate.X = 0;

        if (!_isScrolling)
        {
            return;
        }

        _isScrolling = false;
        Scrolling.Remove(this);

        if (Scrolling.Count == 0)
        {
            Timer.Stop();
        }
    }

    private static void AdvanceAll()
    {
        for (var i = Scrolling.Count - 1; i >= 0; i--)
        {
            Scrolling[i].Advance();
        }
    }

    /// <summary>推进一帧：起点停留 → 向左滚到尾部 → 末端停留 → 滚回起点，如此循环。</summary>
    private void Advance()
    {
        var travel = Math.Clamp(_distance / Speed, MinTravelSeconds, MaxTravelSeconds);
        var cycle = (HoldSeconds + travel) * 2;
        var t = _elapsed % cycle;
        _elapsed += FrameSeconds;

        double x;
        if (t < HoldSeconds)
        {
            x = 0;
        }
        else if (t < HoldSeconds + travel)
        {
            x = -_distance * Ease((t - HoldSeconds) / travel);
        }
        else if (t < HoldSeconds * 2 + travel)
        {
            x = -_distance;
        }
        else
        {
            x = -_distance * (1 - Ease((t - HoldSeconds * 2 - travel) / travel));
        }

        _translate.X = x;
    }

    /// <summary>平滑首尾（smoothstep），让往返不至于太生硬。</summary>
    private static double Ease(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return progress * progress * (3 - 2 * progress);
    }
}