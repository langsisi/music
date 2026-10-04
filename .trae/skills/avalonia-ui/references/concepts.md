# Avalonia 核心概念知識庫

此檔案包含 Avalonia 核心概念相關文件摘要。

---

## 手勢識別 (Gestures)

**來源：** https://docs.avaloniaui.net/docs/concepts/input/gestures

控制項可透過 `GestureRecognizer` 偵測手勢，附加在控制項的 `GestureRecognizers` 屬性上。

### 重點程式碼範例（如有）
```xml
<Image Source="/image.jpg">
    <Image.GestureRecognizers>
        <ScrollGestureRecognizer CanHorizontallyScroll="True"
                                  CanVerticallyScroll="True"/>
    </Image.GestureRecognizers>
</Image>
```

---

## Markup Extensions

**來源：** https://docs.avaloniaui.net/docs/concepts/markupextensions/

Markup Extension 是在執行時為 XAML 屬性提供值的類別，使用 `{ns:Extension ...}` 語法。

Avalonia 內建 Markup Extensions：

| Extension | 用途 |
|-----------|------|
| `StaticResource` | 靜態資源（不更新） |
| `DynamicResource` | 動態資源（會更新） |
| `Binding` | 預設綁定 |
| `CompiledBinding` | 編譯式綁定 |
| `ReflectionBinding` | 反射式綁定 |
| `TemplateBinding` | 控制項模板內綁定 |
| `OnPlatform` | 依平台條件 |
| `OnFormFactor` | 依裝置類型條件 |

XAML 編譯器內建語法：`x:True`、`x:False`、`x:Null`、`x:Static`、`x:Type`

### 重點程式碼範例（如有）
```csharp
public class LocExtension
{
    public string Key { get; set; } = "";
    public string ProvideValue(IServiceProvider serviceProvider)
    {
        return LocalizationService.GetString(Key) ?? Key;
    }
}
```

```xml
<TextBlock Text="{local:Loc Key=WelcomeMessage}" />
<Button CommandParameter="{x:True}" />
```

---


## 鍵盤與滑鼠輸入綁定

**來源：** https://docs.avaloniaui.net/docs/concepts/input/binding-key-and-mouse

使用 `KeyBindings` 設定鍵盤快捷鍵；滑鼠按鍵（MouseBindings）不支援，需在 code-behind 處理（如 `DoubleTapped` 事件）。

### 重點程式碼範例（如有）
```xml
<ListBox DoubleTapped="ListBox_DoubleTapped"
         ItemsSource="{Binding OperatingSystems}">
    <ListBox.KeyBindings>
        <KeyBinding Command="{Binding PrintItem}" Gesture="Enter" />
    </ListBox.KeyBindings>
</ListBox>
<TextBlock>
    <TextBlock.ContextMenu>
        <ContextMenu>
            <MenuItem Command="{Binding Clear}" Header="Clear" />
        </ContextMenu>
    </TextBlock.ContextMenu>
</TextBlock>
```

---

## 焦點管理 (Focus)

**來源：** https://docs.avaloniaui.net/docs/concepts/input/focus

焦點（Focus）指接收鍵盤輸入的 `InputElement`。

- `IsFocused`：唯讀，追蹤焦點狀態
- `Focusable`：啟用/停用焦點能力
- 使用 `.Focus()` 方法明確指定焦點

焦點事件：`GotFocus`、`LostFocus`

焦點偽類：`:focus`、`:focus-within`、`:focus-visible`

Tab 焦點導覽：`IsTabStop`、`TabIndex` 控制順序；`KeyboardNavigation.TabNavigation` 設定模式（Continue/Cycle/Contained/Once/None/Local）

方向焦點導覽（v11.1）：`XYFocus` 支援 2D 方向導覽（遊戲手柄、遙控器等）

---


**來源：** https://docs.avaloniaui.net/docs/concepts/attached-property

附加屬性是套用在子控制項上、引用其容器控制項的屬性。XAML 語法：`ContainerClassName.AttachedPropertyName="value"`

使用場景：
1. **附加控制項**：附加工具提示、上下文選單等，不計入內容區域（如 ToolTip、ContextMenu、Flyouts）
2. **版面配置控制項**：容器需要了解子控制項排列方式（如 Grid 的行列位置、DockPanel 的停靠方向）

---

## 控制樹 (Control Trees)

**來源：** https://docs.avaloniaui.net/docs/concepts/control-trees

Avalonia 從 XAML 建立兩種控制樹：

- **邏輯樹（Logical Tree）**：應用程式控制項的階層關係，如 XAML 中定義的結構
- **視覺樹（Visual Tree）**：包含所有視覺元素的完整樹，包括模板展開後的控制項

在執行時可透過開發者工具查看控制樹。資料綁定的 DataContext 沿邏輯樹往上搜尋，樣式也是沿邏輯樹向上應用。

---

## Headless 測試平台

**來源：** https://docs.avaloniaui.net/docs/concepts/headless/

Headless 平台讓 Avalonia 應用程式在沒有 GUI 的環境下執行（如 CI/CD 環境、伺服器），用於 UI 測試和自動化。

提供整合套件：
- **Headless + XUnit**
- **Headless + NUnit**
- **手動設定**

模擬使用者輸入的方法：`Window.KeyPress()`、`Window.KeyRelease()`、`Window.KeyTextInput()`、`Window.MouseDown()`、`Window.MouseMove()`、`Window.MouseUp()`、`Window.MouseWheel()`、`Window.DragDrop()`

### 重點程式碼範例（如有）
```csharp
// 基本按鈕點擊測試
var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch };
var window = new Window { Width = 100, Height = 100, Content = button };
var buttonClicked = false;
button.Click += (_, _) => buttonClicked = true;
window.Show();
window.MouseDown(new Point(50, 50), MouseButton.Left);
window.MouseUp(new Point(50, 50), MouseButton.Left);
Assert.True(buttonClicked);

// 擷取渲染畫面（需啟用 Skia）
public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

var frame = window.CaptureRenderedFrame();
frame.Save("file.png");
```

---

## 路由事件 (Routed Events)

**來源：** https://docs.avaloniaui.net/docs/concepts/input/routed-events

路由事件是在整個控制樹上觸發而非只在產生事件的控制項上觸發的事件。三種路由策略：

- **Bubbling（冒泡）**：從事件來源往上傳遞到根節點（最常用）
- **Direct**：只在來源元素觸發
- **Tunneling（隧道）**：從根節點往下到事件來源

### 重點程式碼範例（如有）
```csharp
// 定義自訂路由事件
public class SampleControl : Control
{
    public static readonly RoutedEvent<RoutedEventArgs> TapEvent =
        RoutedEvent.Register<SampleControl, RoutedEventArgs>(
            nameof(Tap), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> Tap
    {
        add => AddHandler(TapEvent, value);
        remove => RemoveHandler(TapEvent, value);
    }
}

// 在一個處理器中處理多個來源
private void CommonClickHandler(object sender, RoutedEventArgs e)
{
    var source = e.Source as Control;
    switch (source.Name)
    {
        case "YesButton": break;
        case "NoButton": break;
    }
    e.Handled = true;
}
```

---

## 熱鍵 (Hotkeys)

**來源：** https://docs.avaloniaui.net/docs/concepts/input/hotkeys

實作 `ICommandSource` 的控制項有 `HotKey` 屬性，按下熱鍵會執行控制項綁定的命令。熱鍵由一個 Key 和零或多個 KeyModifiers 組成。

### 重點程式碼範例（如有）
```csharp
// 程式碼中設定熱鍵
HotKeyManager.SetHotKey(saveMenuItem, new KeyGesture(Key.S, KeyModifiers.Control));
```

```xml
<!-- XAML 中設定熱鍵 -->
<MenuItem Header="_Save" HotKey="Ctrl+S" Command="{Binding SaveCommand}" />
```

---
(Application Lifetimes)

**來源：** https://docs.avaloniaui.net/docs/concepts/application-lifetimes

Avalonia 跨平台框架提供多種生命週期模型：

| 介面 | 提供者 | 說明 |
|------|--------|------|
| `IControlledApplicationLifetime` | `StartWithClassicDesktopLifetime`、`StartLinuxFramebuffer` | 訂閱 Startup/Exit 事件，可呼叫 `Shutdown()` |
| `IClassicDesktopStyleApplicationLifetime` | `StartWithClassicDesktopLifetime` | 類 WinForms/WPF 方式，管理視窗清單，設定主視窗，三種關閉模式 |
| `ISingleViewApplicationLifetime` | 行動平台、WebAssembly | 只有單一視圖的平台，設定 `MainView` |

關閉模式（ShutdownMode）：
- `OnLastWindowClose`：最後一個視窗關閉時
- `OnMainWindowClose`：主視窗關閉時
- `OnExplicitShutdown`：手動呼叫 `Shutdown()` 時

### 重點程式碼範例（如有）
```csharp
// 初始化桌面應用程式
class Program
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect();

    public static int Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}

// Application 類別中建立主視窗
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();
    else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        singleView.MainView = new MainView();
    base.OnFrameworkInitializationCompleted();
}
```

---

## 應用程式服務概覽

**來源：** https://docs.avaloniaui.net/docs/concepts/services/

Avalonia 內建多項應用程式服務，包括：IActivatableLifetime、IClipboard、IFocusManager、InputPane、InsetsManager、ILauncher、IPlatformSettings、IStorageProvider。

---

## IActivatableLifetime - 應用程式生命週期啟動服務

**來源：** https://docs.avaloniaui.net/docs/concepts/services/activatable-lifetime

`IActivatableLifetime` 服務定義應用程式啟動/停用生命週期的事件和方法，通過 `Application.Current.TryGetFeature<IActivatableLifetime>()` 取得。

主要事件：
- `Activated`：應用程式啟動時觸發（Background、File、OpenUri、Reopen 等類型）
- `Deactivated`：應用程式停用時觸發

主要方法：
- `TryLeaveBackground()`：嘗試離開背景狀態（macOS: `[NSApp unhide]`）
- `TryEnterBackground()`：嘗試進入背景狀態（macOS: `[NSApp hide]`）

### 重點程式碼範例（如有）
```csharp
// 處理背景/前台切換
if (Application.Current.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
{
    activatableLifetime.Activated += (sender, args) =>
    {
        if (args.Kind == ActivationKind.Background)
            Console.WriteLine("App exited background");
    };
}

// 處理 URI 啟動（Deep Linking）
activatableLifetime.Activated += (s, a) =>
{
    if (a is ProtocolActivatedEventArgs protocolArgs && protocolArgs.Kind == ActivationKind.OpenUri)
        Console.WriteLine($"App activated via Uri: {protocolArgs.Uri}");
};
```

平台支援：macOS、Android、iOS 支援較多功能；Windows 和 Linux 支援較少。

---

## IClipboard - 剪貼簿服務

**來源：** https://docs.avaloniaui.net/docs/concepts/services/clipboard

`IClipboard` 介面允許存取系統剪貼簿，支援文字、圖片及自訂資料格式。

取得方式：`var clipboard = window.Clipboard;`

資料格式（DataFormat）類型：
- **Universal**：`DataFormat.Text`（string）、`DataFormat.File`（IStorageItem）、`DataFormat.Bitmap`（Bitmap）
- **Platform**：平台特定格式（Windows 用 `HTML format`，Linux 用 `text/html`，macOS 用 `public.html`）
- **Application**：應用程式自訂格式

### 重點程式碼範例（如有）
```csharp
// 讀取文字
var text = await clipboard.TryGetTextAsync();

// 寫入文字
var data = new DataTransfer();
data.Add(DataTransferItem.CreateText("Copied from Avalonia!"));
await clipboard.SetDataAsync(data);

// 讀取整個剪貼簿（建議一次性讀取）
using var clipboardData = await clipboard.TryGetDataAsync();
```

---

## FocusManager - 焦點管理服務

**來源：** https://docs.avaloniaui.net/docs/concepts/services/focus-manager

`FocusManager` 負責管理鍵盤焦點，追蹤當前焦點元素和焦點範圍。

取得方式：`var focusManager = window.FocusManager;`

主要方法：
- `GetFocusedElement()`：取得當前焦點元素
- `ClearFocus()`：清除焦點

聆聽全域焦點變化：使用 `InputElement.GotFocusEvent.Raised.Subscribe(handler)`

### 重點程式碼範例（如有）
```csharp
// 讓控制項獲得焦點
var hasFocused = button.Focus();
```

---

## InputPane - 輸入面板服務（v11.1）

**來源：** https://docs.avaloniaui.net/docs/concepts/services/input-pane

`InputPane` 允許監聽平台輸入面板（軟體鍵盤）的狀態和邊界，適用於行動裝置應用。

取得方式：`var inputPane = TopLevel.GetTopLevel(control).InputPane;`

注意：Avalonia 目前不自動調整根視圖和捲動位置，開發者需自行處理。

主要屬性：`State`（Closed/Opened）、`OccludedRect`（輸入面板邊界，Client 座標）

主要事件：`StateChanged`，事件參數包含 `NewState`、`StartRect`、`EndRect`、`AnimationDuration`、`Easing`。

平台支援：Windows、Android、iOS、部分 Browser（僅 Chromium 行動版）。

---

## InsetsManager - 邊距管理服務

**來源：** https://docs.avaloniaui.net/docs/concepts/services/insets-manager

`InsetsManager` 允許與平台系統列互動，處理行動視窗的安全區域。

取得方式：`var insetsManager = TopLevel.GetTopLevel(control).InsetsManager;`

從 Avalonia 11.1 起，預設會自動根據 inset 值調整根視圖，可透過 `TopLevel.AutoSafeAreaPadding="False"` 停用。

主要屬性：
- `IsSystemBarVisible`：是否顯示系統列
- `DisplayEdgeToEdge`：是否邊緣到邊緣顯示
- `SafeAreaPadding`：安全區域內距
- `SystemBarColor`：系統列顏色

平台支援：Android、iOS、部分 Browser（Chromium 行動版）。

---

## ILauncher - 啟動器服務（v11.1）

**來源：** https://docs.avaloniaui.net/docs/concepts/services/launcher

`ILauncher` 允許以關聯的預設應用程式開啟檔案或 URI 連結。

取得方式：`var launcher = TopLevel.GetTopLevel(control).Launcher;`

### 重點程式碼範例（如有）
```csharp
// 開啟 URI
await launcher.LaunchUriAsync(new Uri("https://example.com"));

// 開啟檔案（沙盒 API）
await launcher.LaunchFileAsync(storageItem);

// 開啟本地檔案（桌面平台）
await launcher.LaunchFileInfoAsync(new FileInfo("/path/to/file"));
```

全平台支援 `LaunchUriAsync`；`LaunchFileAsync` 支援除 Browser 外的所有平台。

---

## IPlatformSettings - 平台設定服務

**來源：** https://docs.avaloniaui.net/docs/concepts/services/platform-settings

`IPlatformSettings` 提供存取平台特定設定的介面，包括手勢設定、色彩主題等。

取得方式：`var platformSettings = window.PlatformSettings;`

主要方法：
- `GetTapSize(PointerType)`：點擊手勢的最大距離
- `GetDoubleTapSize(PointerType)`：雙擊手勢的最大距離
- `GetDoubleTapTime(PointerType)`：雙擊最大時間間隔
- `GetColorValues()`：取得系統色彩值（深色模式、強調色）

主要屬性：`HoldWaitDuration`、`HotkeyConfiguration`（平台快捷鍵設定）

主要事件：`ColorValuesChanged`（系統色彩變更，包含深色模式切換）

### 重點程式碼範例（如有）
```csharp
// 處理平台快捷鍵
protected override void OnKeyDown(KeyEventArgs e)
{
    var hotkeys = TopLevel.GetTopLevel(this).PlatformSettings.HotkeyConfiguration;
    if (hotkeys.Copy.Any(g => g.Matches(e)))
    {
        // Handle Copy hotkey
    }
}
```

---

## IStorageProvider - 儲存空間服務

**來源：** https://docs.avaloniaui.net/docs/concepts/services/storage-provider/

`IStorageProvider` 是檔案和資料夾管理的核心，提供檔案選擇器和書籤功能。

取得方式：`var storage = window.StorageProvider;`

主要屬性：`CanOpen`、`CanSave`、`CanPickFolder`

主要方法：
- `OpenFilePickerAsync(FilePickerOpenOptions)`：開啟檔案選取對話框
- `SaveFilePickerAsync(FilePickerSaveOptions)`：開啟儲存檔案對話框
- `OpenFolderPickerAsync(FolderPickerOpenOptions)`：開啟資料夾選取對話框
- `TryGetFileFromPathAsync(Uri)`：依路徑取得檔案
- `TryGetFolderFromPathAsync(Uri)`：依路徑取得資料夾
- `TryGetWellKnownFolderAsync(WellKnownFolder)`：取得常用資料夾（文件、下載等）

---

## StorageProvider Bookmarks - 書籤功能

**來源：** https://docs.avaloniaui.net/docs/concepts/services/storage-provider/bookmarks

書籤在 iOS 和新版 macOS 等平台的沙盒安全機制中非常重要，允許應用程式在未來持續存取已授權的檔案或資料夾，而無需再次要求使用者選擇。

### 重點程式碼範例（如有）
```csharp
// 儲存書籤
var bookmarkId = await selectedFolder.SaveBookmarkAsync();
// 將 bookmarkId 儲存到本地資料庫

// 之後透過書籤重新開啟資料夾
var folder = await toplevel.StorageProvider.OpenFolderBookmarkAsync(bookmarkId);

// 讀取書籤檔案內容
var bookmarkedFile = await toplevel.StorageProvider.OpenFileBookmarkAsync(bookmarkId);
await using var readStream = await bookmarkedFile.OpenReadAsync();
using var reader = new StreamReader(readStream, Encoding.UTF8);
var content = await reader.ReadToEndAsync();

// 釋放書籤
if (folder is IStorageBookmarkItem bookmark)
    await bookmark.ReleaseBookmarkAsync();
```

---

## FilePickerOptions - 檔案選擇器選項

**來源：** https://docs.avaloniaui.net/docs/concepts/services/storage-provider/file-picker-options

**通用選項**：`Title`（標題列文字）、`SuggestedStartLocation`（建議起始位置）

**FilePickerOpenOptions**：`AllowMultiple`（允許多選）、`FileTypeFilter`（檔案類型篩選）

**FilePickerSaveOptions**：`SuggestedFileName`、`DefaultExtension`、`FileTypeChoices`、`ShowOverwritePrompt`

**FolderPickerOpenOptions**：`AllowMultiple`

內建檔案類型：`FilePickerFileTypes.All`、`TextPlain`、`ImageAll`、`ImageJpg`、`ImagePng`、`ImageWebP`、`Pdf`

### 重點程式碼範例（如有）
```csharp
// 自訂檔案類型
public static FilePickerFileType ImageAll { get; } = new("All Images")
{
    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" },
    AppleUniformTypeIdentifiers = new[] { "public.image" },
    MimeTypes = new[] { "image/*" }
};
```

---

## IStorageItem / IStorageFile / IStorageFolder 成員

**來源：** https://docs.avaloniaui.net/docs/concepts/services/storage-provider/storage-item

通用成員：`Name`、`Path`（注意：Android 使用 `content:` 協議）、`CanBookmark`、`SaveBookmarkAsync()`、`GetBasicPropertiesAsync()`（Size/DateCreated/DateModified）、`GetParentAsync()`、`DeleteAsync()`、`MoveAsync(IStorageFolder)`

`IStorageFile` 額外方法：`OpenReadAsync()`（讀取串流）、`OpenWriteAsync()`（寫入串流）

`IStorageFolder` 額外方法：`GetItemsAsync()`（列出子項目，懶評估）、`CreateFileAsync(name)`、`CreateFolderAsync(name)`

**警告**：不要用 `Path` 屬性保留存取權限，應使用 Bookmarks。

---

## TemplatedControl - 樣板化控制項

**來源：** https://docs.avaloniaui.net/docs/concepts/templated-controls

`TemplatedControl` 適用於可跨應用程式共用的通用控制項，是「lookless」控制項，可為不同主題重新樣式化。大多數 Avalonia 標準控制項屬於此類別。

若在獨立檔案中提供樣式，需在 `Application` 中透過 `StyleInclude` 引入。

---

## The Main Window - 主視窗

**來源：** https://docs.avaloniaui.net/docs/concepts/the-main-window

主視窗是在 `App.axaml.cs` 的 `OnFrameworkInitializationCompleted` 方法中傳給 `ApplicationLifetime.MainWindow` 的視窗。

### 重點程式碼範例（如有）
```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
    {
        desktopLifetime.MainWindow = new MainWindow();
    }
}
```

**重要**：行動裝置和瀏覽器平台沒有 Window 概念，需設定 `MainView` 並實作 `ISingleViewApplicationLifetime`。

---

## MVVM 模式概覽

**來源：** https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern/

MVVM（Model-View-ViewModel）模式通過資料綁定將 UI（View）和應用邏輯（ViewModel）分離：
- **Model**：資料儲存和業務服務
- **View**：XAML 定義的 UI，AXAML 檔案 + code-behind
- **ViewModel**：純程式碼類別，不依賴 UI 平台，可進行單元測試

使用時機：應用程式隨時間成長變複雜時，MVVM 的優勢才會明顯。小型應用可先使用 code-behind 模式。

---

## Avalonia UI 與 MVVM

**來源：** https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern/avalonia-ui-and-mvvm

在 Avalonia 中實現 MVVM：
- **View**：AXAML 檔案 + code-behind，由內建控制項、UserControl、自訂控制項組成
- **ViewModel**：普通 C# 類別，通過資料綁定連接到 View
- **Model**：通過 Dependency Injection (DI) 模式與 ViewModel 分離

資料綁定方向：
- 雙向（`<->`）：輸入控制項（TextBox 等）
- 單向（`->`）：命令（Button 等）

推薦框架：ReactiveUI（Avalonia 套件支援）

---

## TopLevel - 頂層控制項

**來源：** https://docs.avaloniaui.net/docs/concepts/toplevel

`TopLevel` 是所有頂層控制項（如 Window）的基底類別，處理佈局排程、樣式和渲染，是存取平台服務的主要入口。

### 重點程式碼範例（如有）
```csharp
// 取得 TopLevel
var topLevel = TopLevel.GetTopLevel(control);

// 存取各服務
var clipboard = topLevel.Clipboard;
var focusManager = topLevel.FocusManager;
var storage = topLevel.StorageProvider;
var insetsManager = topLevel.InsetsManager;
var platformSettings = topLevel.PlatformSettings;
```

主要屬性：`ClientSize`、`FrameSize`、`RenderScaling`、`RequestedThemeVariant`、`TransparencyLevelHint`

主要事件：`BackRequested`（返回按鈕）、`Closed`、`Opened`、`ScalingChanged`

---

## UI Composition - UI 組合

**來源：** https://docs.avaloniaui.net/docs/concepts/ui-composition

UI Composition 是通過元件組合建立版面配置的方式，優點：封裝複雜性、重用一致行為。

Avalonia 的 UI 元件類型：
- **Windows**：基本佈局單元（視窗平台）
- **Built-in Controls**：內建控制項（Button、TextBox、Grid 等）
- **User Controls**：使用者自訂控制項（XAML + code-behind）
- **Custom Controls**：自訂繪製控制項
- **Template Controls**：樣板化控制項（Lookless）

控制項關係以樹狀結構表示（Logical Tree 和 Visual Tree），有助於樣式和資料綁定的繼承查找。

---

## 未處理例外狀況

**來源：** https://docs.avaloniaui.net/docs/concepts/unhandledexceptions

### 重點程式碼範例（如有）
```csharp
// UI 執行緒例外（謹慎使用）
Dispatcher.UIThread.UnhandledException += (s, e) =>
{
    Console.WriteLine($"Unhandled: {e.Exception}");
    // e.Handled = true; // 謹慎標記為已處理
};

// 全域 try-catch（Program.cs）
public static void Main(string[] args)
{
    try
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    catch (Exception e)
    {
        Log.Fatal(e, "Something very bad happened");
    }
    finally
    {
        Log.CloseAndFlush();
    }
}

// Task 例外
TaskScheduler.UnobservedTaskException += ...;

// ReactiveUI 例外（需在任何 ReactiveCommand 建立前設定）
RxApp.DefaultExceptionHandler = ...;
```

---

## ViewLocator - 視圖定位器

**來源：** https://docs.avaloniaui.net/docs/concepts/view-locator

`ViewLocator` 是可選的 MVVM 輔助工具（Avalonia 預設範本已包含），實作 `IDataTemplate` 將 ViewModel 型別對應到對應的 View 型別。

預設實作（反射）：將型別名稱中的 `ViewModel` 替換為 `View`
例如：`MyApp.ViewModels.MainViewModel` → `MyApp.Views.MainView`

### 重點程式碼範例（如有）
```csharp
// 預設反射實作
public class ViewLocator : IDataTemplate
{
    public Control Build(object data)
    {
        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);
        return type != null ? (Control)Activator.CreateInstance(type)! : new TextBlock { Text = "Not Found: " + name };
    }
    public bool Match(object data) => data is ViewModelBase;
}

// 型別安全實作（推薦，AOT 相容）
public class ViewLocator : IDataTemplate
{
    public Control Build(object data) => data switch
    {
        MainViewModel vm => new MainView { DataContext = vm },
        SettingsViewModel vm => new SettingsView { DataContext = vm },
        _ => new TextBlock { Text = $"View not found for {data.GetType().Name}" }
    };
    public bool Match(object data) => data is ViewModelBase;
}
```

在 `App.axaml` 中註冊：
```xml
<Application.DataTemplates>
    <local:ViewLocator />
</Application.DataTemplates>
```

---

## 指南概覽

**來源：** https://docs.avaloniaui.net/docs/guides/

Avalonia 指南涵蓋：跨平台應用程式建置、自訂控制項開發、資料綁定、開發工具使用、圖形和動畫、平台特定功能等主題。

---

## 實作指南索引

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/

涵蓋 MVVM 模式、相依性注入、開發者工具、錯誤記錄、IDE 支援、設計時期資料、本地化等實作技術。

---

## Code-Behind 程式設計模式

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/code-behind

每個 Window/UserControl XAML 檔案都有對應的 `.axaml.cs` code-behind 檔案，類別名稱須與 XAML 的 `x:Class` 屬性一致。適合小型應用程式；大型應用建議使用 MVVM 模式。

- 在 XAML 中設定控制項 `Name` 屬性，可在 code-behind 中直接取用。
- 事件處理器在 code-behind 中定義，並在 XAML 的事件屬性中參照。

### 重點程式碼範例（如有）
```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void GreetingButtonClickHandler(object sender, RoutedEventArgs e)
    {
        GreetingButton.Background = Brushes.Blue;
    }
}
```
```xml
<Button Name="GreetingButton" Click="GreetingButtonClickHandler">Hello World</Button>
```

---

## 開發者工具（DevTools）

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/developer-tools

Avalonia 內建 DevTools，預設在 DEBUG 模式下按 F12 開啟（需 `Avalonia.Diagnostics` NuGet 套件）：
- **Logical/Visual Tree**：檢視控制項樹狀結構並即時修改屬性。
- **Properties**：檢視屬性名稱、值、型別、優先層級。
- **Layout**：檢視並修改 Margin/Border/Padding 等排版屬性。
- **Styles**：查看所有樣式規則與目前生效的值。

```csharp
dotnet add package Avalonia.Diagnostics
```

---

## 相依性注入（Dependency Injection）

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/how-to-implement-dependency-injection

使用 `Microsoft.Extensions.DependencyInjection` 管理服務注入：

### 重點程式碼範例（如有）
```csharp
// 安裝套件
// dotnet add package Microsoft.Extensions.DependencyInjection

// 建立 ServiceCollection 擴充方法
public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<IRepository, Repository>();
        collection.AddTransient<BusinessService>();
        collection.AddTransient<MainViewModel>();
    }
}

// 在 App.axaml.cs 中使用
public override void OnFrameworkInitializationCompleted()
{
    var collection = new ServiceCollection();
    collection.AddCommonServices();
    var services = collection.BuildServiceProvider();
    var vm = services.GetRequiredService<MainViewModel>();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow { DataContext = vm };

    base.OnFrameworkInitializationCompleted();
}
```

---

## 使用設計時期資料（Design-Time Data）

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/how-to-use-design-time-data

建立 ViewModel 的設計版本並填入模擬資料，在 XAML 中用 `Design.DataContext` 宣告設計時期資料內容：

### 重點程式碼範例（如有）
```csharp
public class DesignAppointmentViewModel : AppointmentViewModel
{
    public DesignAppointmentViewModel()
    {
        ServerName = "John Price";
        ServiceTitle = "Hair Cut";
        ServicePrice = 25.5m;
    }
}
```
```xml
<UserControl xmlns:vm="using:MyApp.ViewModels"
             x:DataType="vm:DesignAppointmentViewModel">
    <Design.DataContext>
        <vm:DesignAppointmentViewModel/>
    </Design.DataContext>
</UserControl>
```

---

## 使用 MVVM 模式

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/how-to-use-the-mvvm-pattern

本頁介紹在 Avalonia 中如何使用 MVVM 模式，核心是 `INotifyPropertyChanged` 介面（詳見資料綁定章節）。

---

## IDE 支援與 XAML 預覽

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/ide-support

Avalonia for Visual Studio 擴充功能提供 XAML 即時預覽。設計時期屬性：
- `d:DesignWidth`/`d:DesignHeight`：設定預覽尺寸。
- `d:DataContext`：指定設計時期資料來源。
- `Design.DataContext`：另一種設定方式，支援從程式碼設定（`Design.SetDataContext()`）。

### 重點程式碼範例（如有）
```xml
<Window xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        d:DesignWidth="800" d:DesignHeight="450">
    <Design.DataContext>
        <vm:MainViewModel />
    </Design.DataContext>
</Window>
```

---

## 本地化（ResX 檔案）

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/localizing

使用 `.resx` 資源檔進行多語言本地化：

### 重點程式碼範例（如有）
```csharp
// 在 App.axaml.cs 中設定語言
Lang.Resources.Culture = new CultureInfo("fil-PH");
```
```xml
<!-- 在 XAML 中使用本地化字串 -->
<TextBlock Text="{x:Static lang:Resources.GreetingText}"/>
```

注意：資源類別需使用 `PublicResXFileCodeGenerator` 確保可從 XAML 存取。

---

## 記錄錯誤與警告

**來源：** https://docs.avaloniaui.net/docs/guides/implementation-guides/logging-errors-and-warnings

在 `Program.cs` 的 `BuildAvaloniaApp` 方法中呼叫 `LogToTrace()`：

### 重點程式碼範例（如有）
```csharp
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace(LogEventLevel.Verbose);
```

記錄訊息將顯示在 IDE 的 Debug Output 視窗中。

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter01 — Core Concepts: MVVM, AvaloniaProperty, and Architecture

*(詳見 getting-started.md Chapter01 的 MVVM 建構區塊與架構分層)*

**AvaloniaProperty 體系**
- `AvaloniaObject`：所有控制項基類，提供依賴屬性系統存取
- `StyledElement`：新增樣式、資源和邏輯樹，繼承自 `AvaloniaObject`
- `AvaloniaLocator`：輕量服務定位器，用於解析框架服務

**ViewLocator 模式**
```csharp
// ReactiveUI 提供預設 ViewLocator
// 自訂服務：按命名慣例解析 XAML 類型
public class AppViewLocator : IViewLocator
{
    public IViewFor? ResolveView<T>(T viewModel, string? contract = null) where T : class
    {
        var name = viewModel.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);
        return type is null ? null : (IViewFor?)Activator.CreateInstance(type);
    }
}
```

---

### Chapter04 — Application Concepts: Lifetimes and DI

*(詳見 getting-started.md Chapter04)*

---

### Chapter09 — Concepts: Routed Events and Command Pipeline

**事件流程**
1. 裝置發出原始事件（`PointerPressed`、`KeyDown`），每個都是具有路由策略的 `RoutedEvent`
2. `InputElement` 主持事件元數據，觸發類別處理程序和實例處理程序
3. 手勢識別器訂閱指標流，發出語義事件（`Tapped`、`DoubleTapped`）
4. 指令來源（`Button.Command`、`KeyBinding`）執行 `ICommand`

**使用 vs 事件決策**

| 用 Command 的時機 | 用 Event 的時機 |
|------------------|----------------|
| 從 ViewModel 公開操作（Save/Delete） | 需要指標座標、差量或低階控制 |
| 需要 CanExecute/禁用邏輯 | 實作自訂手勢/拖曳互動 |
| 操作從按鈕、選單、快捷鍵觸發 | 工作純屬視覺或特定於視圖 |
| 計劃進行單元測試 | 資料是暫時的或需要立即 UI 回饋 |

---

### Chapter22 — Rendering Pipeline

#### 關鍵概念

**渲染流程架構**
1. **UI Thread**：建置/更新 Visual Tree，屬性變更標記 dirty
2. **Scene Graph**：批次化的 Visual 繪圖操作
3. **Compositor**：UI → Render Thread 的場景更新
4. **Render Loop**：由 `IRenderTimer` 驅動，在有 dirty 內容時排程 frame
5. **Renderer**：遍歷 scene graph 發出繪圖指令
6. **Skia**：光柵化至 GPU 紋理或 CPU bitmap

**SkiaOptions（全域調整）**
```csharp
AppBuilder.Configure<App>()
    .UseSkia(new SkiaOptions
    {
        MaxGpuResourceSizeBytes = 64L * 1024 * 1024,
        UseOpacitySaveLayer = false
    })
```

**RenderOptions（per-Visual 調整）**
- `BitmapInterpolationMode`：圖片縮放品質
- `TextRenderingMode`：文字渲染模式
- `EdgeMode`：幾何邊緣模式

**何時觸發 Frame**
屬性變更、Layout 更新、Animation、輸入事件、視窗 resize/DPI 變更。

#### 代碼範例

```csharp
// 強制軟體渲染（偵錯用）
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseSkia(new SkiaOptions { RenderMode = RenderMode.Software })
```

```csharp
// per-Visual RenderOptions
RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
RenderOptions.SetTextRenderingMode(smallText, TextRenderingMode.Aliased);
```

```csharp
// 啟用渲染偵錯 Overlay
if (TopLevel is { Renderer: { } renderer })
    renderer.DebugOverlays = RendererDebugOverlays.Fps | RendererDebugOverlays.LayoutTimeGraph;
```

```csharp
// 離屏渲染
var bitmap = new RenderTargetBitmap(new PixelSize(300, 200), new Vector(96, 96));
await bitmap.RenderAsync(myControl);
bitmap.Save("snapshot.png");
```

```csharp
// 自訂 Render Timer
AppBuilder.Configure<App>()
    .UseRenderLoop(new RenderLoop(new DispatcherRenderTimer()))
```

---

### Chapter28 — Advanced Input Handling

#### 關鍵概念

**Input Pipeline**
1. Raw input → `RawInputEventArgs`
2. Pre-process observers（`InputManager.Instance?.PreProcess`）
3. Device routing → 路由事件
4. Process/PostProcess observers

**Pointer Capture**
- 呼叫 `e.Pointer.Capture(this)` 確保拖曳期間持續收到事件
- 一定要在完成/取消時 `Capture(null)`，並監聽 `PointerCaptureLost`

**Multi-touch / Pen**
- 用 `Pointer.Id` 區分不同接觸點
- `PointerPoint.Properties` 提供壓力、傾斜、ContactRect

**Gesture Recognizers**
- 預定義：`Tapped`, `DoubleTapped`, `RightTapped`
- 可組合：`PinchGestureRecognizer`, `ScrollGestureRecognizer`, `PullGestureRecognizer`
- 自訂：繼承 `GestureRecognizer`

**XYFocus（遊戲手柄/遙控器）**
```xml
<StackPanel input:XYFocus.Up="{Binding ElementName=SearchBox}"
            input:XYFocus.NavigationModes="Keyboard,Gamepad" />
```

**IME / 文字輸入**
`TextInputOptions` 附加屬性控制鍵盤行為（ContentType、ReturnKeyType、IsSensitive）。

#### 代碼範例

```csharp
// Pointer Capture（拖曳）
protected override void OnPointerPressed(PointerPressedEventArgs e)
{
    if (e.Pointer.Type == PointerType.Touch)
    {
        e.Pointer.Capture(this);
        _dragStart = e.GetPosition(this);
        e.Handled = true;
    }
}

protected override void OnPointerReleased(PointerReleasedEventArgs e)
{
    if (ReferenceEquals(e.Pointer.Captured, this))
    {
        e.Pointer.Capture(null);
        CompleteDrag(e.GetPosition(this));
        e.Handled = true;
    }
}
```

```csharp
// 自訂 Gesture Recognizer（長按）
public class PressAndHoldRecognizer : GestureRecognizer
{
    public static readonly RoutedEvent<RoutedEventArgs> PressAndHoldEvent =
        RoutedEvent.Register<InputElement, RoutedEventArgs>(
            nameof(PressAndHoldEvent), RoutingStrategies.Bubble);

    public TimeSpan Threshold { get; set; } = TimeSpan.FromMilliseconds(600);
    private CancellationTokenSource? _hold;
    private Point _pressOrigin;

    protected override async void PointerPressed(PointerPressedEventArgs e)
    {
        if (Target is not Visual visual) return;
        _pressOrigin = e.GetPosition(visual);
        Capture(e.Pointer);
        _hold = new CancellationTokenSource();
        try
        {
            await Task.Delay(Threshold, _hold.Token);
            Target?.RaiseEvent(new RoutedEventArgs(PressAndHoldEvent));
        }
        catch (TaskCanceledException) { }
    }

    protected override void PointerMoved(PointerEventArgs e)
    {
        if (Target is not Visual visual || _hold is null) return;
        var current = e.GetPosition(visual);
        if ((current - _pressOrigin).Length > 8)
            _hold.Cancel();
    }

    protected override void PointerReleased(PointerReleasedEventArgs e) => _hold?.Cancel();
    protected override void PointerCaptureLost(IPointer pointer) => _hold?.Cancel();
}
```

```csharp
// 全域 KeyBinding
KeyBindings.Add(new KeyBinding
{
    Gesture = new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift),
    Command = ViewModel.NewNoteCommand
});
```

```xml
<!-- IME 文字選項 -->
<TextBox
    Text=""
    input:TextInputOptions.ContentType="Email"
    input:TextInputOptions.ReturnKeyType="Send"
    input:TextInputOptions.ShowSuggestions="True"
    input:TextInputOptions.IsSensitive="False" />
```

---

### Chapter30 — XAML Compiler & Runtime Loading

#### 關鍵概念

**XAML 資產管線（2 個 MSBuild Tasks）**
1. `GenerateAvaloniaResources`：打包 `AvaloniaResource` 為 `avares://` bundle
2. `CompileAvaloniaXaml`（XamlIl）：將 XAML 編譯為 IL，生成 partial class 和 compiled bindings

**XamlIl 編譯流程**
1. 解析 → AST
2. Transform passes（命名空間解析、Markup Extension 展開）
3. IL 生成（`CompiledAvaloniaXaml.!XamlLoader` 等）
4. Runtime helpers（deferred templates、資源解析）

**Runtime Loading**
`AvaloniaXamlLoader` 選擇：已編譯的 loader → 若無則使用 Runtime loader（反射較重，適合 prototyping）。

**自訂 Markup Extension**
繼承 `MarkupExtension`，實作 `ProvideValue(IServiceProvider)`。

**重要 MSBuild 屬性**
- `<VerifyXamlIl>true</VerifyXamlIl>`：啟用 IL 驗證
- `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`：全局 compiled bindings

#### 代碼範例

```csharp
// 自訂 Markup Extension
public class UppercaseExtension : MarkupExtension
{
    public string? Text { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var source = Text ?? serviceProvider.GetDefaultAnchor() as TextBlock;
        return source switch
        {
            string s => s.ToUpperInvariant(),
            TextBlock block => block.Text?.ToUpperInvariant() ?? string.Empty,
            _ => string.Empty
        };
    }
}
```

```xml
<!-- 使用自訂 Markup Extension -->
<TextBlock Text="{local:Uppercase Text=hello}"/>
```

```csharp
// 在組件層級宣告 XML 命名空間
[assembly: XmlnsDefinition("https://schemas.myapp.com/ui", "MyApp.Controls")]
[assembly: XmlnsPrefix("https://schemas.myapp.com/ui", "myapp")]
```

---

### Chapter36 — Code-First Templates & Factories

#### 關鍵概念

**FuncControlTemplate**
程式碼版的 `ControlTemplate`，Lambda 接收 templated parent 與 `INameScope`，回傳控件樹。每次應用模板時 Lambda 都會重新執行。

**FuncDataTemplate**
為資料項目建立視覺，可設 `recycle: true` 參與 virtualization。

**FuncTreeDataTemplate**
用於階層資料（TreeView），接收子項目 accessor 與 recycling 旗標。

**InstancedBinding**
預設好 source 的 binding，適合模板中需要同時綁定多個來源的情境。

**動態模板切換**
模板是 CLR 物件，可隨時替換；`ContentPresenter.UpdateChild()` 強制重新套用新模板。

**RecyclingElementFactory + ItemsRepeater**
適合效能敏感的大量列表，比 `ItemsControl` 更細緻的 virtualization 控制。

#### 代碼範例

```csharp
// FuncControlTemplate
public static ControlTemplate CreateCardTemplate()
{
    return new FuncControlTemplate<ContentControl>((parent, scope) =>
    {
        var border = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = new ContentPresenter { Name = "PART_ContentPresenter" }
        };
        scope?.RegisterNamed("PART_ContentPresenter", border.Child);
        return border;
    });
}
```

```csharp
// FuncDataTemplate（含 recycling）
var itemTemplate = new FuncDataTemplate<OrderItem>((item, _) =>
    new Border
    {
        Margin = new Thickness(0, 0, 0, 12),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = item.ProductName, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = item.Quantity.ToString() }
            }
        }
    }, recycle: true);

itemsControl.ItemTemplate = itemTemplate;
```

```csharp
// FuncTreeDataTemplate（TreeView 階層）
var treeTemplate = new FuncTreeDataTemplate<DirectoryNode>((item, _) =>
    new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Children = { new TextBlock { Text = item.Name } }
    },
    x => x.Children,
    true);

var treeView = new TreeView { Items = fileSystem.RootNodes, ItemTemplate = treeTemplate };
```

```csharp
// ItemsRepeater + RecyclingElementFactory
var factory = new RecyclingElementFactory
{
    RecycleKey = "Widget",
    Template = new FuncDataTemplate<IWidgetViewModel>(
        (item, _) => WidgetFactory.CreateControl(item))
};
var items = new ItemsRepeater { ItemTemplate = factory };
```

```csharp
// 動態模板切換
contentControl.Bind(ContentControl.ContentTemplateProperty, new Binding("SelectedTemplate")
{
    Mode = BindingMode.OneWay
});
```

---