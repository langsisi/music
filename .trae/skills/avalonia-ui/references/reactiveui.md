# Avalonia ReactiveUI 知識庫

此檔案包含 Avalonia 與 ReactiveUI 整合相關文件摘要。

---

## 綁定到排序/過濾清單

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/binding-to-sorted-filtered-list

使用 `SourceCache<TObject, TKey>` 或 `SourceList<T>` 搭配 `ReadOnlyObservableCollection<T>` 實現排序和過濾的資料集合綁定。

### 重點程式碼範例（如有）
```csharp
private readonly ReadOnlyObservableCollection<TestViewModel> _testViewModels;
public ReadOnlyObservableCollection<TestViewModel> TestViewModels => _testViewModels;

public MainWindowViewModel()
{
    _sourceCache.Connect()
        .Sort(SortExpressionComparer<TestViewModel>.Ascending(t => t.OrderIndex))
        .Filter(x => x.Id.ToString().EndsWith('1'))
        .Bind(out _testViewModels)
        .Subscribe();
}
```

---

## 資料持久化 (Data Persistence)

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/data-persistence

ReactiveUI 提供應用程式暫停/恢復時保存 ViewModel 狀態的功能。使用 `[DataMember]` 標記要保存的屬性，`[IgnoreDataMember]` 標記不需保存的（如命令）。

### 重點程式碼範例（如有）
```csharp
[DataContract]
public class MainViewModel : ReactiveObject
{
    [IgnoreDataMember]
    public ReactiveCommand<Unit, Unit> Search { get; }

    [DataMember]
    public string SearchQuery
    {
        get => _searchQuery;
        set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
    }
}

// App.axaml.cs 中設定暫停驅動器
public override void OnFrameworkInitializationCompleted()
{
    var suspension = new AutoSuspendHelper(ApplicationLifetime);
    RxApp.SuspensionHost.CreateNewAppState = () => new MainViewModel();
    RxApp.SuspensionHost.SetupDefaultSuspendResume(new NewtonsoftJsonSuspensionDriver("appstate.json"));
}
```

---


**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/

ReactiveUI 是進階的函數式反應式 MVVM 框架。Avalonia UI 透過 `ReactiveUI.Avalonia` NuGet 套件整合 ReactiveUI。ReactiveUI 不是必須的，Avalonia 支援任何 MVVM 框架或自訂方案。

---

## ReactiveViewModel 基礎

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/reactive-view-model

ReactiveUI 提供 `ReactiveObject` 作為 ViewModel 基底類別，實作屬性變更通知和 Observable 監控。Avalonia MVVM 範本會自動加入 `ViewModelBase`。

### 重點程式碼範例（如有）
```csharp
// ViewModelBase
public class ViewModelBase : ReactiveObject {}

// 在 ViewModel 中使用 RaiseAndSetIfChanged
public class MyViewModel : ViewModelBase
{
    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }
}
```

---

## ReactiveCommand

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/reactive-command

`ReactiveCommand` 用於實作「功能揭示（Revealed Functionality）」UI 原則：功能只在有效時才可用。使用 `WhenAnyValue` 建立 Observable 監控屬性，傳入 `canExecute` 參數控制命令是否可用。

### 重點程式碼範例（如有）
```csharp
public class MainWindowViewModel : ViewModelBase
{
    private string _userName = string.Empty;
    public string UserName
    {
        get => _userName;
        set => this.RaiseAndSetIfChanged(ref _userName, value);
    }

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }

    public MainWindowViewModel()
    {
        // 建立驗證 Observable：輸入超過 7 字元才允許提交
        IObservable<bool> isInputValid = this.WhenAnyValue(
            x => x.UserName,
            x => !string.IsNullOrWhiteSpace(x) && x.Length > 7
        );

        SubmitCommand = ReactiveCommand.Create(() =>
        {
            Debug.WriteLine("The submit command was run.");
        }, isInputValid);
    }
}
```

```xml
<StackPanel Margin="20">
    <TextBlock>User Name</TextBlock>
    <TextBox Text="{Binding UserName}"/>
    <Button Margin="0 20" Command="{Binding SubmitCommand}">Submit</Button>
</StackPanel>
```

---

## ReactiveObject 作為 ViewModel 基底類別

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/reactive-view-model

ReactiveUI 提供 `ReactiveObject` 作為 ViewModel 的基底類別，實作 `INotifyPropertyChanged` 和屬性變更的 Observable 監控。

使用方式：繼承 `ReactiveObject` 建立 ViewModelBase，再讓各 ViewModel 繼承 ViewModelBase。

### 重點程式碼範例（如有）
```csharp
public class ViewModelBase : ReactiveObject {}

public class MyViewModel : ViewModelBase
{
    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }
}
```

在 XAML 中使用雙向綁定：
```xml
<TextBox AcceptsReturn="True"
         Text="{Binding Description}"
         Watermark="Enter a description"/>
```

---

## ReactiveUI Routing（路由導覽）

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/routing

ReactiveUI Routing 由 `IScreen`（含 `RoutingState`）、多個 `IRoutableViewModel` 以及 `RoutedViewHost` XAML 控制項組成。`RoutingState` 管理 ViewModel 導覽堆疊，`RoutedViewHost` 監控路由狀態並自動顯示對應 View。

基本設置流程：
1. 安裝 `ReactiveUI.Avalonia` 套件
2. 建立繼承 `IRoutableViewModel` 的 ViewModel
3. 建立繼承 `IScreen` 的主視窗 ViewModel（含 `RoutingState Router`）
4. 在 XAML 中放置 `<rxui:RoutedViewHost Router="{Binding Router}"/>`
5. 實作 `IViewLocator` 將 ViewModel 對應到 View

### 重點程式碼範例（如有）
```csharp
// 主視窗 ViewModel
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
<rxui:RoutedViewHost Grid.Row="0" Router="{Binding Router}">
    <rxui:RoutedViewHost.DefaultContent>
        <TextBlock Text="Default content" HorizontalAlignment="Center" VerticalAlignment="Center" />
    </rxui:RoutedViewHost.DefaultContent>
</rxui:RoutedViewHost>
```

---

## ReactiveUI View Activation（視圖啟動）

**來源：** https://docs.avaloniaui.net/docs/concepts/reactiveui/view-activation

使用 `WhenActivated` 功能需搭配 `ReactiveUI.Avalonia` 套件的基底類別（如 `ReactiveWindow<TViewModel>` 或 `ReactiveUserControl<TViewModel>`）。當 View 附加到視覺樹時，`WhenActivated` 區塊內的程式碼會被呼叫；當 View 從視覺樹移除時，`CompositeDisposable` 會被釋放。

### 重點程式碼範例（如有）
```csharp
// ViewModel 實作 IActivatableViewModel
public class ViewModel : ReactiveObject, IActivatableViewModel
{
    public ViewModelActivator Activator { get; }
    public ViewModel()
    {
        Activator = new ViewModelActivator();
        this.WhenActivated((CompositeDisposable disposables) =>
        {
            /* handle activation */
            Disposable
                .Create(() => { /* handle deactivation */ })
                .DisposeWith(disposables);
        });
    }
}

// View code-behind
public class View : ReactiveWindow<ViewModel>
{
    public View()
    {
        this.WhenActivated(disposables => { /* Handle view activation */ });
        AvaloniaXamlLoader.Load(this);
    }
}
```

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter11 — MVVM Patterns and ReactiveUI

**基礎 ObservableObject**
```csharp
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
```

**PeopleViewModel（完整範例）**
```csharp
public sealed class PeopleViewModel : ObservableObject
{
    private Person? _selected;
    private readonly IPersonService _personService;

    public ObservableCollection<Person> People { get; } = new();
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public PeopleViewModel(IPersonService personService)
    {
        _personService = personService;
        AddCommand = new RelayCommand(_ => AddPerson());
        RemoveCommand = new RelayCommand(_ => RemovePerson(), _ => Selected is not null);
        LoadInitialPeople();
    }

    public Person? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) RemoveCommand.RaiseCanExecuteChanged(); }
    }
}
```

**DataTemplate 自動映射（ViewModel → View）**
```xml
<Application.DataTemplates>
  <DataTemplate DataType="{x:Type viewmodels:PeopleViewModel}">
    <views:PeopleView />
  </DataTemplate>
  <DataTemplate DataType="{x:Type viewmodels:HomeViewModel}">
    <views:HomeView />
  </DataTemplate>
</Application.DataTemplates>
```

```xml
<!-- Shell 中 -->
<ContentControl Content="{Binding CurrentViewModel}"/>
```

**導覽服務**
```csharp
public sealed class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _services;
    private object? _currentViewModel;

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public void NavigateTo<TViewModel>() where TViewModel : class
        => CurrentViewModel = _services.GetRequiredService<TViewModel>();
}
```

**ReactiveUI：ReactiveObject 和衍生狀態**
```csharp
using ReactiveUI;
using System.Reactive.Linq;

public sealed class PersonViewModelRx : ReactiveObject
{
    private string _firstName = "Ada";

    public string FirstName
    {
        get => _firstName;
        set => this.RaiseAndSetIfChanged(ref _firstName, value);
    }

    public string FullName => $"{FirstName} {LastName}";

    public PersonViewModelRx()
    {
        this.WhenAnyValue(x => x.FirstName, x => x.LastName)
            .Select(_ => Unit.Default)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FullName)));
    }
}
```

**ReactiveCommand 和非同步工作流**
```csharp
public sealed class PeopleViewModelRx : ReactiveObject
{
    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<PersonViewModelRx, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, IReadOnlyList<PersonViewModelRx>> LoadCommand { get; }

    public PeopleViewModelRx(IPersonService service)
    {
        AddCommand = ReactiveCommand.Create(() => { /* 新增人員 */ });

        var canRemove = this.WhenAnyValue(x => x.Selected).Select(s => s is not null);
        RemoveCommand = ReactiveCommand.Create<PersonViewModelRx>(p => People.Remove(p), canRemove);

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var people = await service.FetchPeopleAsync();
            // 更新 People 集合
            return People.ToList();
        });

        LoadCommand.ThrownExceptions.Subscribe(ex => { /* 處理錯誤 */ });
    }
}
```

**ReactiveUserControl 和 WhenActivated**
```csharp
public partial class PeopleViewRx : ReactiveUserControl<PeopleViewModelRx>
{
    public PeopleViewRx()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.Selected, v => v.PersonList.SelectedItem)
                .DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCommand, v => v.AddButton)
                .DisposeWith(disposables);
        });
    }
}
```

**依賴注入設定（App.axaml.cs）**
```csharp
private static IServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();
    services.AddSingleton<MainWindow>();
    services.AddSingleton<INavigationService, NavigationService>();
    services.AddTransient<PeopleViewModel>();
    services.AddSingleton<IPersonService, PersonService>();
    services.AddSingleton<IGlobalDataTemplates, AppDataTemplates>();
    return services.BuildServiceProvider();
}
```

**UndoRedo 管理器**
```csharp
public sealed class UndoRedoManager
{
    private readonly Stack<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();

    public void Do(IUndoableAction action)
    {
        action.Execute(); _undo.Push(action); _redo.Clear();
    }
    public void Undo() => Execute(_undo, _redo);
    public void Redo() => Execute(_redo, _undo);
}
```

**單元測試 ViewModel**
```csharp
[Fact]
public void RemovePerson_Disables_When_No_Selection()
{
    var service = Substitute.For<IPersonService>();
    var vm = new PeopleViewModel(service);

    vm.Selected = vm.People.First();
    Assert.True(vm.RemoveCommand.CanExecute(null));

    vm.Selected = null;
    Assert.False(vm.RemoveCommand.CanExecute(null));
}
```

---

### Chapter37 — ReactiveUI Integration & Code-First Reactive Patterns

#### 關鍵概念

**AvaloniaObject 的響應式支援**
`GetObservable(property)` 直接回傳 `IObservable<T>`，可接入完整 Rx 管線（`Throttle`、`DistinctUntilChanged`、`ObserveOn`）。

**ReactiveUI 整合**
- `WhenAnyValue` 觀察 VM 屬性
- `ObserveOn(RxApp.MainThreadScheduler)` 確保 UI 更新在正確執行緒
- DynamicData `SourceList<T>` + `.Bind(out var items)` → 直接設定 `listBox.Items`

**Classes/PseudoClasses 管理**
- `panel.Classes.Add("card")` / `panel.PseudoClasses.Set(":active", true)`
- `Toggle` extension 配合 `WhenAnyValue` 響應式切換

**Behaviours（`Avalonia.Interactivity`）**
`Interaction.SetBehaviors(control, new BehaviorCollection { new MyBehavior() })` 封裝複雜互動邏輯。

**Transitions 動態管理**
`panel.Transitions = new Transitions { new DoubleTransition { Property = ... } }` 可在 runtime 依主題/功能替換整個 Transitions 集合。

**生命週期管理**
`CompositeDisposable` 收集訂閱，在 `OnDetachedFromVisualTree` 時 Dispose。

#### 代碼範例

```csharp
// Avalonia Property → Rx 管線
var textBox = new TextBox();
textBox.GetObservable(TextBox.TextProperty)
    .Throttle(TimeSpan.FromMilliseconds(250), RxApp.MainThreadScheduler)
    .DistinctUntilChanged()
    .Subscribe(text => _search.Execute(text));
```

```csharp
// ReactiveUI WhenAnyValue → UI 更新
var vm = new DashboardViewModel();
vm.WhenAnyValue(x => x.IsLoading)
  .ObserveOn(RxApp.MainThreadScheduler)
  .Subscribe(isLoading => spinner.IsVisible = isLoading);
```

```csharp
// DynamicData SourceList → ListBox
var source = new SourceList<ItemViewModel>();
var bindingList = source.Connect()
    .Filter(item => item.IsEnabled)
    .Sort(SortExpressionComparer<ItemViewModel>.Descending(x => x.CreatedAt))
    .ObserveOn(RxApp.MainThreadScheduler)
    .Bind(out var items)
    .Subscribe();

listBox.Items = items;
```

```csharp
// PseudoClass 響應式切換
vm.WhenAnyValue(x => x.IsSelected)
  .Subscribe(selected => panel.Classes.Toggle("selected", selected));

// Toggle Extension
public static class ClassExtensions
{
    public static void Toggle(this Classes classes, string name, bool add)
    {
        if (add) classes.Add(name);
        else classes.Remove(name);
    }
}
```

```csharp
// Transition 動態設定
panel.Transitions = new Transitions
{
    new DoubleTransition
    {
        Property = Border.OpacityProperty,
        Duration = TimeSpan.FromMilliseconds(200),
        Easing = new CubicEaseOut()
    }
};

vm.WhenAnyValue(x => x.ShowDetails)
  .Subscribe(show => panel.Opacity = show ? 1 : 0);
```

```csharp
// 可重用 ReactiveControl Helper
public static class ReactiveControlHelpers
{
    public static IDisposable BindState<TViewModel>(
        this TViewModel vm,
        Control control,
        Expression<Func<TViewModel, bool>> property,
        string pseudoClass)
    {
        return vm.WhenAnyValue(property)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(value => control.PseudoClasses.Set(pseudoClass, value));
    }
}

// 使用：
_disposables.Add(vm.BindState(this, x => x.IsActive, ":active"));
```

```csharp
// 啟用 DevTools（偵錯時）
if (Debugger.IsAttached)
    this.AttachDevTools();
```

```csharp
// 開啟渲染 Diagnostics Overlays
if (Debugger.IsAttached)
{
    RenderOptions.ProcessRenderOperations = true;
    RendererDiagnostics.DebugOverlays = RendererDebugOverlays.Fps | RendererDebugOverlays.Layout;
}
```

---