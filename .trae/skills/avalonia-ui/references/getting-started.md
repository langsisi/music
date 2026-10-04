# Avalonia 入門知識庫

此檔案包含 Avalonia 入門相關文件摘要。

---

## 開始使用 Avalonia

**來源：** https://docs.avaloniaui.net/docs/get-started/

安裝前置需求：.NET 8.0+，推薦使用 JetBrains Rider（macOS/Linux）或 Visual Studio（Windows）。

安裝 Avalonia 範本：
```bash
dotnet new install Avalonia.Templates
```

主要範本：
- `avalonia.app` - 基本 Avalonia 應用程式
- `avalonia.mvvm` - MVVM 架構應用程式
- `avalonia.xplat` - 跨平台應用程式（含 Browser/Mobile）

---

## 學習更多資源

**來源：** https://docs.avaloniaui.net/docs/get-started/learn-more

建議學習路徑：
1. 資料綁定：`/docs/basics/data/data-binding`
2. MVVM 概念：`/docs/concepts/the-mvvm-pattern`
3. 樣式系統：`/docs/basics/user-interface/styling`
4. 實作教學：Todo List App（GitHub Samples）

---

## XAML 預覽器

**來源：** https://docs.avaloniaui.net/docs/get-started/xaml-previewers

XAML 預覽器可在 JetBrains Rider（需安裝 AvaloniaRider 外掛）和 Visual Studio（需安裝 Avalonia for Visual Studio 外掛）中使用，支援即時預覽 AXAML 變更。

使用方式（Rider）：選取 .axaml 檔案 → 點選右上角「Editor and Preview」→ 建置專案。

---

## 從 WPF 遷移到 Avalonia（概覽）

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/

WPF 開發者遷移到 Avalonia 的主要差異點：
- Styling 系統不同（CSS-like Styles + ControlTheme）
- DataTemplates 放置方式不同
- 屬性系統：`DependencyProperty` → `StyledProperty`/`DirectProperty`
- 控制項基底類別名稱不同
- 事件 Tunneling 方式不同

---

## WPF vs Avalonia：Class Handlers

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/class-handlers

### 重點程式碼範例（如有）
```csharp
// WPF 方式（靜態方法）
static MyControl() {
    EventManager.RegisterClassHandler(typeof(MyControl), MyEvent, HandleMyEvent));
}

// Avalonia 方式（非靜態，自動導向正確執行個體）
static MyControl() {
    MyEvent.AddClassHandler<MyControl>((x, e) => x.HandleMyEvent(e));
}
private void HandleMyEvent(RoutedEventArgs e) {}
```

---

## WPF vs Avalonia vs UWP 功能比較

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/comparison-of-avalonia-with-wpf-and-uwp

Avalonia 相比 WPF/UWP 的優勢：
- ✔ 跨平台（Windows、Linux、macOS、iOS、Android、WebAssembly）
- ✔ Compiled Bindings（WPF 無此功能）
- ✔ `x:True`/`x:False` 標記延伸（WPF 無）
- ✔ Binding to ConverterParameter
- ✔ MultiBinding / IMultiValueConverter
- ✔ RelativeSource / AncestorType（完整支援）
- ✔ StringFormat in Binding
- ✔ Implicit DataTemplate（無需 DataTemplateSelector）
- ✔ LayoutTransform

---

## WPF vs Avalonia：DataTemplates 差異

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/datatemplates

Avalonia 的 DataTemplate **不**存放在 `Resources` 中，而是放在控制項的 `DataTemplates` 集合或 `Application.DataTemplates`。

Avalonia 支援對介面和衍生類別使用 DataTemplate（WPF 不支援），因此 DataTemplate 宣告順序很重要——由最具體到最泛化。

可用 `IDataTemplate` 介面取代 WPF 的 `DataTemplateSelector`。

### 重點程式碼範例（如有）
```xml
<UserControl.DataTemplates>
    <DataTemplate DataType="viewmodels:FooViewModel">
        <Border Background="Red" CornerRadius="8">
            <TextBox Text="{Binding Name}"/>
        </Border>
    </DataTemplate>
</UserControl.DataTemplates>
```

---

## WPF vs Avalonia：DependencyProperty

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/dependencyproperty

WPF 的 `DependencyProperty` 在 Avalonia 中對應為 `StyledProperty`，另有 `DirectProperty` 可將標準 CLR 屬性轉為 Avalonia 屬性。兩者的共同基底類別是 `AvaloniaProperty`。

---

## WPF vs Avalonia：Grid

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/grid

Avalonia 支援字串語法定義欄列，更簡潔：

### 重點程式碼範例（如有）
```xml
<Grid ColumnDefinitions="Auto,*,32" RowDefinitions="*,Auto">
```

若只是要疊放兩個控制項，建議使用更輕量的 `Panel` 而非 `Grid`。

---

## WPF vs Avalonia：HierarchicalDataTemplate

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/hierarchicaldatatemplate

WPF 的 `HierarchicalDataTemplate` 在 Avalonia 中稱為 `TreeDataTemplate`，兩者幾乎完全等效，只是名稱不同。

---

## WPF vs Avalonia：PropertyChangedCallback

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/propertychangedcallback

Avalonia 沒有在屬性註冊時的 `PropertyChangedCallback`，改為在控制項的靜態建構子中加入 class listener，方式與事件 class listener 相同。

---

## WPF vs Avalonia：RenderTransformOrigin

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/rendertransforms-and-rendertransformorigin

**重要差異**：Avalonia 的 `RenderTransformOrigin` 預設值為 `RelativePoint.Center`（中心點），而 WPF 預設為 `RelativePoint.TopLeft`（左上角）。若要與 WPF 行為一致，需明確指定 TopLeft。

---

## WPF vs Avalonia：Styling 樣式系統

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/styling

Avalonia 有兩種樣式方式：
1. **Style**：CSS-like，儲存在 `Styles` 集合（非 WPF 的 `Resources`）
2. **ControlTheme**：類似 WPF 的 Style，用於 lookless 控制項主題

### 重點程式碼範例（如有）
```xml
<UserControl.Styles>
    <!-- 使用 h1 樣式類別的 TextBlock 字體大小為 24pt -->
    <Style Selector="TextBlock.h1">
        <Setter Property="FontSize" Value="24"/>
    </Style>
</UserControl.Styles>
<TextBlock Classes="h1">Header</TextBlock>
```

---

## WPF vs Avalonia：Tunnelling Events

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/tunnelling-events

Avalonia 有 Tunnelling 事件，但不像 WPF 有獨立的 `Preview*` CLR 事件處理器，需使用 `AddHandler` 並指定 `RoutingStrategies.Tunnel`：

### 重點程式碼範例（如有）
```csharp
target.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

void OnPreviewKeyDown(object sender, KeyEventArgs e)
{
    // Handler code
}
```

---

## WPF vs Avalonia：UIElement/FrameworkElement/Control 對應

**來源：** https://docs.avaloniaui.net/docs/get-started/wpf/uielement-frameworkelement-and-control

類別對應關係：
- WPF `UIElement` → Avalonia `Control`
- WPF `FrameworkElement` → Avalonia `Control`
- WPF `Control`（templated）→ Avalonia `TemplatedControl`

建立自訂控制項時：
- 自訂繪製控制項：繼承 `Control`
- 樣板化控制項：繼承 `TemplatedControl`

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter01 — Introduction to Avalonia and MVVM

**Avalonia 簡介**
Avalonia 是開源、跨平台 UI 框架，一套程式碼可以部署到 Windows、macOS、Linux、Android、iOS 和 WebAssembly。提供 Fluent 主題、豐富資料繫結和 DevTools。

**架構分層**
- `Avalonia.Base`：依賴屬性（`AvaloniaProperty`）、執行緒、佈局基礎
- `Avalonia.Controls`：控制項集合、視窗、應用程式生命週期
- `Avalonia.Markup.Xaml`：XAML 解析器、編譯 XAML
- Platform backends：Win32、macOS Native、X11、Android、iOS、Browser

**MVVM 核心概念**
- `INotifyPropertyChanged`：ViewModel 屬性變更通知繫結 UI
- `AvaloniaProperty`：依賴屬性，支援樣式、動畫、模板化
- Commands：`ICommand` 實作，讓按鈕/選單觸發邏輯
- Data Templates：定義 ViewModel 在清單/導覽中的渲染方式

**AppBuilder 啟動流程**
```csharp
// Program.cs
[STAThread]
public static void Main(string[] args) => BuildAvaloniaApp()
    .StartWithClassicDesktopLifetime(args);

public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()  // 選擇正確的後端
        .UseSkia()            // Skia 渲染管線
        .LogToTrace();        // 診斷日誌
```

**DataContext 繼承**
DataContext 沿邏輯樹向下繼承（Window → Grid → TextBlock）。Popup 等位於樹外的控制項不會自動繼承，需手動指定。

**與其他框架比較**
- vs WPF：保留同等 XAML 開發體驗，但支援跨平台
- vs WinUI3：不限 Windows-only，跨平台優先
- vs .NET MAUI：更強調桌面一致性與 Skia 像素級渲染
- vs Uno Platform：單一 Skia 渲染管線，視覺跨平台一致

---

### Chapter02 — Setting Up Your Development Environment

**SDK 安裝與工作負載**
```bash
dotnet --version           # 確認 .NET SDK 版本
dotnet --list-sdks

# 安裝額外工作負載
dotnet workload install wasm-tools   # WebAssembly
dotnet workload install android      # Android
dotnet workload install ios          # iOS/macOS Catalyst
dotnet workload restore              # 還原已宣告的工作負載
```

**安裝 Avalonia 專案範本**
```bash
dotnet new install Avalonia.Templates
dotnet new list avalonia    # 確認安裝
```

**範本速查表**

| 範本 | 指令 | 適用情境 |
|------|------|---------|
| 桌面（code-behind）| `dotnet new avalonia.app -n MyApp` | 小型原型 |
| MVVM starter | `dotnet new avalonia.mvvm -n MyApp.Mvvm` | MVVM 繫結 |
| ReactiveUI | `dotnet new avalonia.reactiveui -n MyApp.ReactiveUI` | ReactiveUI |
| 跨平台 | `dotnet new avalonia.app --multiplatform -n MyApp.Multi` | 桌面+行動+瀏覽器 |
| 控制項函式庫 | `dotnet new avalonia.library -n MyApp.Controls` | 可重用 UI 庫 |

**建立並執行第一個專案**
```bash
mkdir HelloAvalonia && cd HelloAvalonia
dotnet new avalonia.app -o HelloAvalonia.Desktop
cd HelloAvalonia.Desktop
dotnet build
dotnet run
```

**最小可視化 XAML**
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="HelloAvalonia.MainWindow"
        Width="400" Height="260"
        Title="Hello Avalonia!">
  <StackPanel Margin="16" Spacing="12">
    <TextBlock Text="It works!" FontSize="24"/>
    <Button Content="Click me" HorizontalAlignment="Left"/>
  </StackPanel>
</Window>
```

**從原始碼建置 Avalonia**
```bash
git clone https://github.com/AvaloniaUI/Avalonia.git
# Windows:
.\build.ps1 -Target Build
# macOS/Linux:
./build.sh --target=Build
# 執行 ControlCatalog:
dotnet run --project samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj
```

**疑難排解**
- `dotnet` 指令不存在：重新安裝 SDK，確認 PATH
- 範本找不到：重新執行 `dotnet new install Avalonia.Templates`
- NuGet 還原問題：`dotnet nuget locals all --clear`
- 預覽器失敗：先 build 一次，查看 Output 視窗

---

### Chapter03 — Building Your First UI with XAML

**XAML 命名空間宣告**
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:SampleUiBasics.Views"
        x:Class="SampleUiBasics.Views.MainWindow">
```

**主要佈局（StackPanel + Grid + DockPanel）**
```xml
<DockPanel LastChildFill="True" Margin="16">
  <TextBlock DockPanel.Dock="Top" Text="Customer overview" Margin="0,0,0,16"/>
  <Grid ColumnDefinitions="2*,3*" RowDefinitions="Auto,*" ColumnSpacing="16" RowSpacing="16">
    <!-- 左欄：表單 -->
    <StackPanel Grid.Column="0" Spacing="8">
      <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto" RowSpacing="8" ColumnSpacing="12">
        <TextBlock Text="Name:"/>
        <TextBox Grid.Column="1" Width="200" Text="{Binding Customer.Name}"/>
        <TextBlock Grid.Row="1" Text="Email:"/>
        <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding Customer.Email}"/>
      </Grid>
    </StackPanel>
    <!-- 右欄：清單 -->
    <StackPanel Grid.Column="1" Spacing="8">
      <ItemsControl Items="{Binding RecentOrders}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <ui:OrderRow />
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </Grid>
</DockPanel>
```

**建立可重用 UserControl（OrderRow）**
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="SampleUiBasics.Views.OrderRow">
  <Border CornerRadius="6" Padding="12">
    <Grid ColumnDefinitions="*,Auto" RowDefinitions="Auto,Auto" ColumnSpacing="12">
      <TextBlock Text="{Binding Title}"/>
      <TextBlock Grid.Column="1"
                 Text="{Binding Total, Converter={StaticResource CurrencyConverter}}"/>
      <TextBlock Grid.Row="1" Grid.ColumnSpan="2"
                 Text="{Binding PlacedOn, StringFormat='Ordered on {0:d}'}"/>
    </Grid>
  </Border>
</UserControl>
```

**值轉換器（Value Converter）**
```csharp
public sealed class CurrencyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal amount)
            return string.Format(culture, "{0:C}", amount);
        return value;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
```

在 `App.axaml` 中註冊：
```xml
<Application.Resources>
  <converters:CurrencyConverter x:Key="CurrencyConverter"/>
</Application.Resources>
```

**ViewModel 資料結構**
```csharp
public sealed class MainWindowViewModel
{
    public CustomerViewModel Customer { get; } = new("Avery Diaz", "avery@example.com");
    public ObservableCollection<OrderViewModel> RecentOrders { get; } = new()
    {
        new OrderViewModel("Starter subscription", 49.00m, DateTime.Today.AddDays(-2)),
        new OrderViewModel("Design add-on", 129.00m, DateTime.Today.AddDays(-12)),
    };
}
public sealed record CustomerViewModel(string Name, string Email);
public sealed record OrderViewModel(string Title, decimal Total, DateTime PlacedOn);
```

**邏輯樹 vs 視覺樹**
- 邏輯樹：內容關係，繫結和資源查找沿邏輯樹走
- 視覺樹：模板產生的實際視覺元素
- 用 DevTools（F12）→ Logical/Visual 分頁檢查

**FindControl 與 NameScope**
```csharp
// 名稱在 UserControl 內是局部的，不跨越邊界
var ordersList = this.FindControl<ItemsControl>("OrdersList");
```

---

### Chapter04 — AppBuilder, Lifetimes, and Startup

**AppBuilder 管線**
```csharp
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()       // 選擇 Application 子類別
        .UsePlatformDetect()             // 偵測後端（Win32/macOS/X11/Android/iOS/Browser）
        .UseSkia()                       // Skia 渲染管線
        .With(new SkiaOptions { MaxGpuResourceSizeBytes = 96 * 1024 * 1024 })
        .LogToTrace()                    // 啟動前掛接日誌
        .UseReactiveUI();                // 選用 ReactiveUI 整合
```

**生命週期類型對照**

| 類型 | 適用平台 | 關鍵成員 |
|------|---------|---------|
| `ClassicDesktopStyleApplicationLifetime` | Windows/macOS/Linux | `MainWindow`, `ShutdownMode`, `Exit` |
| `SingleViewApplicationLifetime` | Android/iOS/嵌入 | `MainView`, `OnMainViewClosed` |
| `BrowserSingleViewLifetime` | WebAssembly | `MainView`，非同步初始化 |
| `HeadlessApplicationLifetime` | 單元/UI 測試 | `TryGetTopLevel()`，手動 pump |

**在 App.axaml.cs 中連接生命週期**
```csharp
public partial class App : Application
{
    private IServiceProvider? _services;

    public override void OnFrameworkInitializationCompleted()
    {
        _services ??= ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => (_services as IDisposable)?.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = _services.GetRequiredService<MainView>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainView>();
        services.AddSingleton<DashboardViewModel>();
        return services.BuildServiceProvider();
    }
}
```

**全域例外捕捉**
```csharp
[STAThread]
public static void Main(string[] args)
{
    AppDomain.CurrentDomain.UnhandledException += (_, e) => LogFatal(e.ExceptionObject);
    TaskScheduler.UnobservedTaskException += (_, e) => LogFatal(e.Exception);
    Dispatcher.UIThread.UnhandledException += (_, e) =>
    {
        LogFatal(e.Exception);
        e.Handled = true;
    };
    try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
    catch (Exception ex) { LogFatal(ex); throw; }
}
```

**多生命週期切換**
```csharp
public static void Main(string[] args)
{
#if HEADLESS
    BuildAvaloniaApp().Start(AppMain);
#elif BROWSER
    BuildAvaloniaApp().SetupBrowserApp("app");
#else
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#endif
}
```

---

### Chapter33 — Code-First App Bootstrap

#### 關鍵概念

**無 XAML 的 Application 建置**
1. 覆寫 `Initialize()`：手動加入 `FluentTheme`、`StyleInclude`、`ResourceDictionary`
2. 覆寫 `RegisterServices()`：DI 容器設定（在 `Initialize` 之前執行）
3. 覆寫 `OnFrameworkInitializationCompleted()`：依 Lifetime 設定 MainWindow 或 MainView

**AppBuilder 設定**
- `.UsePlatformDetect()`：自動選擇後端
- `.UseReactiveUI()`：ReactiveUI scheduler/command binding
- `.With<TOptions>()`：後端特定選項（唯一設定入口，因為沒有 `App.axaml`）
- `.UseSkia()`：顯式指定 Skia 渲染

**主題切換**
清除 `Application.Current.Styles` 再依序加入新主題的 `IStyle` 項目。

**模組化目錄結構**
每個功能模組含 `View.cs`、`ViewModel.cs`、`Styles.cs`（`Styles` 屬性），在模組啟用時 merge 至 Application.Styles。

#### 代碼範例

```csharp
// Program.cs
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition }
            })
            .With(new X11PlatformOptions { EnableIme = true })
            .UseSkia();
}
```

```csharp
// App.cs（無 XAML）
public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Clear();
        Styles.Add(new FluentTheme { Mode = FluentThemeMode.Dark });
        Styles.Add(new StyleInclude(new Uri("avares://App/Styles"))
        {
            Source = new Uri("avares://App/Styles/Controls.axaml")
        });
        Styles.Add(CreateButtonStyle());
        Resources.MergedDictionaries.Add(CreateAppResources());
    }

    protected override void RegisterServices()
    {
        AvaloniaLocator.CurrentMutable.Bind<IMyService>().ToSingleton<MyService>();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new HomeView { DataContext = new HomeViewModel() };

        base.OnFrameworkInitializationCompleted();
    }

    private static Style CreateButtonStyle()
        => new(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(Button.PaddingProperty, new Thickness(16, 8))
            }
        };

    private static ResourceDictionary CreateAppResources() => new()
    {
        ["AccentBrush"] = new SolidColorBrush(Color.Parse("#FF4F8EF7")),
        ["AccentForegroundBrush"] = Brushes.White,
        ["BorderRadiusSmall"] = new CornerRadius(4)
    };
}
```

```csharp
// 主題切換
public void UseDarkTheme()
{
    Application.Current!.Styles.Clear();
    foreach (var style in AppTheme.Dark)
        Application.Current.Styles.Add(style);
}
```

```csharp
// Microsoft.Extensions.DependencyInjection 整合
protected override void RegisterServices()
{
    var services = new ServiceCollection();
    services.AddSingleton<IMyService, MyService>();
    services.AddSingleton<HomeViewModel>();
    var provider = services.BuildServiceProvider();

    AvaloniaLocator.CurrentMutable
        .Bind<IMyService>()
        .ToSingleton(() => provider.GetRequiredService<IMyService>());
}
```

---