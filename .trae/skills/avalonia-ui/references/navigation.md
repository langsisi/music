# Avalonia 導覽知識庫

此檔案包含 Avalonia 頁面導覽相關文件摘要。

---

## ReactiveUI 路由 (ReactiveUI Routing)

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/routing

ReactiveUI 路由系統由三個核心組成：
- **IScreen**：包含 `RoutingState` 的導覽根節點（不一定要佔滿整個螢幕）
- **RoutingState**：管理 ViewModel 導覽堆疊，提供前進/後退命令
- **IRoutableViewModel**：可導覽的 ViewModel，需實作 `UrlPathSegment` 識別碼
- **RoutedViewHost**：XAML 控制項，監控 RoutingState 並自動建立對應 View

### 重點程式碼範例（如有）
```csharp
// FirstViewModel.cs
public class FirstViewModel : ReactiveObject, IRoutableViewModel
{
    public IScreen HostScreen { get; }
    public string UrlPathSegment { get; } = Guid.NewGuid().ToString().Substring(0, 5);
    public FirstViewModel(IScreen screen) => HostScreen = screen;
}

// MainWindowViewModel.cs - 實作 IScreen
public class MainWindowViewModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new RoutingState();
    public ReactiveCommand<Unit, IRoutableViewModel> GoNext { get; }
    public ReactiveCommand<Unit, IRoutableViewModel> GoBack => Router.NavigateBack;

    public MainWindowViewModel()
    {
        GoNext = ReactiveCommand.CreateFromObservable(
            () => Router.Navigate.Execute(new FirstViewModel(this))
        );
    }
}
```

```xml
<!-- MainWindow.xaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:rxui="http://reactiveui.net"
        xmlns:app="clr-namespace:RoutingExample">
    <Grid>
        <rxui:RoutedViewHost Grid.Row="0" Router="{Binding Router}">
            <rxui:RoutedViewHost.DefaultContent>
                <TextBlock Text="Default content" />
            </rxui:RoutedViewHost.DefaultContent>
            <rxui:RoutedViewHost.ViewLocator>
                <app:AppViewLocator />
            </rxui:RoutedViewHost.ViewLocator>
        </rxui:RoutedViewHost>
        <StackPanel Grid.Row="1" Orientation="Horizontal">
            <Button Content="Go next" Command="{Binding GoNext}" />
            <Button Content="Go back" Command="{Binding GoBack}" />
        </StackPanel>
    </Grid>
</Window>
```

---

## 使用 ViewLocator 實作多頁面應用程式

**來源：** https://docs.avaloniaui.net/docs/guides/development-guides/how-to-implement-multi-page-apps

透過 `ViewLocator` 類別（`IDataTemplate`）根據 ViewModel 型別名稱自動尋找對應的 View，是 Avalonia MVVM 範本內建的多頁面導覽模式：

### 重點程式碼範例（如有）
```csharp
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data == null) return null;
        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);
        if (type != null)
            return (Control)Activator.CreateInstance(type)!;
        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
```

使用方式：在 XAML 中將 `ContentControl.Content` 綁定到目前的 ViewModel，ViewLocator 會自動尋找並建立對應的 View。

---

## 使用 SplitView 搭配 MVVM 實作側邊欄

**來源：** https://docs.avaloniaui.net/docs/guides/development-guides/how-to-show-and-hide-a-split-view-pane-with-mvvm

可用 MVVM 模式搭配 SplitView 控制項實作可展開/收合的工具面板（tool pane）UI。使用複雜 binding path 定位父 ViewModel（文件尚在準備中）。

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter12 — Lifetimes, Windows, and Navigation

**App 生命週期處理**
```csharp
public override void OnFrameworkInitializationCompleted()
{
    var services = ConfigureServices();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.MainWindow = services.GetRequiredService<MainWindow>();
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }
    else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
    {
        singleView.MainView = services.GetRequiredService<ShellView>();
    }

    base.OnFrameworkInitializationCompleted();
}
```

**Modal 和 Modeless 視窗**
```csharp
// Modal 對話方塊
public Task ShowAboutDialogAsync(Window owner)
    => new AboutWindow { Owner = owner }.ShowDialog(owner);

// Modeless 工具視窗
var tool = new ToolWindow { Owner = this };
tool.Show();
```

**多螢幕定位**
```csharp
var topLevel = TopLevel.GetTopLevel(this);
if (topLevel?.Screens is { } screens)
{
    var screen = screens.ScreenFromPoint(Position);
    var workingArea = screen.WorkingArea;
    Position = new PixelPoint(workingArea.X, workingArea.Y);
}

// 監聽螢幕變化
screens.Changed += (_, _) =>
{
    var active = screens.ScreenFromWindow(this);
    Logger.LogInformation("Monitor layout changed. Active: {Bounds}", active.WorkingArea);
};
```

**防止關閉視窗（未儲存變更）**
```csharp
Closing += async (sender, e) =>
{
    if (DataContext is ShellViewModel vm && vm.HasUnsavedChanges)
    {
        var confirm = await MessageBox.ShowAsync(this, "Exit without saving?", ...);
        if (!confirm) e.Cancel = true;
    }
};
```

**ShutdownRequested 處理**
```csharp
if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
{
    desktop.ShutdownRequested += (_, e) =>
    {
        if (_documentStore.HasDirtyDocuments && !ConfirmShutdown())
            e.Cancel = true;
    };
}
```

**內容控制項導覽（桌面 + 行動通用）**
```csharp
public sealed class ShellViewModel : ObservableObject
{
    private readonly INavigationService _nav;
    public object? Current => _nav.Current;
    public RelayCommand GoHome { get; }
    public RelayCommand GoSettings { get; }

    public ShellViewModel(INavigationService nav)
    {
        _nav = nav;
        GoHome = new RelayCommand(_ => _nav.NavigateTo<HomeViewModel>());
        GoSettings = new RelayCommand(_ => _nav.NavigateTo<SettingsViewModel>());
        _nav.NavigateTo<HomeViewModel>();
    }
}
```

```xml
<DockPanel>
  <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8">
    <Button Content="Home" Command="{Binding GoHome}"/>
    <Button Content="Settings" Command="{Binding GoSettings}"/>
  </StackPanel>
  <TransitioningContentControl Content="{Binding Current}"/>
</DockPanel>
```

**SplitView Shell 導覽**
```xml
<SplitView IsPaneOpen="{Binding IsPaneOpen}"
           DisplayMode="CompactOverlay"
           CompactPaneLength="48" OpenPaneLength="200">
  <SplitView.Pane>
    <ItemsControl ItemsSource="{Binding NavigationItems}">
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <Button Content="{Binding Title}" Command="{Binding NavigateCommand}"/>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </SplitView.Pane>
  <TransitioningContentControl Content="{Binding Current}"/>
</SplitView>
```

**TopLevel 服務（剪貼簿、Storage、螢幕）**
```csharp
// 剪貼簿
var topLevel = TopLevel.GetTopLevel(control);
if (topLevel?.Clipboard is { } clipboard)
    await clipboard.SetTextAsync("Copied text");

// 系統返回導覽（Android/Browser）
topLevel.BackRequested += (_, e) =>
{
    if (_navigation.Pop()) e.Handled = true;
};
```

---

### Chapter13 — Menus, Dialogs, Tray Icons

**Menu 結構（XAML）**
```xml
<Menu DockPanel.Dock="Top">
  <MenuItem Header="_File">
    <MenuItem Header="_New" Command="{Binding AppCommands.New}" HotKey="Ctrl+N"/>
    <MenuItem Header="_Open..." Command="{Binding AppCommands.Open}" HotKey="Ctrl+O"/>
    <MenuItem Header="_Save" Command="{Binding AppCommands.Save}" HotKey="Ctrl+S"/>
    <Separator/>
    <MenuItem Header="E_xit" Command="{Binding AppCommands.Exit}"/>
  </MenuItem>
  <MenuItem Header="_Edit">
    <MenuItem Header="_Undo" Command="{Binding AppCommands.Undo}"/>
    <MenuItem Header="_Redo" Command="{Binding AppCommands.Redo}"/>
  </MenuItem>
</Menu>
```

**全域鍵盤繫結**
```xml
<Window.InputBindings>
  <KeyBinding Gesture="Ctrl+N" Command="{Binding AppCommands.New}"/>
  <KeyBinding Gesture="Ctrl+O" Command="{Binding AppCommands.Open}"/>
</Window.InputBindings>
```

**NativeMenu（macOS 選單列）**
```csharp
private static NativeMenu BuildNativeMenu()
{
    var appMenu = new NativeMenu
    {
        new NativeMenuItem("About", (_, _) => Locator.Commands.ShowAbout.Execute(null)),
        new NativeMenuItemSeparator(),
        new NativeMenuItem("Quit", (_, _) => Locator.Commands.Exit.Execute(null))
    };

    return new NativeMenu
    {
        new NativeMenuItem("MyApp") { Menu = appMenu },
        new NativeMenuItem("File") { Menu = new NativeMenu { new NativeMenuItem("New") { Gesture = new KeyGesture(Key.N, KeyModifiers.Control) } } }
    };
}
```

**ContextMenu（清單項目右鍵選單）**
```xml
<ListBox Items="{Binding Documents}">
  <ListBox.Styles>
    <Style Selector="ListBoxItem">
      <Setter Property="ContextMenu">
        <ContextMenu>
          <MenuItem Header="Rename"
                    Command="{Binding DataContext.Rename, RelativeSource={RelativeSource AncestorType=ListBox}}"
                    CommandParameter="{Binding}"/>
          <MenuItem Header="Delete"
                    Command="{Binding DataContext.Delete, RelativeSource={RelativeSource AncestorType=ListBox}}"
                    CommandParameter="{Binding}"/>
        </ContextMenu>
      </Setter>
    </Style>
  </ListBox.Styles>
</ListBox>
```

**檔案對話方塊服務（IFileDialogService）**
```csharp
public interface IFileDialogService
{
    Task<IReadOnlyList<FilePickResult>> PickFilesAsync(FilePickerOpenOptions options, CancellationToken ct = default);
    Task<FilePickResult?> SaveFileAsync(FilePickerSaveOptions options, CancellationToken ct = default);
}

public sealed class FileDialogService : IFileDialogService
{
    private readonly TopLevel _topLevel;
    public FileDialogService(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<IReadOnlyList<FilePickResult>> PickFilesAsync(FilePickerOpenOptions options, CancellationToken ct = default)
    {
        var provider = _topLevel.StorageProvider;
        if (provider is { CanOpen: true })
        {
            var files = await provider.OpenFilePickerAsync(options, ct);
            return files.Select(f => new FilePickResult(f.TryGetLocalPath() ?? f.Name, f)).ToArray();
        }
        return Array.Empty<FilePickResult>();
    }
}
```

**系統匣圖示（TrayIcon）**
```csharp
var trayIcons = new TrayIcons
{
    new TrayIcon
    {
        Icon = new WindowIcon("avares://MyApp/Assets/App.ico"),
        ToolTipText = "My App",
        Menu = new NativeMenu
        {
            new NativeMenuItem("Show", (_, _) => Locator.Commands.ShowMain.Execute(null)),
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Exit", (_, _) => Locator.Commands.Exit.Execute(null))
        }
    }
};
TrayIcon.SetIcons(this, trayIcons);
```

**Toast 通知**
```csharp
var manager = new WindowNotificationManager(_desktopLifetime.MainWindow!)
{
    Position = NotificationPosition.TopRight, MaxItems = 3
};
manager.Show(new Notification("Saved", "Document saved successfully.", NotificationType.Success));
```

---