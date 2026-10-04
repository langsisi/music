# Avalonia 樣式知識庫

此檔案包含 Avalonia 樣式、主題相關文件摘要。

---

## Container Queries（v11.3）

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/container-queries

Container Queries 允許根據祖先容器的大小來啟用樣式，類似 CSS Container Queries。

使用方法：
1. 宣告 `ContainerQuery`（在控制項的 `Styles` 中）
2. 設定容器（`Container.Name` + `Container.Sizing` 附加屬性）

`Container.Sizing` 選項：`Normal`（預設）、`Width`、`Height`、`WidthAndHeight`

限制：不能放在 `Style` 元素內；容器本身不能被其 queries 的樣式影響。

### 重點程式碼範例（如有）
```xml
<StackPanel>
    <StackPanel.Styles>
        <ContainerQuery Name="container" Query="max-width:400">
            <Style Selector="Button">
                <Setter Property="Background" Value="Red"/>
            </Style>
        </ContainerQuery>
    </StackPanel.Styles>
</StackPanel>
<Button Container.Name="container" Container.Sizing="Width" />
```

---

## Simple 主題

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/themes/simple

簡約輕量的主題，適合嵌入式設備。需安裝 `Avalonia.Themes.Simple` NuGet 套件。

```xml
<Application.Styles>
    <SimpleTheme />
</Application.Styles>
```

---


## 樣式類別 (Style Classes)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/style-classes

使用 `Classes` 屬性為控制項指定一或多個樣式類別（空格分隔）。

### 重點程式碼範例（如有）
```xml
<Button Classes="h1 blue"/>

<!-- 偽類（Pseudo Classes）：以冒號開頭，由控制項定義 -->
<Style Selector="Border:pointerover">
    <Setter Property="Background" Value="Red"/>
</Style>

<!-- 條件式樣式類別 -->
<Button Classes.accent="{Binding IsSpecial}" />

<!-- 模板內部偽類選擇器 -->
<Style Selector="Button:pressed /template/ ContentPresenter">
    <Setter Property="TextBlock.Foreground" Value="Red"/>
</Style>
```

```csharp
// 程式碼中操作樣式類別
control.Classes.Add("blue");
control.Classes.Remove("red");
```

常用偽類：`:focus`、`:disabled`、`:pressed`（按鈕）、`:checked`（CheckBox）、`:pointerover`

---

## 主題概觀 (Themes Overview)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/themes/

Avalonia 提供兩個內建主題：
- **Fluent Theme**：受 Microsoft Fluent Design System 啟發的現代主題
- **Simple Theme**：簡約輕量的主題，內建樣式較少

社群主題：
- **Material.Avalonia**：受 Google Material Design 啟發
- **Semi.Avalonia**：受 Semi Design 啟發
- **Classic.Avalonia**：Windows 9x 風格經典主題

---

## Fluent 主題

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/themes/fluent

Fluent Theme 是受 Microsoft Fluent Design System 啟發的現代主題。需安裝 `Avalonia.Themes.Fluent` NuGet 套件。

### 重點程式碼範例（如有）
```xml
<!-- App.axaml 中引入 -->
<Application.Styles>
    <FluentTheme />
</Application.Styles>

<!-- 使用緊湊版面 -->
<Application.Styles>
    <FluentTheme DensityStyle="Compact" />
</Application.Styles>

<!-- 自訂色彩調色盤 -->
<Application.Styles>
    <FluentTheme>
        <FluentTheme.Palettes>
            <ColorPaletteResources x:Key="Light" Accent="Green" RegionColor="White" ErrorText="Red" />
            <ColorPaletteResources x:Key="Dark" Accent="DarkGreen" RegionColor="Black" ErrorText="Yellow" />
        </FluentTheme.Palettes>
    </FluentTheme>
</Application.Styles>
```

**注意**：Accent 支援動態綁定，其他調色盤屬性在應用程式啟動時只讀取一次。可使用 [https://theme.xaml.live/](https://theme.xaml.live/) 線上編輯器建立自訂調色盤。

---


**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/

Avalonia 提供三種主要樣式機制：
- **Styles**：類似 CSS，用於根據內容或用途設定控制項樣式（如標題文字塊）
- **Control Themes**：類似 WPF/UWP 樣式，用於套用控制項主題
- **Container Queries**：根據容器大小套用樣式集合

---

## 樣式系統 (Styles)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/styles

Avalonia 樣式系統類似 CSS，而非 WPF 樣式（WPF 樣式對應 Avalonia 的 ControlTheme）。

樣式包含**選擇器（Selector）**和**設定器（Setters）**兩部分。系統會從控制項向上搜尋邏輯樹套用樣式（支援層疊）。樣式放在 `Styles` 集合中，位置決定作用範圍。

### 重點程式碼範例（如有）
```xml
<Window>
    <Window.Styles>
        <!-- 選擇器語法：ControlClass.styleClass -->
        <Style Selector="TextBlock.h1">
            <Setter Property="FontSize" Value="24"/>
            <Setter Property="FontWeight" Value="Bold"/>
            <!-- 巢狀樣式：^ 代表父選擇器 -->
            <Style Selector="^:pointerover">
                <Setter Property="Foreground" Value="Red"/>
            </Style>
        </Style>
    </Window.Styles>
    <StackPanel>
       <TextBlock Classes="h1">Heading 1</TextBlock>
    </StackPanel>
</Window>
```

```csharp
// 覆寫 StyleKey 讓衍生控制項以父類型樣式顯示
public class MyButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
}
```

---

## 控制項主題 (Control Themes)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/control-themes

Control Themes 是 Avalonia 11 引入的機制，解決了標準樣式無法「移除」的問題。

與 Style 的主要差異：
- 沒有選擇器，改用 `TargetType` 屬性
- 儲存在 `ResourceDictionary` 而非 `Styles` 集合
- 透過設定控制項的 `Theme` 屬性（通常用 `{StaticResource}`）套用

### 重點程式碼範例（如有）
```xml
<!-- 在 App.axaml 定義 ControlTheme -->
<Application.Resources>
    <ControlTheme x:Key="EllipseButton" TargetType="Button">
        <Setter Property="Background" Value="Blue"/>
        <Setter Property="Foreground" Value="Yellow"/>
        <Setter Property="Padding" Value="8"/>
        <Setter Property="Template">
            <ControlTemplate>
                <Panel>
                    <Ellipse Fill="{TemplateBinding Background}"
                             HorizontalAlignment="Stretch"
                             VerticalAlignment="Stretch"/>
                    <ContentPresenter x:Name="PART_ContentPresenter"
                                      Content="{TemplateBinding Content}"
                                      Margin="{TemplateBinding Padding}"/>
                </Panel>
            </ControlTemplate>
        </Setter>
        <!-- 巢狀樣式設定 hover 狀態 -->
        <Style Selector="^:pointerover">
            <Setter Property="Background" Value="Red"/>
            <Setter Property="Foreground" Value="White"/>
        </Style>
    </ControlTheme>
</Application.Resources>

<!-- 在視圖中套用 -->
<Button Theme="{StaticResource EllipseButton}">Hello World!</Button>
```

---

## 如何使用自訂字體

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-fonts

Avalonia 支援嵌入自訂字體作為應用資源。需要先將字體檔案加入專案（設定為 EmbeddedResource），然後在 XAML 中使用 `avares://` 路徑參考。

---

## 如何使用包含的樣式

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-included-styles

可以將樣式定義在獨立的 `.axaml` 檔案中，然後使用 `<StyleInclude>` 在其他地方引用。

---

## 如何使用主題變體（Theme Variants）

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants

Avalonia 支援亮色/暗色主題變體。可以透過 `Application.RequestedThemeVariant` 或控件的 `RequestedThemeVariant` 屬性切換。也可以在資源字典中使用 `ResourceDictionary.ThemeDictionaries` 定義不同主題的資源。

---

## 屬性設置器（Property Setters）

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/property-setters

樣式中的 `<Setter>` 元素用於設定目標控件的屬性值。Setter 可以使用靜態資源、動態資源或數據綁定。

---

## 資源（Resources）

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/resources

資源允許在多個控件之間共用值（顏色、筆刷、字體大小等）。可以定義在 `Window.Resources`、`Application.Resources` 或任何控件上。使用 `{StaticResource}` 或 `{DynamicResource}` 引用資源，後者在資源更改時自動更新。

---

## 選擇器（Selectors）

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/selectors

Avalonia 樣式選擇器類似於 CSS 選擇器，可以根據控件類型、名稱、樣式類別、偽類別、父子關係等進行選擇。

---

## 設置器優先級（Setter Precedence）

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/setter-precedence

Avalonia 中樣式設置器的優先級規則：本地值 > 模板綁定 > 樣式觸發器 > 樣式 setter。越靠近控件定義的樣式優先級越高。

---

## 樣式故障排除

**來源：** https://docs.avaloniaui.net/docs/guides/styles-and-resources/troubleshooting

常見樣式問題排除建議：使用 Avalonia DevTools（F12）的 Visual Tree 和 Styles 面板診斷樣式未生效的問題。確認選擇器是否正確、資源鍵是否存在、樣式是否在正確的範圍內定義。

---

## Reference Styles 樣式參考索引

**來源：** https://docs.avaloniaui.net/docs/reference/styles/

Avalonia UI 樣式參考，包含樣式選擇器格式和偽類的詳細說明。

---

## Pseudo Classes 偽類

**來源：** https://docs.avaloniaui.net/docs/reference/styles/pseudo-classes

偽類是控制項透過 `PseudoClasses` 屬性公開的狀態關鍵字（慣例以冒號開頭，如 `:pressed`），用於條件式樣式設定。

**通用偽類（InputElement 定義）：**
- `:disabled`：控制項已停用
- `:pointerover`：指標位於控制項上方
- `:focus`：控制項擁有焦點
- `:focus-within`：控制項或其後代擁有焦點
- `:focus-visible`：擁有焦點且應顯示視覺指示器

**自訂偽類：** 繼承控制項時用 `[PseudoClasses]` 屬性宣告，並在程式碼中呼叫 `PseudoClasses.Set(":name", value)` 設定狀態。

### 重點程式碼範例（如有）
```xml
<Window.Styles>
    <Style Selector="CheckBox:checked">
        <Setter Property="FontWeight" Value="Bold" />
    </Style>
</Window.Styles>
```

```csharp
[PseudoClasses(":left", ":right", ":middle")]
public class AreaButton : Button
{
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (pos.X < Bounds.Width * 0.25)
            PseudoClasses.Set(":left", true);
        // ...
    }
}
```

---

## Style Selector Syntax 樣式選擇器語法

**來源：** https://docs.avaloniaui.net/docs/reference/styles/style-selector-syntax

完整的 XAML 樣式選擇器語法與對應的 C# 程式碼。

**主要選擇器：**
- **按控制項類別：** `Button` / `new Style(x => x.OfType<Button>())`
- **按名稱：** `#myButton` / `new Style(x => x.Name("myButton"))`
- **按樣式類別：** `Button.large` / `.Class("large")`
- **按偽類：** `Button:focus` / `.Class(":focus")`
- **含衍生類別：** `:is(Button)`
- **直接子代：** `StackPanel > Button`（`>`）
- **任意後代：** `StackPanel Button`（空格）
- **屬性比對：** `Button[IsDefault=true]`
- **範本內部：** `Button /template/ ContentPresenter`
- **排除：** `TextBlock:not(.h1)`
- **清單（OR）：** `TextBlock, Button`
- **位置公式：** `TextBlock:nth-child(2n+3)`

### 重點程式碼範例（如有）
```xml
<!-- 按類別 -->
<Style Selector="Button">
<!-- 含衍生類別 -->
<Style Selector=":is(Button)">
<!-- 直接子代 -->
<Style Selector="StackPanel > Button">
<!-- 任意後代 -->
<Style Selector="StackPanel Button">
<!-- 屬性比對 -->
<Style Selector="Button[IsDefault=true]">
<!-- 附加屬性比對（需括號）-->
<Style Selector="TextBlock[(Grid.Row)=0]">
<!-- 排除 -->
<Style Selector="TextBlock:not(.h1)">
```

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter07 — Styling, Themes, and Resources

**Fluent 主題基礎**
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="ThemePlayground.App"
             RequestedThemeVariant="Light">
  <Application.Styles>
    <FluentTheme Mode="Light"/>
    <!-- Mode="Dark" | Mode="Default"（跟隨 OS） -->
  </Application.Styles>
</Application>
```

**分離資源字典**
```xml
<!-- Styles/Colors.axaml -->
<ResourceDictionary xmlns="https://github.com/avaloniaui">
  <Color x:Key="BrandPrimaryColor">#2563EB</Color>
  <SolidColorBrush x:Key="BrandPrimaryBrush" Color="{DynamicResource BrandPrimaryColor}"/>
</ResourceDictionary>
```

```xml
<!-- Styles/Controls.axaml -->
<Styles xmlns="https://github.com/avaloniaui">
  <Style Selector="Button.primary">
    <Setter Property="Background" Value="{DynamicResource BrandPrimaryBrush}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="Padding" Value="14,10"/>
    <Setter Property="CornerRadius" Value="6"/>
  </Style>
  <Style Selector="Button.primary:pointerover">
    <Setter Property="Background" Value="{DynamicResource BrandPrimaryHoverBrush}"/>
  </Style>
</Styles>
```

```xml
<!-- App.axaml 中引入 -->
<Application.Resources>
  <ResourceInclude Source="avares://ThemePlayground/Styles/Colors.axaml"/>
</Application.Resources>
<Application.Styles>
  <FluentTheme Mode="Default"/>
  <StyleInclude Source="avares://ThemePlayground/Styles/Controls.axaml"/>
</Application.Styles>
```

**StaticResource vs DynamicResource**
- `StaticResource`：載入時解析一次，用於不會改變的值（字型、圓角常數）
- `DynamicResource`：執行期重新求值，主題切換時必須用此

**資源查找順序**
1. 控制項本地資源（`this.Resources`）
2. 邏輯樹父節點
3. `Application.Resources`
4. FluentTheme 合併的字典（light/dark/high contrast）
5. 系統主題後備

**ThemeVariantScope（局部主題）**
```xml
<ThemeVariantScope RequestedThemeVariant="Dark">
  <Border Padding="16">
    <StackPanel>
      <TextBlock Text="Dark section"/>
      <Button Content="Dark themed button" Classes="primary"/>
    </StackPanel>
  </Border>
</ThemeVariantScope>
```

**覆寫 Fluent 資源（按主題變體）**
```xml
<Application.Resources>
  <ResourceDictionary ThemeVariant="Light">
    <SolidColorBrush x:Key="SystemAccentColor" Color="#2563EB"/>
  </ResourceDictionary>
  <ResourceDictionary ThemeVariant="Dark">
    <SolidColorBrush x:Key="SystemAccentColor" Color="#60A5FA"/>
  </ResourceDictionary>
</Application.Resources>
```

**執行期切換主題**
```csharp
// ViewModel
public bool IsDark
{
    get => _isDark;
    set
    {
        if (SetProperty(ref _isDark, value))
            Application.Current!.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
```

**ControlTheme（自訂控制項模板）**
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui">
  <ControlTheme x:Key="PillToggleTheme" TargetType="ToggleButton">
    <Setter Property="Template">
      <ControlTemplate>
        <Border x:Name="PART_Root"
                Background="{TemplateBinding Background}"
                CornerRadius="20"
                Padding="{TemplateBinding Padding}">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                            Content="{TemplateBinding Content}"/>
        </Border>
      </ControlTemplate>
    </Setter>
  </ControlTheme>
</ResourceDictionary>
```

```xml
<ToggleButton Content="Pill" Theme="{StaticResource PillToggleTheme}" Padding="12,6"/>
```

**偽類選擇器速查**

| 偽類 | 觸發時機 |
|------|---------|
| `:pointerover` | 滑鼠移入 |
| `:pressed` | 按下時 |
| `:checked` | 切換控制項為 on |
| `:focus` / `:focus-within` | 鍵盤焦點 |
| `:disabled` | `IsEnabled = false` |
| `:invalid` | 繫結驗證錯誤 |

**高對比主題**
```xml
<ResourceDictionary ThemeVariant="HighContrast" xmlns="https://github.com/avaloniaui">
  <SolidColorBrush x:Key="BrandPrimaryBrush" Color="#00AACC"/>
</ResourceDictionary>
```

切換：`Application.Current!.RequestedThemeVariant = ThemeVariant.HighContrast;`

---

### Chapter29 — Animations & Composition

#### 關鍵概念

**Keyframe Animation**
- `Animation` 類包含 `Duration`、`IterationCount`、`PlaybackDirection`、`FillMode`、`SpeedRatio`
- `KeyFrame` 指定 Cue（0%~100%）+ Setters
- 30+ easing 曲線，`SplineEasing` 支援自訂 cubic Bezier
- `IterationCount="INFINITE"` 時不能用 `RunAsync`（會拋出異常）

**Implicit Transitions**
`Animatable.Transitions` 自動在屬性值改變時補間，比 keyframe 輕量。適合 hover 狀態、主題切換。

**Page Transitions**
`TransitioningContentControl` + `IPageTransition`：`PageSlide`（方向滑入）、`CrossFade`（淡入淡出）、`CompositePageTransition`（組合多效果）。

**Composition Layer（GPU 加速）**
- 在獨立執行緒渲染，UI thread 繁忙時仍流暢
- `ElementComposition.GetElementVisual(control)` 取得 `CompositionVisual`
- `CompositionEffectBrush`：blend mode、模糊、發光
- `ExpressionAnimation`：以公式驅動屬性（視差效果）
- `ImplicitAnimationCollection`：屬性改變時自動播放

**效能注意**
- 優先動畫化 Transform/Opacity，避免 Width/Height（觸發 UI thread layout）
- 載入大量資料時用 `Animatable.DisableTransitions` 暫停轉場

#### 代碼範例

```xml
<!-- 無限循環 Keyframe Animation -->
<Window.Styles>
  <Style Selector="Rectangle.alert">
    <Style.Animations>
      <Animation Duration="0:0:0.6" IterationCount="INFINITE" PlaybackDirection="Alternate">
        <KeyFrame Cue="0%">
          <Setter Property="Opacity" Value="0.4"/>
          <Setter Property="RenderTransform.ScaleX" Value="1"/>
        </KeyFrame>
        <KeyFrame Cue="100%">
          <Setter Property="Opacity" Value="1"/>
          <Setter Property="RenderTransform.ScaleX" Value="1.05"/>
        </KeyFrame>
      </Animation>
    </Style.Animations>
  </Style>
</Window.Styles>
```

```csharp
// 程式化執行 Animation
public async Task ShowAsync(CancellationToken token)
{
    await _slideIn.RunAsync(_host, token);
    await Task.Delay(TimeSpan.FromSeconds(3), token);
    await _slideOut.RunAsync(_host, token);
}
```

```xml
<!-- Implicit Transitions -->
<Button Classes="primary">
  <Button.Transitions>
    <Transitions>
      <DoubleTransition Property="Opacity" Duration="0:0:0.150"/>
      <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.200"/>
    </Transitions>
  </Button.Transitions>
</Button>

<Style Selector="Button:pointerover">
  <Setter Property="Opacity" Value="1"/>
  <Setter Property="RenderTransform">
    <Setter.Value><ScaleTransform ScaleX="1.02" ScaleY="1.02"/></Setter.Value>
  </Setter>
</Style>
```

```xml
<!-- Page Transitions -->
<TransitioningContentControl Content="{Binding CurrentPage}">
  <TransitioningContentControl.PageTransition>
    <CompositePageTransition>
      <CompositePageTransition.PageTransitions>
        <PageSlide Duration="0:0:0.25" Orientation="Horizontal" Offset="32"/>
        <CrossFade Duration="0:0:0.20"/>
      </CompositePageTransition.PageTransitions>
    </CompositePageTransition>
  </TransitioningContentControl.PageTransition>
</TransitioningContentControl>
```

```csharp
// Composition Visual（自訂底部 accent bar）
var elementVisual = ElementComposition.GetElementVisual(host)!;
var compositor = elementVisual.Compositor;

var sprite = compositor.CreateSolidColorVisual();
sprite.Color = Colors.DeepSkyBlue;
sprite.Size = new Vector2((float)host.Bounds.Width, 4);
sprite.Offset = new Vector3(0, (float)host.Bounds.Height - 4, 0);
(elementVisual as CompositionContainerVisual)!.Children.Add(sprite);
```

```csharp
// ExpressionAnimation（視差效果）
var parallax = compositor.CreateExpressionAnimation(
    "Vector3(host.Offset.X * 0.05, host.Offset.Y * 0.05, 0)");
parallax.SetReferenceParameter("host", hostVisual);
parallax.Target = nameof(CompositionVisual.Offset);
glow.StartAnimation(nameof(CompositionVisual.Offset), parallax);
```

---

### Chapter35 — Code-First Bindings, Resources & Styles

#### 關鍵概念

**程式碼中建立 Binding**
`Binding` 類直接設 `Mode`、`UpdateSourceTrigger`、`ValidatesOnExceptions`、`Converter`；`RelativeSource` 物件對應 XAML 的 `RelativeSource`。

**Indexer 綁定**
直接在 `Binding.Path` 中使用 `"Statuses[SelectedStatus]"` 語法；字典變更時呼叫 `RaisePropertyChanged("Item[]")` 觸發更新。

**CompiledBindingFactory**
強型別 compiled bindings，捕捉 delegates 而非反射，適合 Source Generator 工作流。

**ResourceDictionary**
直接用 C# 集合 API，用強型別常數類管理資源 key 避免 magic strings。

**Style 物件**
`new Style(x => x.OfType<Button>().Class("primary"))` + `Setters` 集合；支援 `Triggers`；可用 `StyleInclude` 仍載入現有 `.axaml` 片段。

#### 代碼範例

```csharp
// 建立 Binding
var binding = new Binding("Customer.Name")
{
    Mode = BindingMode.TwoWay,
    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
    ValidatesOnExceptions = true
};
nameTextBox.Bind(TextBox.TextProperty, binding);
```

```csharp
// RelativeSource Binding
var binding = new Binding
{
    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
    {
        AncestorType = typeof(Window)
    },
    Path = nameof(Window.Title)
};
header.Bind(TextBlock.TextProperty, binding);
```

```csharp
// CompiledBindingFactory
var factory = new CompiledBindingFactory();
var compiled = factory.Create<DashboardViewModel, string>(
    vmGetter: static vm => vm.Header,
    vmSetter: static (vm, value) => vm.Header = value,
    name: nameof(DashboardViewModel.Header),
    mode: BindingMode.TwoWay);
headerText.Bind(TextBlock.TextProperty, compiled);
```

```csharp
// Fluent Binding Helper
public static class BindingHelpers
{
    public static T BindValue<T, TValue>(this T control, AvaloniaProperty<TValue> property,
        string path, BindingMode mode = BindingMode.Default) where T : AvaloniaObject
    {
        control.Bind(property, new Binding(path) { Mode = mode });
        return control;
    }
}

// 使用：
var searchBox = new TextBox()
    .BindValue(TextBox.TextProperty, nameof(SearchViewModel.Query), BindingMode.TwoWay);
```

```csharp
// ResourceDictionary（型別安全 keys）
public static class ResourceKeys
{
    public const string AccentBrush = nameof(AccentBrush);
}

Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
{
    [ResourceKeys.AccentBrush] = new SolidColorBrush(Color.Parse("#4F8EF7")),
    ["AccentForeground"] = Brushes.White
});
```

```csharp
// Fluent Style 建立
var buttonStyle = new Style(x => x.OfType<Button>().Class("primary"))
{
    Setters =
    {
        new Setter(Button.BackgroundProperty, Brushes.MediumPurple),
        new Setter(Button.ForegroundProperty, Brushes.White),
        new Setter(Button.PaddingProperty, new Thickness(20, 10))
    },
    Triggers =
    {
        new Trigger
        {
            Property = Button.IsPointerOverProperty,
            Value = true,
            Setters = { new Setter(Button.BackgroundProperty, Brushes.DarkMagenta) }
        }
    }
};
Application.Current!.Styles.Add(buttonStyle);
```

```csharp
// MultiBinding
var multi = new MultiBinding
{
    Bindings = { new Binding("FirstName"), new Binding("LastName") },
    Converter = FullNameConverter.Instance
};
fullNameText.Bind(TextBlock.TextProperty, multi);
```

---