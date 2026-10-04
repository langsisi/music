# Avalonia 雜項知識庫

此檔案包含不屬於其他特定類別的 Avalonia 文件摘要。

---

## MessageBox

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/messagebox

Avalonia 目前沒有內建 `MessageBox` 元件，此功能正在考慮未來加入。

第三方解決方案：
- [Actipro Avalonia UI Controls](https://www.actiprosoftware.com/products/controls/avalonia)
- [DialogHost.Avalonia](https://github.com/AvaloniaUtils/DialogHost.Avalonia)
- [MessageBox.Avalonia](https://github.com/AvaloniaCommunity/MessageBox.Avalonia)
- [Ursa.Avalonia](https://github.com/irihitech/Ursa.Avalonia)

---
(File Dialogs)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/file-dialogs

透過 `StorageProvider` 服務 API 存取檔案對話框功能，從 `Window` 或 `TopLevel` 類別取得。

### 重點程式碼範例（如有）
```csharp
// 開啟檔案選擇器
private async void OpenFileButton_Clicked(object sender, RoutedEventArgs args)
{
    var topLevel = TopLevel.GetTopLevel(this);
    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
        Title = "Open Text File",
        AllowMultiple = false
    });
    if (files.Count >= 1)
    {
        await using var stream = await files[0].OpenReadAsync();
        using var streamReader = new StreamReader(stream);
        var fileContent = await streamReader.ReadToEndAsync();
    }
}

// 儲存檔案
private async void SaveFileButton_Clicked(object sender, RoutedEventArgs args)
{
    var topLevel = TopLevel.GetTopLevel(this);
    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
    {
        Title = "Save Text File"
    });
    if (file is not null)
    {
        await using var stream = await file.OpenWriteAsync();
        using var streamWriter = new StreamWriter(stream);
        await streamWriter.WriteLineAsync("Hello World!");
    }
}
```

---

## XAML 介紹 (Introduction to XAML)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/introduction-to-xaml

Avalonia UI 使用 XAML 定義使用者介面。Avalonia 使用 `.axaml` 副檔名（與 WPF 的 `.xaml` 不同，避免 Visual Studio 整合問題）。

典型 AXAML 檔案結構：
- `xmlns="https://github.com/avaloniaui"` - Avalonia UI 命名空間（必須）
- `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"` - XAML 語言命名空間
- `x:Class="..."` - 對應的 code-behind 類別

命名空間宣告有兩種格式：
- `xmlns:alias="using:AppNameSpace.MyNamespace"` - 使用 `using:` 前綴
- `xmlns:alias="clr-namespace:AppNameSpace.MyNamespace"` - 使用 `clr-namespace:` 前綴（跨 Assembly 需加 `;assembly=AssemblyName`）

### 重點程式碼範例（如有）
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="AvaloniaApplication1.MainWindow">
    <Button Background="Blue" Content="{Binding Greeting}">Hello World!</Button>
</Window>
```

---


**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/code-behind

大多數 Avalonia 控制項除了 XAML 檔案外，還有一個 code-behind 檔案（副檔名為 `.axaml.cs`）。Code-behind 類別名稱必須與 XAML 中的 `x:Class` 屬性相符。

Code-behind 的主要用途：
- 在建構子中呼叫 `InitializeComponent()` 以載入 XAML
- 使用 `Name` 屬性在 XAML 中命名控制項，然後在 code-behind 中存取
- 撰寫事件處理器

### 重點程式碼範例（如有）
```xml
<!-- MainWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="AvaloniaApplication4.MainWindow">
  <Button Name="greetingButton" Click="GreetingButtonClickHandler">Hello World</Button>
</Window>
```

```csharp
// MainWindow.axaml.cs
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        greetingButton.Content = "Goodbye Cruel World!";
        greetingButton.Background = Brushes.Blue;
    }

    public void GreetingButtonClickHandler(object sender, RoutedEventArgs e)
    {
        // 事件處理邏輯
    }
}
```

---


**來源：** https://docs.avaloniaui.net/docs/basics/

Avalonia 基礎章節介紹建立應用程式所需的核心概念，分為兩大主題：**使用者介面（User Interface）** 和 **資料（Data）**。這是索引頁，提供基礎知識的入口。

---

## 新增互動功能 (Adding Interactivity)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/adding-interactivity

Avalonia 提供兩種方式讓 UI 與使用者互動：**事件（Events）** 和 **命令（Commands）**。

- **事件處理**：在 Code-Behind 實作事件處理器，然後在 XAML 訂閱事件
- **命令**：更高階的互動方式，將使用者操作與 ViewModel 的方法解耦

### 重點程式碼範例（如有）
```csharp
// Code-behind 事件處理器
private void HandleButtonClick(object sender, RoutedEventArgs e)
{
    // 事件處理邏輯
}
```

```xml
<!-- XAML 中訂閱事件 -->
<Button Name="myButton" Content="Click Me" Click="HandleButtonClick" />

<!-- 使用 Command 綁定 ViewModel 方法 -->
<Button Content="Click Me" Command="{Binding HandleButtonClick}" />
```

---

## 動畫 (Animations)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/animations

Avalonia 有兩種動畫類型：
- **關鍵幀動畫（Keyframe Animation）**：可在時間軸上定義多個關鍵幀來改變屬性值，非常靈活
- **Transitions**：只改變單一屬性的簡單動畫

關鍵幀動畫使用 CSS 選擇器觸發：若選擇器始終匹配則動畫在控制項出現時觸發；若選擇器條件性匹配則在匹配時執行。支援設定 Delay、Repeat、播放方向、Fill Mode 及 Easing Function。

---

## 資源（Assets）

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/assets

Avalonia 應用程式可以包含圖片、樣式、資源字典等資源。透過在 `.csproj` 中加入 `<AvaloniaResource>` 元素來包含資源。

### 重點程式碼範例（如有）
```xml
<!-- 在 .csproj 中包含 Assets 資料夾所有檔案 -->
<ItemGroup>
  <AvaloniaResource Include="Assets\**"/>
</ItemGroup>
```

```xml
<!-- 在 XAML 中引用資源 -->
<Image Source="icon.png"/>
<Image Source="/Assets/icon.png"/>
<!-- 引用其他 Assembly 的資源 -->
<Image Source="avares://MyAssembly/Assets/icon.png"/>
```

```csharp
// 在程式碼中載入資源
var bitmap = new Bitmap(AssetLoader.Open(new Uri(uri)));
```

**注意**：Avalonia 不支援 `file://`、`http://`、`https://` 協定，需自行實作。

---

## 管理跨平台差異與功能

**來源：** https://docs.avaloniaui.net/docs/guides/building-cross-platform-applications/dealing-with-platforms

跨平台開發中需處理螢幕尺寸、導覽模式、鍵盤差異等平台特性。可透過以下方式應對：
- **平台抽象（Platform Abstraction）**：使用介面或基底類別，在共用程式碼中定義，各平台分別實作。
- **條件式程式碼**：使用 `#if` 或 Avalonia 提供的平台特定 API。
- **Maui.Essentials**：可透過 `Microsoft.Maui.Essentials` NuGet 套件在 .NET 8+ 上使用（但不支援 Linux、Browser、非 macCatalyst 的 macOS）。

---

## 跨平台應用程式的方案結構設定

**來源：** https://docs.avaloniaui.net/docs/guides/building-cross-platform-applications/solution-setup

`Avalonia Cross Platform Application` 範本產生以下專案結構：
- **Core Project**：共用核心，包含業務邏輯、ViewModels、Views，所有其他專案都參照此專案。
- **Desktop Project**：支援 Windows/macOS/Linux，輸出類型為 WinExe。
- **Android Project**：基於 NET-Android，MainActivity 繼承自 `AvaloniaMainActivity`。
- **iOS Project**：基於 NET-iOS，入口點 AppDelegate 繼承自 `AvaloniaAppDelegate`。
- **Browser Project**：WebAssembly，RuntimeIdentifier 為 `browser-wasm`。

---

## 自訂控制項概覽

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/

Avalonia 自訂控制項分兩類：
- **Custom Control**：直接繼承 `Control`，使用 Avalonia 繪圖系統自行繪製（如 TextBlock、Image）。
- **Templated Custom Control**：「無外觀」控制項，樣式交由主題/樣式字典決定（大多數內建控制項屬此類）。

---

## 加入自訂控制項類別

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/add-custom-control-class

建立繼承 `Control` 的類別，並在 XAML 中透過 XML 命名空間宣告使用：

### 重點程式碼範例（如有）
```xml
<Window xmlns:cc="using:AvaloniaCCExample.CustomControls">
  <cc:MyCustomControl Height="200" Width="300"/>
</Window>
```
```csharp
public class MyCustomControl : Control { }
```

---

## 建立自訂 Panel

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/create-a-custom-panel

繼承 `Panel` 並覆寫 `MeasureOverride` 和 `ArrangeOverride` 方法：

### 重點程式碼範例（如有）
```csharp
public class PlotPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var panelDesiredSize = new Size();
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            panelDesiredSize = child.DesiredSize;
        }
        return panelDesiredSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            double x = 50, y = 50;
            child.Arrange(new Rect(new Point(x, y), child.DesiredSize));
        }
        return finalSize;
    }
}
```

---

## 自訂控制項定義 Styled Property

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/defining-properties

用 `AvaloniaProperty.Register` 或 `AddOwner` 建立 Styled Property，名稱慣例為 `[AttributeName]Property`：

### 重點程式碼範例（如有）
```csharp
public static readonly StyledProperty<IBrush?> BackgroundProperty =
    Border.BackgroundProperty.AddOwner<MyCustomControl>();

public IBrush? Background
{
    get { return GetValue(BackgroundProperty); }
    set { SetValue(BackgroundProperty, value); }
}
```

---

## 用屬性繪製自訂控制項

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/draw-with-a-property

覆寫 `Render` 方法使用 `DrawingContext` 繪製控制項，透過 `Bounds.Size` 取得控制項尺寸：

### 重點程式碼範例（如有）
```csharp
public sealed override void Render(DrawingContext context)
{
    if (Background != null)
    {
        var renderSize = Bounds.Size;
        context.FillRectangle(Background, new Rect(renderSize));
    }
    base.Render(context);
}
```

---

## 建立自訂控制項函式庫

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/how-to-create-a-custom-controls-library

1. 建立 Class Library 專案並安裝 Avalonia NuGet 套件
2. 在 .axaml 檔中加入 XML 命名空間宣告
3. 在呼叫專案的 .csproj 加入 ProjectReference

可用 `XmlnsDefinition` attribute 定義友善的 URL 命名空間：

### 重點程式碼範例（如有）
```csharp
[assembly: XmlnsDefinition("https://my.controls.url", "My.NameSpace")]
```
```xml
<ItemGroup>
  <ProjectReference Include="..\CCLibrary\CCLibrary.csproj" />
</ItemGroup>
```

---

## 建立自訂 Flyout

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/how-to-create-a-custom-flyout

繼承 `FlyoutBase` 並覆寫 `CreatePresenter()` 方法：

### 重點程式碼範例（如有）
```csharp
public class MyImageFlyout : FlyoutBase
{
    public static readonly StyledProperty<IImage> ImageProperty =
        AvaloniaProperty.Register<MyImageFlyout, IImage>(nameof(Image));

    [Content]
    public IImage Image { get; set; }

    protected override Control CreatePresenter()
    {
        return new FlyoutPresenter
        {
            Content = new Image { [!Image.SourceProperty] = this[!ImageProperty] }
        };
    }
}
```

---

## 進階自訂控制項：StyledProperty、DirectProperty、AttachedProperty

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/how-to-create-advanced-custom-controls

- **StyledProperty**：支援樣式系統，用 `AvaloniaProperty.Register` 或 `AddOwner` 建立。
- **DirectProperty**：輕量版，用 `RegisterDirect`，支援 DataValidation，但不支援繼承與樣式。
- **ReadOnly Property**：用 `RegisterDirect` 只傳 getter，以 `SetAndRaise` 通知變更。
- **DataValidation**：`DirectProperty` 加 `enableDataValidation: true` 參數。

### 重點程式碼範例（如有）
```csharp
// DirectProperty 範例
public static readonly DirectProperty<MyControl, IEnumerable> ItemsProperty =
    AvaloniaProperty.RegisterDirect<MyControl, IEnumerable>(
        nameof(Items), o => o.Items, (o, v) => o.Items = v);
```

---

## 建立 Attached Properties

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/how-to-create-attached-properties

用 `AvaloniaProperty.RegisterAttached` 建立附加屬性，搭配 Changed 事件實作 Behavior：

### 重點程式碼範例（如有）
```csharp
public class DoubleTappedBehav : AvaloniaObject
{
    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<DoubleTappedBehav, Interactive, ICommand>(
            "Command", default, false, BindingMode.OneTime);

    public static void SetCommand(AvaloniaObject element, ICommand value)
        => element.SetValue(CommandProperty, value);
    public static ICommand GetCommand(AvaloniaObject element)
        => element.GetValue(CommandProperty);
}
```

---

## 建立 Templated 控制項的 Data Binding

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/how-to-create-templated-controls

在控制項範本中使用 `TemplateBinding` 或 `RelativeSource=TemplatedParent` 綁定父控制項屬性：

### 重點程式碼範例（如有）
```xml
<TextBlock Text="{TemplateBinding Caption}"/>
<!-- 等同於 -->
<TextBlock Text="{Binding Caption, RelativeSource={RelativeSource TemplatedParent}}"/>
```
注意：`TemplateBinding` 僅支援 `OneWay` 模式；若需 `TwoWay`，需改用完整 Binding 語法。

---

## 控制項類型選擇

**來源：** https://docs.avaloniaui.net/docs/guides/custom-controls/types-of-control

- **UserControl**：最簡單，適合應用程式特定的頁面/視圖，類似 Window 的開發方式。
- **TemplatedControl**：無外觀，可由主題重新樣式化，繼承 `TemplatedControl`（非 WPF 的 `Control`）。
- **Basic Control**：繼承 `Control`，覆寫 `Visual.Render` 自行繪製（如 TextBlock、Image）。

---

## 存取 UI 執行緒

**來源：** https://docs.avaloniaui.net/docs/guides/development-guides/accessing-the-ui-thread

使用 `Dispatcher.UIThread` 從背景執行緒更新 UI：
- `Post`：「發射後不管」，不等待結果。
- `InvokeAsync`：等待執行並可取得結果。

### 重點程式碼範例（如有）
```csharp
// Fire-and-forget
Dispatcher.UIThread.Post(() => SetText(text));

// 等待結果
var result = await Dispatcher.UIThread.InvokeAsync(GetText);
```

---

## 資料驗證

**來源：** https://docs.avaloniaui.net/docs/guides/development-guides/data-validation

Avalonia 提供三種驗證外掛：
1. **DataAnnotations**：使用 `[Required]`、`[EmailAddress]` 等屬性裝飾 ViewModel 屬性。
2. **INotifyDataErrorInfo**：支援 ReactiveUI.Validation、CommunityToolkit.Mvvm 等框架。
3. **Exception**：在 property setter 中拋出例外。

自訂 `DataValidationErrors` 樣式可改變錯誤訊息的呈現方式。

### 重點程式碼範例（如有）
```csharp
[Required]
[EmailAddress]
public string? EMail
{
    get => _EMail;
    set => this.RaiseAndSetIfChanged(ref _EMail, value);
}
```

---

## 實作多頁面應用程式

**來源：** https://docs.avaloniaui.net/docs/guides/development-guides/how-to-implement-multi-page-apps

使用 `ViewLocator` 搭配 UserControl 作為頁面視圖：

### 重點程式碼範例（如有）
```csharp
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);
        return type != null ? (Control)Activator.CreateInstance(type)! : new TextBlock { Text = "Not Found: " + name };
    }
    public bool Match(object? data) => data is ViewModelBase;
}
```

---

## 效能最佳化建議

**來源：** https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance

- 使用 **CompiledBindings**：編譯時期解析 binding path，避免 reflection 開銷。
- 使用 **TreeDataGrid**（替代 DataGrid）：內建虛擬化，適合大量資料。
- **啟用虛擬化**：只渲染可見項目。
- **扁平化視覺樹**：減少巢狀層級，降低 layout pass 計算量。
- **避免大量 Run 元素**於 TextBlock 中。
- 使用 `StreamGeometry` 取代 `PathGeometry`（效能更佳）。
- 縮小圖片尺寸：避免載入大圖後縮放。
- **解決 Binding Errors**：每個 binding 錯誤都造成效能損耗。
- 非同步載入資料，避免凍結 UI 執行緒。

---

## 漸層背景（LinearGradientBrush）

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/gradients

用 `LinearGradientBrush` 建立線性漸層效果，透過 `StartPoint`/`EndPoint` 設定方向：

### 重點程式碼範例（如有）
```xml
<!-- 水平漸層 -->
<LinearGradientBrush StartPoint="0%,50%" EndPoint="100%,50%">
    <GradientStop Color="#FF6B6B" Offset="0.0"/>
    <GradientStop Color="#4ECDC4" Offset="1.0"/>
</LinearGradientBrush>

<!-- 垂直漸層 -->
<LinearGradientBrush StartPoint="50%,0%" EndPoint="50%,100%">
    <GradientStop Color="#A8E6CF" Offset="0.0"/>
    <GradientStop Color="#3D84A8" Offset="1.0"/>
</LinearGradientBrush>
```

---

## 圖形與動畫概覽

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/graphics-and-animations

Avalonia 圖形特性：裝置無關像素（1/96 英吋）、雙精度座標系統、使用 Skia 渲染引擎（同 Chrome/Android）。

提供 `Ellipse`、`Line`、`Path`、`Polygon`、`Rectangle` 等 2D 圖形控制項，支援透明度合成與 hit-testing。

---

## 為選單項目加入圖示

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/how-to-add-menu-icons

使用 `MenuItem.Icon` 屬性設定圖示：

### 重點程式碼範例（如有）
```xml
<MenuItem Header="Open" Command="{Binding OpenCommand}">
    <MenuItem.Icon>
        <Image Width="16" Height="16" Source="avares://MyApp/Assets/open_icon.png" />
    </MenuItem.Icon>
</MenuItem>
```

---

## 使用圖示的三種方式

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/how-to-use-icons

1. **圖片檔**：`<Image Source="avares://MyApp/Assets/icon.png" />`
2. **圖示字型**：`<TextBlock FontFamily="avares://MyApp/Assets/#FontAwesome" Text="&#xf030;" />`
3. **Path Icon**：使用 `PathIcon` 控制項搭配 SVG Geometry。

---

## Keyframe 動畫

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/keyframe-animations

在 Style 中定義 `Animation` 元素，使用 `KeyFrame` 和 `Cue` 設定時間點的屬性值：

### 重點程式碼範例（如有）
```xml
<Style Selector="Rectangle.red">
    <Setter Property="Fill" Value="Red"/>
    <Style.Animations>
        <Animation Duration="0:0:3">
            <KeyFrame Cue="0%">
                <Setter Property="Opacity" Value="0.0"/>
            </KeyFrame>
            <KeyFrame Cue="100%">
                <Setter Property="Opacity" Value="1.0"/>
            </KeyFrame>
        </Animation>
    </Style.Animations>
</Style>
```

---

## 頁面轉場：CrossFade

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/page-transitions/cross-fade-page-transition

淡出舊頁面、淡入新頁面的轉場效果：

### 重點程式碼範例（如有）
```xml
<CrossFade Duration="0:00:00.500" />
```

---

## 自訂頁面轉場

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/page-transitions/how-to-create-a-custom-page-transition

實作 `IPageTransition` 介面並覆寫 `Start` 方法，可使用 `Animation` 類別製作動畫：

### 重點程式碼範例（如有）
```csharp
public class CustomTransition : IPageTransition
{
    public TimeSpan Duration { get; set; }
    public async Task Start(Visual from, Visual to, bool forward, CancellationToken cancellationToken)
    {
        // 使用 Animation + KeyFrame 實作縮放轉場
    }
}
```

---

## 頁面滑動轉場

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/page-transitions/page-slide-transition

### 重點程式碼範例（如有）
```xml
<PageSlide Duration="0:00:00.500" Orientation="Vertical" />
```
```csharp
var transition = new PageSlide(TimeSpan.FromMilliseconds(500), PageSlide.SlideAxis.Vertical);
```

---

## 組合頁面轉場

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/page-transitions/page-transition-combinations

使用 `CompositePageTransition` 組合多個轉場效果（如對角滑動 + 淡入淡出）：

### 重點程式碼範例（如有）
```xml
<CompositePageTransition>
    <CrossFade Duration="0:00:00.500" />
    <PageSlide Duration="0:00:00.500" Orientation="Horizontal" />
    <PageSlide Duration="0:00:00.500" Orientation="Vertical" />
</CompositePageTransition>
```

---

## Transitions（屬性過渡動畫）

**來源：** https://docs.avaloniaui.net/docs/guides/graphics-and-animation/transitions

監聽屬性值變化並產生平滑動畫，在控制項的 `Transitions` 屬性中定義：

### 重點程式碼範例（如有）
```xml
<Rectangle.Transitions>
    <Transitions>
        <DoubleTransition Property="Opacity" Duration="0:0:0.2"/>
    </Transitions>
</Rectangle.Transitions>
```

各型別對應：`DoubleTransition`（double）、`BrushTransition`（IBrush）、`ColorTransition`（Color）、`ThicknessTransition`（Thickness）等。

RenderTransform 過渡使用 CSS-like 語法（如 `rotate(45deg)`）搭配 `TransformOperationsTransition`。

---

## 支援的平台

**來源：** https://docs.avaloniaui.net/docs/overview/supported-platforms

Avalonia 支援以下平台：Windows（7/8/8.1/10/11）、macOS（10.14+）、Linux（透過 X11 或 Framebuffer）、iOS（12+）、Android（5.0+）、WebAssembly（瀏覽器）。

---

## 什麼是 Avalonia

**來源：** https://docs.avaloniaui.net/docs/overview/what-is-avalonia

Avalonia 是一個跨平台的 .NET UI 框架，受 WPF 啟發，採用 XAML 定義 UI 並支援資料綁定、樣式、動畫等豐富功能。它允許開發者以一套程式碼建構在 Windows、macOS、Linux、iOS、Android 和 WebAssembly 上執行的應用程式。

---

## Stay Up To Date 版本更新索引

**來源：** https://docs.avaloniaui.net/docs/stay-up-to-date/

Avalonia 版本更新資訊索引，包含重大變更說明、從 0.10 升級指南、新功能說明。

---

## Breaking Changes Avalonia 11 重大變更

**來源：** https://docs.avaloniaui.net/docs/stay-up-to-date/breaking-changes

此頁面列出 Avalonia 11 的重大變更（breaking changes）。雖然主要版本盡量避免破壞 API，但部分行為變更可能導致應用程式無法正常運作。從 0.10 升級到 11 的詳細說明請參考升級指南。

---

## Upgrade from 0.10 從 0.10 升級至 11

**來源：** https://docs.avaloniaui.net/docs/stay-up-to-date/upgrade-from-0.10

Avalonia 11 有許多從 0.10 的重大變更，主要包括：

**專案更新步驟：**
1. 更新 Avalonia 套件至 11.x
2. 主題不再包含在 Avalonia.Desktop，需個別安裝 `Avalonia.Themes.Fluent` 或 `Avalonia.Themes.Simple`
3. 移除 `XamlNameReferenceGenerator` 套件（現已內建）
4. LangVersion 需至少為 9
5. 如需 Inter 字型，加入 `Avalonia.Fonts.Inter` 並呼叫 `.WithInterFont()`

**主題處理（Theme Handling）：** FluentTheme 不再需要 `Mode` 屬性，改用 Application 的 `RequestedThemeVariant`（Default/Dark/Light）。

**重要介面移除：** `IControl` → `Control`、`IVisual` → `Visual`、`IPanel` → `Panel` 等，全部改為具體型別。

**`IStyleable` 棄用：** 改用 `protected override Type StyleKeyOverride => typeof(Button)`。

**Views（視圖）變更：** 類別改為 `partial`，移除手動 `InitializeComponent()` 方法，移除 `this.AttachDevTools()`，XAML 中命名的控制項現在自動產生欄位。

**ItemsControl 變更：** `Items` 改為 `ItemsSource`；移除 `ListBox.VirtualizationMode`，改用 `VirtualizingStackPanel`。

**System.Reactive：** Avalonia 11 不再依賴 System.Reactive，如需使用須自行加入套件。

### 重點程式碼範例（如有）
```xml
<!-- v11 Application.axaml -->
<Application RequestedThemeVariant="Default">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```
```csharp
// 舊版
class MyButton : Button, IStyleable
{
    Type IStyleable.StyleKey => typeof(Button);
}
// 新版
class MyButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
}
```

---

## What's New in Avalonia 11 Avalonia 11 新功能

**來源：** https://docs.avaloniaui.net/docs/stay-up-to-date/whats-new

Avalonia 11.0 的主要新功能：

- **A11y（無障礙存取）**：大幅改善無障礙工具支援
- **IME 支援**：支援輸入法編輯器（輸入不在鍵盤上的字元）
- **Composition Renderer**：全新合成渲染器，更高效彈性
- **WebAssembly (WASM)**：應用程式可直接在瀏覽器中執行
- **iOS 和 Android 支援**：正式支援兩大行動平台
- **Text Inlines**：支援複雜格式化文字區塊（超連結、標註等）
- **Smooth Virtualization**：全新 ItemsControl，流暢虛擬化
- **效能提升**：顯著提升速度和效率
- **Control Themes、巢狀樣式、主題變體**
- **Bitmap Effects**：模糊、陰影等點陣圖效果
- **3D 變換**
- **AOT 編譯和程式碼修剪**：更快執行、更小應用程式
- **GPU Interop**：GPU 互操作改善渲染效能
- **Metal 支援（實驗性）**：改善 iOS/macOS 效能

---

## Welcome 歡迎頁面

**來源：** https://docs.avaloniaui.net/docs/welcome

Avalonia 是使用 .NET 建立跨平台應用程式的強大框架，使用自有渲染引擎確保在各平台（Windows、macOS、Linux、Android、iOS、WebAssembly）上外觀和行為一致。

應用程式使用 C# 或 F# 撰寫，UI 可使用程式碼 API 或 XAML（搭配 code-behind）定義。

**主要資源：**
- 入門教學（Get Started）
- 實作指南（Guides）
- 進階概念（Concepts）
- 從 WPF 移植指南
- 參考文件（Reference）
- 社群支援（Gitter、Telegram）

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter10 — Assets, Fonts, and Resources

**avares:// URI 與專案結構**
```xml
<!-- .csproj -->
<ItemGroup>
  <AvaloniaResource Include="Assets/**" />
</ItemGroup>
```

URI 格式：`avares://<AssemblyName>/<RelativePath>`
範例：`avares://InputPlayground/Assets/Images/logo.png`

**XAML 載入資源**
```xml
<Image Source="avares://AssetsDemo/Assets/Images/logo.png"
       Stretch="Uniform" Width="160"/>
```

**程式碼載入資源**
```csharp
var uri = new Uri("avares://AssetsDemo/Assets/Images/logo.png");
var assetLoader = AvaloniaLocator.Current.GetRequiredService<IAssetLoader>();
await using var stream = assetLoader.Open(uri);
LogoImage.Source = new Bitmap(stream);
```

**點陣圖優化**
```xml
<Image Source="avares://AssetsDemo/Assets/Images/photo.jpg"
       Width="240" Height="160"
       RenderOptions.BitmapInterpolationMode="HighQuality"/>
```

```csharp
// 解碼至目標寬度節省記憶體
using var decoded = Bitmap.DecodeToWidth(stream, 512);
PhotoImage.Source = decoded;
```

**ImageBrush（橢圓頭像、磁磚背景）**
```xml
<Ellipse Width="96" Height="96">
  <Ellipse.Fill>
    <ImageBrush Source="avares://AssetsDemo/Assets/Images/avatar.png"
                Stretch="UniformToFill"/>
  </Ellipse.Fill>
</Ellipse>

<Border Width="200" Height="120">
  <Border.Background>
    <ImageBrush Source="avares://AssetsDemo/Assets/Images/pattern.png"
                TileMode="Tile" Stretch="None"/>
  </Border.Background>
</Border>
```

**向量圖形（Path + StreamGeometry）**
```xml
<Path Data="M2 12 L9 19 L22 4"
      Stroke="{DynamicResource AccentBrush}"
      StrokeThickness="3"
      StrokeLineCap="Round" StrokeLineJoin="Round"/>
```

```csharp
// 程式化建立向量路徑
var geometry = new StreamGeometry();
using (var ctx = geometry.Open())
{
    ctx.BeginFigure(new Point(2, 12), isFilled: false);
    ctx.LineTo(new Point(9, 19));
    ctx.LineTo(new Point(22, 4));
    ctx.EndFigure(isClosed: false);
}
IconPath.Data = geometry;
```

**自訂字型設定**
```xml
<Application.Resources>
  <FontFamily x:Key="HeadingFont">avares://AssetsDemo/Assets/Fonts/Inter.ttf#Inter</FontFamily>
</Application.Resources>
<Application.Styles>
  <Style Selector="TextBlock.h1">
    <Setter Property="FontFamily" Value="{StaticResource HeadingFont}"/>
    <Setter Property="FontSize" Value="28"/>
  </Style>
</Application.Styles>
```

**FontManagerOptions（全域字型設定）**
```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .With(new FontManagerOptions
    {
        DefaultFamilyName = "avares://AssetsDemo/Assets/Fonts/Inter.ttf#Inter",
        FontFallbacks = new[] { new FontFallback { Family = "Segoe UI" } }
    })
    .StartWithClassicDesktopLifetime(args);
```

**執行期動態資源更新**
```csharp
// 更換資源
Application.Current!.Resources["AvatarFallbackBrush"] = new SolidColorBrush(Color.Parse("#3B82F6"));

// 訂閱資源變更通知
Application.Current.Resources.ResourcesChanged += (_, _) => { /* 重新整理 UI */ };
```

---

### Chapter13 — Misc: System Integrations

*(已在 navigation.md Chapter13 中詳細說明)*

---

### Chapter15 — Accessibility and Internationalization

**Tab 導覽焦點順序**
```xml
<StackPanel Spacing="8" KeyboardNavigation.TabNavigation="Cycle">
  <TextBlock Text="_User name" RecognizesAccessKey="True"/>
  <TextBox x:Name="UserName" TabIndex="0"/>
  <TextBlock Text="_Password" RecognizesAccessKey="True"/>
  <PasswordBox x:Name="Password" TabIndex="1"/>
  <Button TabIndex="2">
    <AccessText Text="_Sign in"/>
  </Button>
</StackPanel>
```

**AutomationProperties（螢幕閱讀器語義）**
```xml
<TextBlock x:Name="EmailLabel" Text="Email"/>
<TextBox Text="{Binding Email}"
         AutomationProperties.LabeledBy="{Binding #EmailLabel}"
         AutomationProperties.AutomationId="EmailInput"/>
<TextBlock Text="{Binding Status}"
           AutomationProperties.LiveSetting="Polite"/>
```

**自訂 AutomationPeer**
```csharp
public class ProgressBadge : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ProgressBadge, string?>(nameof(Text));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    protected override AutomationPeer? OnCreateAutomationPeer()
        => new ProgressBadgeAutomationPeer(this);
}

public sealed class ProgressBadgeAutomationPeer : ControlAutomationPeer
{
    public ProgressBadgeAutomationPeer(ProgressBadge owner) : base(owner) { }
    protected override string? GetNameCore() => (Owner as ProgressBadge)?.Text;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Polite;
}
```

**TextInputOptions（IME 和軟鍵盤）**
```xml
<TextBox Text="{Binding PhoneNumber}"
         InputMethod.TextInputOptions.ContentType="TelephoneNumber"
         InputMethod.TextInputOptions.ReturnKeyType="Done"
         InputMethod.TextInputOptions.IsCorrectionEnabled="False"/>
```

**在地化服務**
```csharp
public sealed class Loc : INotifyPropertyChanged
{
    private CultureInfo _culture = CultureInfo.CurrentUICulture;
    public string this[string key] => Resources.ResourceManager.GetString(key, _culture) ?? key;

    public void SetCulture(CultureInfo culture)
    {
        _culture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

```xml
<!-- App.axaml 中註冊 -->
<Application.Resources>
  <local:Loc x:Key="Loc"/>
</Application.Resources>

<!-- 使用 -->
<TextBlock Text="{Binding [Ready], Source={StaticResource Loc}}"/>
```

**執行期切換語言**
```csharp
var culture = new CultureInfo("fr-FR");
CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
((Loc)Application.Current!.Resources["Loc"]).SetCulture(culture);
```

**RTL 佈局（FlowDirection）**
```xml
<Window FlowDirection="RightToLeft">
  <!-- RTL 語言的視窗佈局 -->
</Window>
```

**多語系字型後備**
```csharp
AppBuilder.Configure<App>()
    .With(new FontManagerOptions
    {
        DefaultFamilyName = "Noto Sans",
        FontFallbacks = new[]
        {
            new FontFallback { Family = "Noto Sans Arabic" },
            new FontFallback { Family = "Noto Sans CJK SC" }
        }
    })
```

---

### Chapter16 — File Storage, Drag & Drop, and Clipboard

**StorageProvider 基礎**
```csharp
var topLevel = TopLevel.GetTopLevel(control);
if (topLevel?.StorageProvider is { } storage)
{
    var options = new FilePickerOpenOptions
    {
        Title = "Open images",
        AllowMultiple = true,
        SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures),
        FileTypeFilter = new[]
        {
            new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg" } }
        }
    };
    var files = await storage.OpenFilePickerAsync(options);
}
```

**非同步讀取檔案**
```csharp
public async Task<string?> ReadTextFileAsync(IStorageFile file, CancellationToken ct)
{
    await using var stream = await file.OpenReadAsync();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return await reader.ReadToEndAsync(ct);
}
```

**儲存檔案**
```csharp
var saveOptions = new FilePickerSaveOptions
{
    Title = "Export report",
    SuggestedFileName = $"report-{DateTime.UtcNow:yyyyMMdd}.csv",
    DefaultExtension = "csv",
    FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
};

var file = await _dialogService.SaveFileAsync(saveOptions);
if (file is not null)
{
    await using var stream = await file.OpenWriteAsync();
    await using var writer = new StreamWriter(stream, Encoding.UTF8);
    await writer.WriteLineAsync("Id,Name,Email");
}
```

**拖放接收**
```xml
<Border AllowDrop="True" DragOver="OnDragOver" Drop="OnDrop">
  <TextBlock Text="Drop files or text"/>
</Border>
```

```csharp
private void OnDragOver(object? sender, DragEventArgs e)
{
    e.DragEffects = e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text)
        ? DragDropEffects.Copy : DragDropEffects.None;
}

private async void OnDrop(object? sender, DragEventArgs e)
{
    var files = await e.Data.GetFilesAsync();
    if (files is not null)
    {
        foreach (var item in files.OfType<IStorageFile>())
        {
            await using var stream = await item.OpenReadAsync();
            // 匯入處理
        }
        return;
    }
    if (e.Data.Contains(DataFormats.Text))
    {
        var text = await e.Data.GetTextAsync();
        // 處理文字
    }
}
```

**發起拖放**
```csharp
private async void DragSource_PointerPressed(object? sender, PointerPressedEventArgs e)
{
    var data = new DataObject();
    data.Set(DataFormats.Text, "Example text");
    var effects = await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
}
```

**剪貼簿服務**
```csharp
public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}

public sealed class ClipboardService : IClipboardService
{
    private readonly TopLevel _topLevel;
    public ClipboardService(TopLevel topLevel) => _topLevel = topLevel;
    public Task SetTextAsync(string text) => _topLevel.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
    public Task<string?> GetTextAsync() => _topLevel.Clipboard?.GetTextAsync() ?? Task.FromResult<string?>(null);
}
```

**多格式剪貼簿**
```csharp
var dataObject = new DataObject();
dataObject.Set(DataFormats.Text, "Plain text");
dataObject.Set("text/html", "<strong>Bold</strong>");
dataObject.Set("application/x-myapp-item", myItemId);
await clipboardService.SetDataObjectAsync(dataObject);
```

**書籤（持久存取）**
```csharp
if (file.CanBookmark)
{
    var bookmarkId = await file.SaveBookmarkAsync();
    // 持久化 bookmarkId 到設定
}
// 之後還原
var restored = await storage.OpenFileBookmarkAsync(bookmarkId);
```

---

### Chapter17 — Async, Background Work, and Networking

**Dispatcher.UIThread 規則**
```csharp
// 必須在 UI 執行緒更新 UI
await Dispatcher.UIThread.InvokeAsync(() => Status = "Ready");

// 指定優先級
Dispatcher.UIThread.Post(
    () => Notifications.Clear(),
    priority: DispatcherPriority.Background);
```

**非同步工作 ViewModel 模式**
```csharp
public sealed class WorkViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public RelayCommand StartCommand { get; }
    public RelayCommand CancelCommand { get; }

    public WorkViewModel()
    {
        StartCommand = new RelayCommand(async _ => await StartAsync(), _ => !IsBusy);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
    }

    private async Task StartAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<double>(value => Progress = value * 100);
        try
        {
            Status = "Processing...";
            await FakeWorkAsync(progress, _cts.Token);
            Status = "Completed";
        }
        catch (OperationCanceledException) { Status = "Canceled"; }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally { IsBusy = false; _cts = null; }
    }

    private static async Task FakeWorkAsync(IProgress<double> progress, CancellationToken ct)
    {
        await Task.Run(async () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(2, ct).ConfigureAwait(false);
                progress.Report((i + 1) / 1000.0);
            }
        }, ct);
    }
}
```

**XAML 進度顯示**
```xml
<StackPanel Spacing="12">
  <ProgressBar Minimum="0" Maximum="100" Value="{Binding Progress}"
               IsIndeterminate="{Binding IsBusy}"/>
  <TextBlock Text="{Binding Status}"/>
  <StackPanel Orientation="Horizontal" Spacing="8">
    <Button Content="Start" Command="{Binding StartCommand}"/>
    <Button Content="Cancel" Command="{Binding CancelCommand}"/>
  </StackPanel>
</StackPanel>
```

**HTTP 客戶端模式**
```csharp
// 重用 HttpClient 避免 Socket 耗盡
public static class ApiClient
{
    public static HttpClient Instance { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };
}

// GET + JSON 串流反序列化
public async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
{
    using var resp = await ApiClient.Instance.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
    resp.EnsureSuccessStatusCode();
    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
    return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
}

// 下載並回報進度
public async Task DownloadAsync(Uri uri, IStorageFile destination, IProgress<double> progress, CancellationToken ct)
{
    using var response = await ApiClient.Instance.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
    var contentLength = response.Content.Headers.ContentLength;
    await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
    await using var fileStream = await destination.OpenWriteAsync();

    var buffer = new byte[81920];
    long totalRead = 0; int read;
    while ((read = await httpStream.ReadAsync(buffer.AsMemory(), ct)) > 0)
    {
        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
        totalRead += read;
        if (contentLength.HasValue) progress.Report(totalRead / (double)contentLength.Value);
    }
}
```

**Reactive 事件流**
```csharp
var pointerStream = Observable
    .FromEventPattern<PointerEventArgs>(
        h => control.PointerMoved += h, h => control.PointerMoved -= h)
    .Select(args => args.EventArgs.GetPosition(control))
    .Throttle(TimeSpan.FromMilliseconds(50))
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(point => PointerPosition = point);

Disposables.Add(pointerStream);
```

**DispatcherTimer（定期工作）**
```csharp
var timer = new DispatcherTimer(
    TimeSpan.FromMinutes(5),
    DispatcherPriority.Background,
    (_, _) => RefreshCommand.Execute(null));
timer.Start();
```

**網路狀態服務**
```csharp
public sealed class NetworkStatusService : INetworkStatusService
{
    public IObservable<bool> ConnectivityChanges { get; }

    public NetworkStatusService()
    {
        ConnectivityChanges = Observable
            .FromEventPattern<NetworkAvailabilityChangedEventHandler, NetworkAvailabilityEventArgs>(
                h => NetworkChange.NetworkAvailabilityChanged += h,
                h => NetworkChange.NetworkAvailabilityChanged -= h)
            .Select(args => args.EventArgs.IsAvailable)
            .StartWith(NetworkInterface.GetIsNetworkAvailable());
    }
}
```

---

### Chapter21 — Headless Testing

#### 關鍵概念

**Headless 平台**
`Avalonia.Headless` 讓 UI 測試在無顯示伺服器的環境（CI/Docker）執行，完全確定性。

**套件組合**
- `Avalonia.Headless` + `Avalonia.Headless.XUnit` 或 `Avalonia.Headless.NUnit`
- `Avalonia.Skia`（僅在需要擷取畫面時）

**測試分層策略**
1. ViewModel 測試（最快，無 Avalonia 依賴）
2. Control 測試（使用 headless 模擬輸入）
3. 視覺回歸測試（擷取 frame 比對）
4. Integration/E2E（少量，完整流程）

**強制渲染 Tick**
輸入操作後呼叫 `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` 刷新排版/Binding。

#### 代碼範例

```csharp
// xUnit 設定 (AssemblyInfo.cs)
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = true,
            UseCpuDisabledRenderLoop = true
        });
}
```

```csharp
// 鍵盤輸入測試
public class TextBoxTests
{
    [AvaloniaFact]
    public async Task TextBox_Receives_Typed_Text()
    {
        var textBox = new TextBox { Width = 200, Height = 24 };
        var window = new Window { Content = textBox };
        window.Show();

        await Dispatcher.UIThread.InvokeAsync(() => textBox.Focus());
        window.KeyTextInput("Avalonia");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.Equal("Avalonia", textBox.Text);
    }
}
```

```csharp
// 滑鼠點擊測試
[AvaloniaFact]
public async Task Button_Click_Executes_Command()
{
    var commandExecuted = false;
    var button = new Button
    {
        Width = 100, Height = 30,
        Content = "Click me",
        Command = ReactiveCommand.Create(() => commandExecuted = true)
    };
    var window = new Window { Content = button };
    window.Show();

    await Dispatcher.UIThread.InvokeAsync(() => button.Focus());
    window.MouseDown(button.Bounds.Center, MouseButton.Left);
    window.MouseUp(button.Bounds.Center, MouseButton.Left);
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();

    Assert.True(commandExecuted);
}
```

```csharp
// Frame 擷取（需搭配 UseSkia + UseHeadlessDrawing=false）
[AvaloniaFact]
public void Border_Renders_Correct_Size()
{
    var border = new Border { Width = 200, Height = 100, Background = Brushes.Red };
    var window = new Window { Content = border };
    window.Show();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();

    using var frame = window.GetLastRenderedFrame();
    Assert.Equal(200, frame.Size.Width);
    Assert.Equal(100, frame.Size.Height);
}
```

---

### Chapter25 — XAML Previewer & Design-Time Data

#### 關鍵概念

**Previewer 管線**
IDE 啟動一個 Preview process，透過 `Avalonia.Remote.Protocol` 串流渲染幀回 IDE。`Design.IsDesignMode` 旗標在 runtime 為 false，只在 previewer 為 true。

**Design.DataContext**
提供輕量 POCO 或設計 ViewModel，讓 Previewer 顯示樣本資料，不影響 Runtime。

**Design.PreviewWith**
在 ResourceDictionary 中提供宿主控件，讓 Previewer 能獨立渲染樣式檔案。

**Design.Width/Height**
設定 Previewer 畫布尺寸，便於模擬手機或特定螢幕尺寸。

#### 代碼範例

```csharp
// 設計時樣本資料
namespace MyApp.Design;

public sealed class SamplePerson
{
    public string Name { get; set; } = "Ada Lovelace";
    public string Email { get; set; } = "ada@example.com";
    public int Age { get; set; } = 37;
}
```

```xml
<!-- 在 XAML 中使用 Design.DataContext -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:design="clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls"
             xmlns:samples="clr-namespace:MyApp.Design" x:Class="MyApp.Views.ProfileView">
  <design:Design.DataContext>
    <samples:SamplePerson/>
  </design:Design.DataContext>
  <StackPanel Spacing="12" Margin="16">
    <TextBlock Classes="h1" Text="{Binding Name}"/>
    <TextBlock Text="{Binding Email}"/>
  </StackPanel>
</UserControl>
```

```xml
<!-- ResourceDictionary 使用 PreviewWith -->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:design="clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls"
                    xmlns:views="clr-namespace:MyApp.Views">
  <design:Design.PreviewWith>
    <Border Padding="16" Background="#1f2937">
      <StackPanel Spacing="8">
        <views:Badge Content="1" Classes="success"/>
        <views:Badge Content="Warning" Classes="warning"/>
      </StackPanel>
    </Border>
  </design:Design.PreviewWith>
</ResourceDictionary>
```

```csharp
// 設計時 IsDesignMode 保護
if (Design.IsDesignMode)
    return; // 跳過服務初始化、計時器、網路呼叫
```

```csharp
// 設計時 DI 覆寫
public static class DesignTimeServices
{
    public static void Register()
    {
        if (!Design.IsDesignMode) return;

        AvaloniaLocator.CurrentMutable
            .Bind<INavigationService>()
            .ToConstant(new FakeNavigationService());
    }
}
```

---

### Chapter27 — Contributing to Avalonia

#### 關鍵概念

**Repository 結構**
- `src/`：核心程式碼（Avalonia.Base、Controls、Markup.Xaml、平台 heads）
- `tests/`：單元、Headless、整合、效能 benchmark 測試
- `samples/`：ControlCatalog 等範例
- `build/NukeBuild`：CI 建置管線

**本機建置**
使用 `build.ps1 --target Test` 或 `dotnet run --project build/NukeBuild` 執行與 CI 相同的管線。

**貢獻 PR 步驟**
1. 確認 CONTRIBUTING.md 分支/風格規範
2. Fork + feature branch
3. 實作修改（小而專注）
4. 新增/更新 `tests/` 下的測試
5. 執行 `dotnet build` + `dotnet test`
6. 更新文件/範例
7. 提交 PR（清楚描述，附 issue ID）

**Source Debugging**
NuGet 套件附帶 SourceLink 元資料，可在 VS/Rider 設定自動下載 `.pdb` 並步進 Avalonia 原始碼。

#### 代碼範例

```bash
# 本機建置
dotnet restore Avalonia.sln
cd src/Avalonia.Controls && dotnet build -c Debug
cd tests/Avalonia.Headless.UnitTests && dotnet test -c Release
cd samples/ControlCatalog && dotnet run
```

---