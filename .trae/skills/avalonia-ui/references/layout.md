# Avalonia 版面配置知識庫

此檔案包含 Avalonia 版面配置相關文件摘要。

---

## 對齊、邊距與間距 (Alignment, Margins and Padding)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/building-layouts/alignment-margins-and-padding

精確控制元素位置的四個關鍵屬性：
- **HorizontalAlignment**：水平對齊（Left/Center/Right/Stretch）
- **VerticalAlignment**：垂直對齊（Top/Center/Bottom/Stretch）
- **Margin**：元素外部的空間
- **Padding**：元素邊框內側的空間（容器控制項如 Border 使用）

### 重點程式碼範例（如有）
```xml
<Border Background="LightBlue" BorderBrush="Black" BorderThickness="2" Padding="15">
  <StackPanel Background="White" HorizontalAlignment="Center" VerticalAlignment="Top">
    <TextBlock Margin="5,0" FontSize="18" HorizontalAlignment="Center">
      Alignment, Margin and Padding Sample
    </TextBlock>
    <Button HorizontalAlignment="Left" Margin="20">Button 1</Button>
    <Button HorizontalAlignment="Right" Margin="10">Button 2</Button>
    <Button HorizontalAlignment="Stretch">Button 3</Button>
  </StackPanel>
</Border>
```

---

## 面板概觀 (Panels Overview)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/building-layouts/panels-overview

詳述各種 Panel 控制項的使用方法：

### Canvas
提供最靈活的版面配置，透過絕對座標定位。支援 `Canvas.Left`、`Canvas.Top`、`Canvas.Right`、`Canvas.Bottom` 附加屬性。預設允許子元素超出邊界，設定 `ClipToBounds="true"` 可限制。

```xml
<Canvas Height="400" Width="400">
  <Canvas Height="100" Width="100" Top="0" Left="0" Background="Red"/>
  <Canvas Height="100" Width="100" Top="100" Left="100" Background="Green"/>
  <Canvas Height="100" Width="100" Top="50" Left="50" Background="Blue"/>
</Canvas>
```

### DockPanel
使用 `DockPanel.Dock` 附加屬性，讓子元素靠著容器邊緣排列（Top/Bottom/Left/Right）。`LastChildFill="True"` 讓最後一個子元素填滿剩餘空間。

```xml
<DockPanel LastChildFill="True">
  <Border Height="25" Background="SkyBlue" DockPanel.Dock="Top">
    <TextBlock>Dock = "Top"</TextBlock>
  </Border>
  <!-- 最後一個元素填滿剩餘空間 -->
  <Border Background="White">
    <TextBlock>LastChildFill</TextBlock>
  </Border>
</DockPanel>
```

---
(Building Layouts)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/building-layouts/

Avalonia 提供多種 Panel 控制項來處理版面配置：

| 控制項 | 說明 |
|--------|------|
| `Panel` | 所有子元素填滿 Panel 邊界 |
| `Canvas` | 用座標明確定位子元素 |
| `DockPanel` | 子元素水平或垂直排列，相對於彼此 |
| `Grid` | 彈性的欄列網格版面 |
| `RelativePanel` | 相對於其他元素或面板本身排列子元素 |
| `StackPanel` | 水平或垂直堆疊排列子元素 |
| `WrapPanel` | 由左到右排列，到邊緣自動換行 |

**注意**：與 WPF 不同，Avalonia 的 `Panel` 是可直接使用的控制項（非抽象類別），行為與無欄列的 `Grid` 相同，但執行時的消耗更小。

### 版面配置系統流程
1. 子元素開始測量（Measure Pass）
2. 評估 `Width`、`Height`、`Margin` 等屬性
3. 套用 Panel 特定邏輯（如 Dock 方向、Stack 方向）
4. 所有子元素測量完後進行排列（Arrange Pass）
5. 繪製 Children 集合到螢幕
6. 若有新子元素加入，重新觸發流程

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter03 — Layout Basics (DockPanel, Grid, StackPanel)

*(關鍵佈局範例已收錄於 getting-started.md 的 Chapter03 區塊中，以下補充要點)*

**資源的 FindResource 用法**
```csharp
// 宣告
// <SolidColorBrush x:Key="HighlightBrush" Color="#FFE57F"/>

private void OnHighlight(object? sender, RoutedEventArgs e)
{
    if (FindResource("HighlightBrush") is IBrush brush)
        Background = brush;
}
```

---

### Chapter05 — The Layout System

**Measure / Arrange 兩階段**
- Measure：父容器詢問子控制項「你想要多大？」→ 子控制項回應 DesiredSize
- Arrange：父容器根據測量結果決定子控制項的最終位置
- 相關類別：`Layoutable`、`LayoutManager`

**佈局失效觸發**
```csharp
InvalidateMeasure();  // DesiredSize 改變時
InvalidateArrange();  // 僅位置改變時
```

**核心面板範例**
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LayoutPlayground.MainWindow"
        Width="880" Height="560">
  <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,*" Padding="16">
    <!-- StackPanel -->
    <StackPanel Grid.Row="1" Spacing="12">
      <Border BorderBrush="#CCC" BorderThickness="1" Padding="8">
        <StackPanel Spacing="6">
          <Button Content="Top"/>
          <Button Content="Bottom"/>
          <Button Content="Stretch" HorizontalAlignment="Stretch"/>
        </StackPanel>
      </Border>
      <!-- DockPanel -->
      <Border BorderBrush="#CCC" BorderThickness="1" Padding="8">
        <DockPanel LastChildFill="True">
          <TextBlock DockPanel.Dock="Top" Text="Top bar"/>
          <TextBlock DockPanel.Dock="Left" Text="Left"/>
          <Border Background="#F0F6FF" Padding="8">
            <TextBlock Text="Last child fills remaining space"/>
          </Border>
        </DockPanel>
      </Border>
    </StackPanel>
    <!-- Grid + WrapPanel -->
    <StackPanel Grid.Column="1" Grid.Row="1" Spacing="12">
      <Border BorderBrush="#CCC" BorderThickness="1" Padding="8">
        <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto" ColumnSpacing="8" RowSpacing="8">
          <TextBlock Text="Name:"/>
          <TextBox Grid.Column="1" MinWidth="200"/>
          <TextBlock Grid.Row="1" Text="Email:"/>
          <TextBox Grid.Row="1" Grid.Column="1"/>
        </Grid>
      </Border>
      <Border BorderBrush="#CCC" BorderThickness="1" Padding="8">
        <WrapPanel ItemHeight="32" ItemWidth="100">
          <Button Content="One"/>
          <Button Content="Two"/>
          <Button Content="Three"/>
        </WrapPanel>
      </Border>
    </StackPanel>
  </Grid>
</Window>
```

**GridSplitter（可調整欄寬）**
```xml
<Grid ColumnDefinitions="3*,Auto,2*">
  <StackPanel Grid.Column="0"><!-- 左側內容 --></StackPanel>
  <GridSplitter Grid.Column="1" Width="6" ShowsPreview="True" Background="#DDD"/>
  <StackPanel Grid.Column="2"><!-- 右側內容 --></StackPanel>
</Grid>
```

**SharedSizeGroup（跨 Grid 共用欄寬）**
```xml
<Grid Grid.IsSharedSizeScope="True">
  <Grid.ColumnDefinitions>
    <ColumnDefinition SharedSizeGroup="Label"/>
    <ColumnDefinition Width="*"/>
  </Grid.ColumnDefinitions>
</Grid>
```

**Viewbox 和 LayoutTransformControl**
```xml
<Viewbox Stretch="Uniform" Width="200" Height="200">
  <TextBlock Text="Scaled" FontSize="24"/>
</Viewbox>

<LayoutTransformControl>
  <LayoutTransformControl.LayoutTransform>
    <RotateTransform Angle="-10"/>
  </LayoutTransformControl.LayoutTransform>
  <Border Padding="12" Background="#E7F1FF">
    <TextBlock Text="Rotated layout"/>
  </Border>
</LayoutTransformControl>
```

**自訂面板（覆寫 MeasureOverride/ArrangeOverride）**
```csharp
public class UniformGridPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<UniformGridPanel, int>(nameof(Columns), 2);

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(Size.Infinity);

        var rows = (int)Math.Ceiling(Children.Count / (double)Columns);
        return new Size(availableSize.Width, availableSize.Height / rows * rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = (int)Math.Ceiling(Children.Count / (double)Columns);
        var cellWidth = finalSize.Width / Columns;
        var cellHeight = finalSize.Height / rows;

        for (int i = 0; i < Children.Count; i++)
        {
            var row = i / Columns;
            var col = i % Columns;
            Children[i].Arrange(new Rect(col * cellWidth, row * cellHeight, cellWidth, cellHeight));
        }
        return finalSize;
    }
}
```

**啟用佈局日誌**
```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .LogToTrace(LogEventLevel.Debug, new[] { LogArea.Layout })
    .StartWithClassicDesktopLifetime(args);
```

---

### Chapter34（layout 補充）— Code-First Layout Recipes

> *同見 controls.md — Chapter34；本節聚焦可重用的 Layout Helpers 與 Factory 模式。*

#### 代碼範例

```csharp
// Dashboard Factory 模式
public static class DashboardViewFactory
{
    public static Control Create(IDashboardViewModel vm)
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            Children =
            {
                CreateSummary(vm).WithGridPosition(0, 0),
                CreateChart(vm).WithGridPosition(0, 1)
            }
        };
    }

    private static Control CreateSummary(IDashboardViewModel vm)
        => new Border
        {
            Padding = new Thickness(24),
            Child = new TextBlock().Bind(TextBlock.TextProperty,
                new Binding(nameof(vm.TotalSales)))
        };
}

// Grid Fluent Helper
public static class GridExtensions
{
    public static T WithGridPosition<T>(this T element, int row, int column)
        where T : Control
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        return element;
    }
}
```

```csharp
// LINQ 動態生成 Grid Children
var cards = vm.Notifications.Select((item, index) =>
    CreateNotificationCard(item).WithGridPosition(index / 3, index % 3));

var grid = new Grid
{
    ColumnDefinitions =
    {
        new ColumnDefinition(GridLength.Star),
        new ColumnDefinition(GridLength.Star),
        new ColumnDefinition(GridLength.Star)
    }
};
foreach (var card in cards)
    grid.Children.Add(card);
```

```csharp
// ItemsControl 程式化 ItemsPanel
var list = new ListBox
{
    ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
    {
        ItemWidth = 160,
        ItemHeight = 200
    }),
    Items = vm.Products.Select(p => CreateProductCard(p))
};
```

```csharp
// VirtualizingStackPanel for large lists
itemsControl.ItemsPanel = new FuncTemplate<Panel?>(() =>
    new VirtualizingStackPanel
    {
        Orientation = Orientation.Vertical,
        VirtualizationMode = ItemVirtualizationMode.Simple
    });
```

---