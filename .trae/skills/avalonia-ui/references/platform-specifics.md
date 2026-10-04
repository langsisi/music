# Avalonia 平台特定知識庫

此檔案包含 Avalonia 跨平台部署和平台特定功能相關文件摘要。

---

## 部署概覽

**來源：** https://docs.avaloniaui.net/docs/deployment/

Avalonia 支援多種部署方式，主要針對 Debian/Ubuntu Linux、macOS 和 Native AOT 有詳細指南。

---

## 部署到 Debian/Ubuntu

**來源：** https://docs.avaloniaui.net/docs/deployment/debian-ubuntu

Avalonia Linux 程式可透過雙擊或命令列執行，但建議封裝成 `.deb` 以提供更好的使用者體驗（桌面捷徑、加入 PATH）。

**.deb 套件結構：**
```
staging_folder/
├── DEBIAN/
│   └── control          # 套件控制檔
└── usr/
    ├── bin/
    │   └── myprogram    # 啟動腳本
    ├── lib/
    │   └── myprogram/   # dotnet publish 輸出的所有檔案
    └── share/
        ├── applications/
        │   └── MyProgram.desktop   # 桌面捷徑
        └── pixmaps/
            └── myprogram.png       # 應用程式圖示
```

Avalonia 必要相依套件：`libx11-6, libice6, libsm6, libfontconfig1`

### 重點程式碼範例（如有）
```bash
# 啟動腳本（/usr/bin/myprogram）
#!/bin/bash
exec /usr/lib/myprogram/myprogram_executable "$@"

# 編譯 .deb 套件
dpkg-deb --root-owner-group --build ./staging_folder/ "./myprogram_${versionName}_amd64.deb"
```

**control 檔案範例：**
```
Package: myprogram
Version: 3.1.0
Architecture: amd64
Depends: libx11-6, libice6, libsm6, libfontconfig1, ca-certificates, libc6...
Maintainer: Ken Lee <kenlee@outlook.com>
Description: This is MyProgram
```

---

## 部署到 macOS

**來源：** https://docs.avaloniaui.net/docs/deployment/macOS

macOS 應用程式通常以 `.app` 應用程式套件形式分發。

**.app 結構：**
```
MyProgram.app/
└── Contents/
    ├── _CodeSignature/   # 程式碼簽署資訊
    ├── MacOS/            # dotnet publish 輸出
    │   ├── MyProgram     # 執行檔（必要）
    │   └── MyProgram.dll
    ├── Resources/
    │   └── MyProgramIcon.icns
    └── Info.plist        # 應用程式資訊
```

**Info.plist 必要欄位：**
- `CFBundleExecutable`：對應 dotnet publish 輸出的執行檔名稱
- `CFBundleName`：顯示名稱（超過 15 字元需加 `CFBundleDisplayName`）
- `CFBundleIconFile`：圖示檔案名稱（含副檔名）
- `CFBundleIdentifier`：反向 DNS 格式的唯一識別碼（如 `com.myapp.macos`）
- `NSHighResolutionCapable`：設為 `<true/>`
- `CFBundleVersion`：版本號（如 `1.4.2`）

### 重點程式碼範例（如有）
```xml
<!-- csproj - 確保生成執行檔 -->
<PropertyGroup>
    <UseAppHost>true</UseAppHost>
</PropertyGroup>
```

使用 **dotnet-bundle** NuGet 套件簡化 .app 建置：
```bash
dotnet restore -r osx-x64
dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=osx-x64 -p:UseAppHost=true
```

---

## Native AOT 部署

**來源：** https://docs.avaloniaui.net/docs/deployment/native-aot

Native AOT 將應用程式編譯為原生執行檔，帶來更快啟動時間、更小記憶體佔用、無需 .NET Runtime。

### 重點程式碼範例（如有）
```xml
<!-- csproj 設定 -->
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <BuiltInComInteropSupport>false</BuiltInComInteropSupport>
</PropertyGroup>
```

```bash
# 各平台發佈
dotnet publish -r win-x64 -c Release
dotnet publish -r linux-x64 -c Release
dotnet publish -r osx-x64 -c Release    # Intel
dotnet publish -r osx-arm64 -c Release  # Apple Silicon
```

**Avalonia 特定注意事項：**
- 使用 `x:CompileBindings="True"`（Compiled Bindings）
- 避免執行時期動態 XAML 載入
- 使用靜態資源引用
- 所有資產以嵌入資源形式打包
- 避免反射式服務定位

**已知限制：**動態控制項建立需設定 TrimmerRoots、部分第三方控制項可能不相容

---

## 跨平台應用程式建置指南

**來源：** https://docs.avaloniaui.net/docs/guides/building-cross-platform-applications/

Avalonia 允許最大化程式碼重用，同時在 Windows、Linux、macOS、iOS、Android 和 WebAssembly 提供高品質 UI。

關鍵原則：
1. 使用 .NET（C#/F#/VB.NET）
2. 採用 MVVM 設計模式
3. 善用 Avalonia 的自繪 UI 能力（不依賴原生控制項）
4. 在核心程式碼和平台特定程式碼之間取得平衡

---

## 跨平台應用程式架構

**來源：** https://docs.avaloniaui.net/docs/guides/building-cross-platform-applications/architecture

建立跨平台應用程式的架構原則：
1. **封裝（Encapsulation）**：最小化 API 暴露，隱藏實作細節
2. **責任分離（Separation of Responsibilities）**：每個元件有明確定義的職責
3. **多型（Polymorphism）**：以介面/抽象類別編程，支援多種平台實作

**典型應用程式層次結構：**
1. **資料層（Data Layer）**：非揮發性資料持久化（SQLite/LiteDB/XML）
2. **資料存取層（DAL）**：CRUD 操作封裝
3. **業務層（Business Layer）**：業務邏輯和實體定義（BLL）
4. **服務存取層（Service Access Layer）**：Web 服務、REST/JSON 存取
5. **應用層（Application Layer）**：平台特定程式碼
6. **UI 層（UI Layer）**：Views 和 ViewModels（Avalonia 可跨平台共用）

**常用架構模式：**
- MVVM：UI 與邏輯分離
- Business Façade：簡化複雜操作的入口
- Singleton：確保只有一個實例
- Provider：促進跨平台程式碼重用
- Async：長時間任務不阻塞 UI

---

## Android 開發指南索引

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/android/

Android 相關指南包含：實機部署、模擬器執行、VSCode 偵錯設定、開發環境設定、嵌入原生視圖、Intent Filter 設定。

---

## 在 Android 實機上執行

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/android/build-and-run-your-application-on-a-device

確保實機已啟用開發者模式並連接後，使用 `dotnet run` 或 VS Code tasks 部署：

### 重點程式碼範例（如有）
```json
{
    "label": "build-android",
    "command": "dotnet",
    "type": "process",
    "args": ["build", "--no-restore", "${workspaceFolder}/<ProjectName>.csproj",
             "-p:TargetFramework=net6.0-android", "-p:Configuration=Debug"]
}
```

---

## 在 Android 模擬器上執行

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/android/build-and-run-your-application-on-a-simulator

在 `HelloWorld.Android` 目錄中執行：
```
dotnet build
dotnet run
```

---

## 在 Linux 上設定 VSCode 偵錯 Android

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/android/configure-vscode-debug-linux

使用 Mono 偵錯器透過指定 port 連接：

### 重點程式碼範例（如有）
```json
{
    "name": "Debug - Android",
    "type": "mono",
    "request": "attach",
    "address": "localhost",
    "port": 10000
}
```
Build task 需加入 `-p:AndroidAttachDebugger=true` 和 `-p:AndroidSdbHostPort=10000` 參數。

---

## 嵌入 Android 原生視圖

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/android/embed-native-views

透過 `AndroidViewControlHandle` 包裝 Android 原生 View 並回傳給 Avalonia：

### 重點程式碼範例（如有）
```csharp
public IPlatformHandle CreateControl(bool isSecond, IPlatformHandle parent, Func<IPlatformHandle> createDefault)
{
    var parentContext = (parent as AndroidViewControlHandle)?.View.Context
        ?? global::Android.App.Application.Context;
    var button = new global::Android.Widget.Button(parentContext) { Text = "Hello world" };
    return new AndroidViewControlHandle(button);
}
```

---

## Android Intent Filter（檔案與協定處理）

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/android/intent-filter

在 `MainActivity` 上套用 `[IntentFilter]` attribute 讓應用程式能處理特定檔案類型或 URI 協定：

### 重點程式碼範例（如有）
```csharp
[IntentFilter(["android.intent.action.VIEW"],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["file", "content"],
    DataMimeType = "text/plain",
    DataPathPattern = ".*\\.txt")]
public class MainActivity : AvaloniaMainActivity<App>
{
    public MainActivity()
    {
        ((IAvaloniaActivity)this).Activated += HandleIntent;
    }
}
```

---

## 設定 Android 開發環境

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/android/setting-up-your-developer-environment-for-android

需要 .NET SDK 6.0.200+ 並安裝 Android Workload：

### 重點程式碼範例（如有）
```bash
dotnet workload install android
```

安裝 Android SDK 後設定環境變數：
```bash
export ANDROID_HOME=/path/to/sdk
export PATH=$PATH:$ANDROID_HOME/tools:$ANDROID_HOME/platform-tools
```

也可使用 MAUI Check 工具自動安裝所有依賴：
```bash
dotnet tool install -g Redth.Net.Maui.Check
maui-check
```

---

## 使用 WebAssembly 在瀏覽器中執行

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/how-to-use-web-assembly

### 重點程式碼範例（如有）
```bash
# 1. 安裝 wasm-tools workload
dotnet workload install wasm-tools

# 2. 安裝/更新 Avalonia 範本
dotnet new install avalonia.templates

# 3. 建立跨平台專案
mkdir BrowserTest && cd BrowserTest
dotnet new avalonia.xplat

# 4. 執行 Browser 版本
cd BrowserTest.Browser
dotnet run

# 5. 發佈
dotnet publish
# 輸出在 bin/Release/net9.0-browser/publish/wwwroot
```

WebAssembly 注意事項：
- .NET Multithreading 需 .NET 8+
- 受瀏覽器沙箱限制
- 若遇 `System.DllNotFoundException: libSkiaSharp`，需加入 `<WasmBuildNative>true</WasmBuildNative>`
- 支援 `[JSImport]/[JSExport]` 標準 JavaScript 互操作

---

## iOS 開發概述

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/ios/

由於 Apple 政策限制，iOS 模擬器及真機測試需要在 macOS 上安裝 Xcode 才能執行。建議在發佈前務必在真實裝置上測試。

---

## 在 iOS 模擬器上建置與執行

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/ios/build-and-run-your-application-on-a-simulator

假設專案名稱為 `HelloWorld`，進入 `HelloWorld.iOS` 目錄後執行：

### 重點程式碼範例（如有）
```bash
dotnet build
dotnet run
```

在 Apple Silicon Mac 上可能需要安裝 Rosetta 2：

```bash
/usr/sbin/softwareupdate --install-rosetta
```

---

## 在 iPhone/iPad 上執行

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/ios/build-and-run-your-application-on-your-iphone-or-ipad

需要先透過 Xcode 為裝置佈建（Provision）。在 `info.plist` 中加入 `CodesignKey` 標籤，值可以從 Keychain Access 搜尋 "development" 找到。

### 重點程式碼範例（如有）
```xml
<RuntimeIdentifier>ios-arm64</RuntimeIdentifier>
<CodesignKey>Apple Development: your@email.com (XXXXXXXX)</CodesignKey>
```

---

## 設置 iOS 開發環境

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/ios/setting-up-your-developer-environment-for-ios

需要最新版 macOS 與 Xcode。安裝正確的 .NET SDK（最低 6.0.200），然後安裝 iOS workload：

### 重點程式碼範例（如有）
```bash
dotnet workload install ios
```

---

## macOS 開發

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/macos-development

macOS 原生程式碼位於 `native/Avalonia.Native/src/OSX`，使用 Xcode 開啟 `.xcodeproj` 專案。可以透過 `AvaloniaNativePlatformOptions.AvaloniaNativeLibraryPath` 指定自訂 dylib 路徑。

### 重點程式碼範例（如有）
```csharp
.With(new AvaloniaNativePlatformOptions{
     AvaloniaNativeLibraryPath = "[Path to your dylib]",
})
```

---

## 平台特定程式碼（.NET 條件編譯）

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/platform-specific-code/dotnet

使用 `OperatingSystem` 類別方法（如 `IsWindows()`、`IsLinux()`）進行執行期平台判斷，或使用 C# 預處理器指令進行編譯期判斷。注意：Linux 沒有對應的 Target Framework，沒有 `LINUX` 常量。

| Target Framework | 常量 |
|---|---|
| net8.0-windows | WINDOWS |
| net8.0-macos | MACOS |
| net8.0-browser | BROWSER |
| net8.0-ios | IOS |
| net8.0-android | ANDROID |

### 重點程式碼範例（如有）
```csharp
public static DeviceOrientation GetOrientation()
{
#if ANDROID
    // Android 特定代碼
#elif IOS
    // iOS 特定代碼
#else
    return DeviceOrientation.Undefined;
#endif
}
```

---

## XAML OnPlatform 標記擴展

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/platform-specific-code/xaml

`OnPlatform` 標記擴展允許根據作業系統指定不同屬性值。`OnFormFactor` 則根據設備類型（Desktop/Mobile）設定不同值。

### 重點程式碼範例（如有）
```xml
<!-- 基本用法 -->
<TextBlock Text="{OnPlatform Default='Unknown', Windows='Im Windows', macOS='Im macOS', Linux='Im Linux'}"/>

<!-- 不同類型 -->
<Border Height="{OnPlatform 10, Windows=50.5}"/>

<!-- XML 語法 - 合併多平台 -->
<Application.Styles>
    <FluentTheme />
    <OnPlatform>
        <On Options="Android, iOS">
            <StyleInclude Source="/Styles/Mobile.axaml" />
        </On>
        <On Options="Default">
            <StyleInclude Source="/Styles/Default.axaml" />
        </On>
    </OnPlatform>
</Application.Styles>
```

---

## 在 Raspberry Pi Lite 上透過 DRM 執行

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/rpi/running-on-raspbian-lite-via-drm

透過 DRM（Direct Rendering Manager）在 Raspberry Pi 上執行 Avalonia。需安裝必要函式庫並加入 `Avalonia.LinuxFrameBuffer` 套件。在 FrameBuffer 模式下沒有視窗，需要使用 UserControl 作為頂層容器。

### 重點程式碼範例（如有）
```bash
sudo apt-get install libgbm1 libgl1-mesa-dri libegl1-mesa libinput10
dotnet add package Avalonia.LinuxFramebuffer
```

---

## 在 Raspberry Pi 上執行

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/rpi/running-your-app-on-a-raspberry-pi

需要安裝 .NET 執行環境，並加入 `SkiaSharp.NativeAssets.Linux` NuGet 套件（包含 `libSkiaSharp.so`）。

### 重點程式碼範例（如有）
```bash
dotnet publish -r linux-arm -f netcoreapp2.0
```

---

## 在 Windows Forms 中托管 Avalonia 控件

**來源：** https://docs.avaloniaui.net/docs/guides/platforms/windows/host-avalonia-controls-in-winforms

允許在 WinForms 應用中嵌入 Avalonia 控件，實現漸進式遷移。需引用 `Avalonia.Desktop` 和 `Avalonia.Win32.Interoperability` 套件，並使用 `WinFormsAvaloniaControlHost` 控件。

### 重點程式碼範例（如有）
```csharp
// 在 Program.cs 的 Application.Run() 之前加入
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .SetupWithoutStarting();

// 在 Form 建構子中
winFormsAvaloniaControlHost1.Content = new MainView { DataContext = new MainViewModel() };
```

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter18 — Desktop Platform Specifics

**平台後端選項設定**
```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .With(new Win32PlatformOptions
    {
        RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software },
        CompositionMode = new[] { Win32CompositionMode.WinUIComposition },
        OverlayPopups = true
    })
    .With(new MacOSPlatformOptions
    {
        DisableDefaultApplicationMenuItems = false, ShowInDock = true
    })
    .With(new X11PlatformOptions
    {
        RenderingMode = new[] { X11RenderingMode.Glx, X11RenderingMode.Software },
        UseDBusMenu = true, WmClass = "MyAvaloniaApp"
    });
```

**視窗基礎屬性（XAML）**
```xml
<Window xmlns="https://github.com/avaloniaui"
        x:Class="MyApp.MainWindow"
        Width="1024" Height="720"
        CanResize="True"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="True"
        Title="My App">
</Window>
```

**視窗位置持久化**
```csharp
protected override void OnOpened(EventArgs e)
{
    base.OnOpened(e);
    if (LocalSettings.TryReadWindowPlacement(out var placement))
    {
        Position = placement.Position;
        Width = placement.Width; Height = placement.Height;
        WindowState = placement.State;
    }
}

protected override void OnClosing(WindowClosingEventArgs e)
{
    base.OnClosing(e);
    LocalSettings.WriteWindowPlacement(new WindowPlacement
    {
        Position = Position, Width = Width, Height = Height, State = WindowState
    });
}
```

**自訂標題列（SystemDecorations="None"）**
```xml
<Window SystemDecorations="None"
        ExtendClientAreaToDecorationsHint="True"
        ExtendClientAreaChromeHints="PreferSystemChrome"
        ExtendClientAreaTitleBarHeightHint="32">
  <Grid>
    <Border Background="#1F2937" Height="32" VerticalAlignment="Top"
            PointerPressed="TitleBar_PointerPressed">
      <StackPanel Orientation="Horizontal" Margin="12,0" Spacing="12">
        <TextBlock Text="My App" Foreground="White"/>
        <Border Width="32" Height="24" PointerPressed="CloseButton_PointerPressed">
          <Path Stroke="White" StrokeThickness="2" Data="M2,2 L10,10 M10,2 L2,10"/>
        </Border>
      </StackPanel>
    </Border>
  </Grid>
</Window>
```

```csharp
private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        BeginMoveDrag(e);
}
private void CloseButton_PointerPressed(object? sender, PointerPressedEventArgs e) => Close();
```

**透明度與視覺效果**
```csharp
TransparencyLevelHint = new[]
{
    WindowTransparencyLevel.Mica,
    WindowTransparencyLevel.AcrylicBlur,
    WindowTransparencyLevel.Blur,
    WindowTransparencyLevel.Transparent
};

// 監聽實際達成的透明度等級
this.GetObservable(TopLevel.ActualTransparencyLevelProperty)
    .Subscribe(level => Debug.WriteLine($"Transparency: {level}"));
```

**螢幕與 DPI 縮放**
```csharp
protected override void OnOpened(EventArgs e)
{
    base.OnOpened(e);
    var currentScreen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
    if (currentScreen is null) return;

    var frameSize = PixelSize.FromSize(ClientSize, DesktopScaling);
    var target = currentScreen.WorkingArea.CenterRect(frameSize);
    Position = target.Position;
}

// 監聽縮放變化（切換到 HiDPI 螢幕）
ScalingChanged += (_, _) =>
{
    // 更新快取的點陣圖資源
};
```

**Skia GPU 選項**
```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .With(new SkiaOptions
    {
        MaxGpuResourceSizeBytes = 128 * 1024 * 1024,
        UseOpacitySaveLayer = true
    })
    .UseSkia()
    .LogToTrace();
```

**打包部署**
- **Windows**：`dotnet publish -r win-x64 --self-contained`；MSIX：`/p:WindowsPackageType=msix`
- **macOS**：`.app` bundle，程式碼簽署 + 公證，確認 `Info.plist` 有 `NSHighResolutionCapable`
- **Linux**：`.deb/.rpm`、AppImage 或 Flatpak；確認 `xdg-desktop-portal` 執行時依賴

**疑難排解速查**

| 問題 | 修正 |
|------|------|
| 高 DPI 模糊 | 使用向量資源；調整 RenderScaling |
| 透明效果被忽略 | 檢查 `ActualTransparencyLevel`；驗證 OS 支援 |
| 自訂 Chrome 拖曳失敗 | 確認 `BeginMoveDrag` 只在左鍵按下時呼叫 |
| 啟動時顯示在錯誤螢幕 | 設定 `WindowStartupLocation` 或用 `Screens` 計算位置 |

---

### Chapter19 — Mobile Platform Specifics (Android & iOS)

**工作負載安裝**
```bash
sudo dotnet workload install android
sudo dotnet workload install ios  # 需 macOS + Xcode
sudo dotnet workload install wasm-tools
dotnet workload list  # 確認安裝
```

**多平台專案結構**
```bash
dotnet new avalonia.app --multiplatform
# 產生：
# MyApp（共用程式碼）
# MyApp.Android
# MyApp.iOS
# MyApp.Browser（選用）
```

**Trimming 設定（Directory.Build.props）**
```xml
<PropertyGroup>
  <TrimMode>partial</TrimMode>
  <PublishTrimmed>true</PublishTrimmed>
</PropertyGroup>
```

**Single-View 生命週期設定**
```csharp
public override void OnFrameworkInitializationCompleted()
{
    var services = ConfigureServices();

    if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        singleView.MainView = services.GetRequiredService<ShellView>();
    else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = services.GetRequiredService<MainWindow>();

    base.OnFrameworkInitializationCompleted();
}
```

**行動導覽（帶 Back 按鈕的 ShellView）**
```xml
<UserControl xmlns="https://github.com/avaloniaui" x:Class="MyApp.Views.ShellView">
  <Grid RowDefinitions="Auto,*">
    <StackPanel Orientation="Horizontal" Spacing="8" Margin="16">
      <Button Content="Back"
              Command="{Binding BackCommand}"
              IsVisible="{Binding CanGoBack}"/>
      <TextBlock Text="{Binding Title}" FontSize="20" VerticalAlignment="Center"/>
    </StackPanel>
    <TransitioningContentControl Grid.Row="1" Content="{Binding Current}"/>
  </Grid>
</UserControl>
```

**安全區域和輸入插入（Safe Areas / Insets）**
```csharp
public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
        this.AttachedToVisualTree += (_, __) =>
        {
            var top = TopLevel.GetTopLevel(this);
            var insets = top?.InsetsManager;
            if (insets is null) return;

            void ApplyInsets()
            {
                RootPanel.Padding = new Thickness(
                    insets.SafeAreaPadding.Left, insets.SafeAreaPadding.Top,
                    insets.SafeAreaPadding.Right, insets.SafeAreaPadding.Bottom);
            }
            ApplyInsets();
            insets.Changed += (_, __) => ApplyInsets();
        };
    }
}
```

**鍵盤遮擋處理（InputPane）**
```csharp
var pane = top?.InputPane;
if (pane is not null)
{
    pane.Showing += (_, args) => RootPanel.Margin = new Thickness(0, 0, 0, args.OccludedRect.Height);
    pane.Hiding += (_, __) => RootPanel.Margin = new Thickness(0);
}
```

**Android 頭設定（MainActivity.cs）**
```csharp
// 攔截 Android 返回鍵
public override void OnBackPressed()
{
    if (!AvaloniaApp.Current?.TryGoBack() ?? true)
        base.OnBackPressed();
}
```

**iOS 頭設定（AppDelegate.cs）**
```csharp
// 繼承自 AvaloniaAppDelegate
// 覆寫 CustomizeAppBuilder 以注入服務
```

**Android 打包**
```bash
cd MyApp.Android
# Debug → 裝置
msbuild /t:Run /p:Configuration=Debug

# Release AAB（App Store）
msbuild /t:Publish /p:Configuration=Release /p:AndroidPackageFormat=aab
```

**iOS 打包**
```bash
# 需 macOS + Xcode
dotnet build -t:Run -f net8.0-ios
```

**觸控設計原則**
- 控制項最小尺寸 44×44 DIP
- 優先用 `Tapped`/`DoubleTapped` 而非滑鼠事件
- 避免只有 hover 的互動
- 用 `TopLevel.Screen` 偵測方向/螢幕尺寸類別

---

> ✅ **所有 Chapter01 到 Chapter19 已成功處理完畢。**
> 
> 各章節按以下映射整理：
> - `getting-started.md` ← Ch01, Ch02, Ch03, Ch04  
> - `layout.md` ← Ch03, Ch05  
> - `controls.md` ← Ch03, Ch06  
> - `styling.md` ← Ch07  
> - `data-binding.md` ← Ch08, Ch09  
> - `concepts.md` ← Ch01, Ch04, Ch09  
> - `reactiveui.md` ← Ch11  
> - `navigation.md` ← Ch12, Ch13  
> - `misc.md` ← Ch10, Ch13, Ch15, Ch16, Ch17  
> - `platform-specifics.md` ← Ch18, Ch19

### Chapter20 — Browser (WebAssembly) Target

#### 關鍵概念

**專案結構與設定**
安裝 `wasm-tools` workload，多目標方案包含 Shared project + Browser head (`MyApp.Browser`)，目標框架為 `net8.0-browserwasm`。

**Single View Lifetime**
Browser 使用 `ISingleViewApplicationLifetime`（與 Mobile 相同），在 `OnFrameworkInitializationCompleted` 設定 `MainView`。

**渲染選項**
透過 `BrowserPlatformOptions` 選擇渲染模式：WebGL2（最佳效能）→ WebGL1（舊瀏覽器）→ Software2D（最終後備）。

**平台限制**
- 無 OS 視窗、系統列、原生選單
- 檔案系統限使用者挑選
- 多執行緒需伺服器設定 COOP/COEP headers
- Clipboard 需使用者手勢

#### 代碼範例

```csharp
// Program.cs — 啟動 Browser App
using Avalonia;
using Avalonia.Browser;

internal sealed class Program
{
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    public static Task Main(string[] args)
        => BuildAvaloniaApp()
            .StartBrowserAppAsync("out");
}
```

```csharp
// 渲染選項配置
await BuildAvaloniaApp().StartBrowserAppAsync(
    "out",
    new BrowserPlatformOptions
    {
        RenderingMode = new[]
        {
            BrowserRenderingMode.WebGL2,
            BrowserRenderingMode.WebGL1,
            BrowserRenderingMode.Software2D
        },
        RegisterAvaloniaServiceWorker = true,
        AvaloniaServiceWorkerScope = "/",
        PreferManagedThreadDispatcher = true
    });
```

```csharp
// JS Interop
using Avalonia.Browser.Interop;
await JSRuntime.InvokeVoidAsync("console.log", "Hello from Avalonia");
```

```bash
# 發布 Release Bundle
dotnet publish -c Release
# 輸出於 bin/Release/net8.0/browser-wasm/AppBundle
```

---

### Chapter26 — Publishing & Distribution

#### 關鍵概念

**Build vs Publish**
- `dotnet build`：本機開發編譯
- `dotnet publish -c Release`：建立可執行的發布包

**Publish 選項矩陣**

| 選項 | 優點 | 缺點 |
|---|---|---|
| Framework-dependent | 小 | 需安裝 runtime |
| Self-contained | 隨處可執行 | 較大 |
| Single-file | 簡單分發 | 解壓原生庫 |
| ReadyToRun | 快速冷啟動 | 更大 |
| Trimmed | 最小 | 反射風險 |

**平台打包**
- Windows: MSIX / MSI / Authenticode 簽章
- macOS: .app bundle + codesign + notarize + DMG
- Linux: AppImage / Flatpak / Snap
- Android: AAB + keystore 簽章
- iOS: .ipa + 憑證 + provisioning profile

**AvaloniaResource 項目**
確保 `.axaml` 和資源包含在 `AvaloniaResource` 中，否則 publish 後找不到。

#### 代碼範例

```bash
# Self-contained 各平台
dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish -c Release -r osx-arm64 --self-contained true
dotnet publish -c Release -r linux-x64 --self-contained true

# Single-file
dotnet publish -c Release -r linux-x64 /p:SelfContained=true /p:PublishSingleFile=true

# ReadyToRun
dotnet publish -c Release -r win-x64 /p:SelfContained=true /p:PublishReadyToRun=true
```

```xml
<!-- 確保資源包含 -->
<ItemGroup>
  <AvaloniaResource Include="Assets/**" />
  <AvaloniaResource Include="Themes/**/*.axaml" />
</ItemGroup>
```

```yaml
# GitHub Actions 矩陣發布
jobs:
  publish:
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: macos-latest
            rid: osx-arm64
          - os: ubuntu-latest
            rid: linux-x64
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Restore workloads
        run: dotnet workload restore
      - name: Publish
        run: |
          dotnet publish src/MyApp/MyApp.csproj \
            -c Release \
            -r ${{ matrix.rid }} \
            --self-contained true \
            /p:PublishSingleFile=true
```

---

### Chapter32 — Native Integration & Platform Services

#### 關鍵概念

**平台抽象**
- `IWindowingPlatform`：建立視窗、tray icons、embeddable top levels
- `INativeControlHostImpl`：在 Avalonia 內宿主原生控件 (HWND/NSView/UIView)
- `EmbeddableControlRoot`：讓 Avalonia 嵌入原生宿主 (WinForms/WPF/X11)

**NativeControlHost**
覆寫 `CreateNativeControlCore` 與 `DestroyNativeControlCore` 包裝原生 Widget。

**Remote Protocol**
`RemoteServer` + `BsonTcpTransport` 透過 TCP 串流 Avalonia UI 到遠端客戶端（XAML Previewer 就是這樣運作的）。

**Offscreen Rendering**
`OffscreenTopLevel` + `RenderTargetBitmap` 可在無視窗情況下渲染（伺服器端生成圖片、縮圖）。

#### 代碼範例

```csharp
// 宿主原生 Win32 WebView
public class Win32WebViewHost : NativeControlHost
{
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var hwnd = Win32Interop.CreateWebView(parent.Handle);
        return new PlatformHandle(hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Win32Interop.DestroyWindow(control.Handle);
    }
}
```

```csharp
// 系統 Tray Icon
var trayIcon = PlatformManager.CreateTrayIcon();
trayIcon.Icon = new WindowIcon("avares://Assets/tray.ico");
trayIcon.ToolTipText = "My App";
trayIcon.Menu = new NativeMenu
{
    Items =
    {
        new NativeMenuItem("Show", (sender, args) => mainWindow.Show()),
        new NativeMenuItem("Exit", (sender, args) => app.Shutdown())
    }
};
trayIcon.IsVisible = true;
```

---