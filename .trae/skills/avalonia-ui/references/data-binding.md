# Avalonia 資料綁定知識庫

此檔案包含 Avalonia 資料綁定相關文件摘要。

---

## 資料綁定概觀 (Data Binding Overview)

**來源：** https://docs.avaloniaui.net/docs/basics/data/data-binding/

Avalonia 的資料綁定系統可以在應用程式物件與 UI 控制項之間移動資料。控制項是**綁定目標（binding target）**，應用程式物件是**資料來源（data source）**。

綁定可以是雙向的（TwoWay）或單向的（OneWay）。XAML 語法基本格式：

```xml
<SomeControl Attribute="{Binding PropertyName}" />
<!-- 雙向範例 -->
<TextBox Text="{Binding FirstName}" />
<!-- 單向範例 -->
<TextBlock Text="{Binding StatusMessage}" />
```

資料綁定通常與 MVVM 模式搭配使用。

---

## 編譯式綁定 (Compiled Bindings)

**來源：** https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings

相較於使用反射（Reflection）的一般綁定，編譯式綁定有以下優點：
- **編譯時期錯誤檢查**：找不到屬性時會報編譯錯誤
- **效能較好**：反射較慢，編譯式綁定效能更佳

啟用方式：
1. **全域啟用**：在 `.csproj` 加入 `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`
2. **單一控制項**：設定 `x:DataType` 和 `x:CompileBindings="True"`

### 重點程式碼範例（如有）
```xml
<!-- 啟用編譯式綁定 -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:MyApp.ViewModels"
             x:DataType="vm:MyViewModel"
             x:CompileBindings="True">
    <StackPanel>
        <TextBox Text="{Binding LastName}" />
        <TextBox Text="{Binding GivenName}" />
        <TextBox Text="{Binding MailAddress, DataType={x:Type vm:MyViewModel}}" />
        <Button Content="Send an E-Mail"
                Command="{Binding SendEmailCommand}" />
    </StackPanel>
</UserControl>
```

```xml
<!-- 使用 CompiledBinding Markup（不需全域啟用） -->
<TextBox Text="{CompiledBinding LastName}" />

<!-- 使用 ReflectionBinding Markup（在編譯綁定啟用時回退） -->
<Button Command="{ReflectionBinding SendEmailCommand}" />
```

**DataContext 類型推斷**（11.3.0 起支援）：
```xml
<Window x:Name="MyWindow" x:DataType="vm:TestDataContext">
    <TextBlock Text="{Binding #MyWindow.DataContext.StringProperty}" />
    <TextBlock Text="{Binding $parent[Window].DataContext.StringProperty}" />
</Window>
```

---

## 資料綁定語法 (Data Binding Syntax)

**來源：** https://docs.avaloniaui.net/docs/basics/data/data-binding/data-binding-syntax

`Binding` MarkupExtension 的完整參數說明：

| 參數 | 說明 |
|------|------|
| `Path` | 要綁定的來源屬性名稱 |
| `Mode` | 綁定同步方向（OneWay/TwoWay/OneTime/OneWayToSource/Default） |
| `Source` | 包含 Path 屬性的物件（預設是 DataContext） |
| `ElementName` | 使用命名控制項作為來源 |
| `RelativeSource` | 使用視覺樹中相對控制項作為來源 |
| `StringFormat` | 將屬性值格式化為字串的模式 |
| `Converter` | IValueConverter 轉換器 |
| `ConverterParameter` | 提供給轉換器的參數 |
| `FallbackValue` | 綁定無法建立時的預設值 |
| `TargetNullValue` | 來源屬性為 null 時的值 |
| `UpdateSourceTrigger` | 觸發更新來源的條件 |

### 重點程式碼範例（如有）
```xml
<!-- Path 可省略 Path= 前綴 -->
<TextBlock Text="{Binding Name}"/>
<TextBlock Text="{Binding Path=Name}"/>

<!-- 子屬性鏈 -->
<TextBlock Text="{Binding Student.Name}"/>

<!-- 索引器 -->
<TextBlock Text="{Binding Students[0].Name}"/>

<!-- 空路徑（綁定到 DataContext 本身） -->
<TextBlock Text="{Binding}" />

<!-- 指定 Mode -->
<TextBlock Text="{Binding Name, Mode=OneTime}" />

<!-- 使用 ElementName 綁定另一個控制項 -->
<TextBox Name="input" />
<TextBlock Text="{Binding Text, ElementName=input}" />
<TextBlock Text="{Binding #input.Text}" />

<!-- RelativeSource -->
<TextBlock Text="{Binding $parent[Window].Title}" />

<!-- StringFormat -->
<TextBlock Text="{Binding FloatProperty, StringFormat={}{0:0.0}}" />
```

---

## 資料上下文 (DataContext)

**來源：** https://docs.avaloniaui.net/docs/basics/data/data-binding/data-context

每個 Avalonia 控制項都有 `DataContext` 屬性。綁定時，Avalonia 從控制項開始往上搜尋邏輯控制樹，找到可用的資料上下文。

### 重點程式碼範例（如有）
```csharp
// 在 App.axaml.cs 中設定 MainWindow 的 DataContext
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.MainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
        };
    }
    base.OnFrameworkInitializationCompleted();
}
```

```xml
<!-- MainWindow.axaml 中使用 Design.DataContext（設計時預覽） -->
<Window ...>
    <Design.DataContext>
        <vm:MainWindowViewModel/>
    </Design.DataContext>
    <TextBlock Text="{Binding Greeting}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
</Window>
```

---

## 資料範本 (Data Templates)

**來源：** https://docs.avaloniaui.net/docs/basics/data/data-templates

資料範本（Data Template）是一個可重用的定義，指定如何呈現特定類型的資料。通常與 `ListBox`、`ItemsControl` 等列表控制項搭配，用來渲染集合中的每個項目。

### 重點程式碼範例（如有）
```xml
<!-- 套用 DataTemplate 到 ListBox -->
<ListBox ItemsSource="{Binding Items}">
  <ListBox.ItemTemplate>
    <DataTemplate>
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="{Binding Name}" />
            <Image Source="{Binding ImageSource}" />
        </StackPanel>
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

---

## DataTemplates 概念概覽

**來源：** https://docs.avaloniaui.net/docs/concepts/templates/

資料模板（Data Template）允許 Avalonia 控制項在其內容區域顯示非控制項的物件。當視窗或控制項的內容區域放入自訂物件時，若無資料模板，只會顯示完整限定類別名稱（如 `MySample.Student`）。

---

## ContentTemplate - 直接設定內容模板

**來源：** https://docs.avaloniaui.net/docs/concepts/templates/content-template

使用資料模板的兩步驟：
1. 定義資料模板（`DataTemplate` 標籤）
2. 選擇要套用的資料模板（設定到控制項的 `ContentTemplate` 屬性）

### 重點程式碼範例（如有）
```xml
<Window>
  <Window.ContentTemplate>
    <DataTemplate DataType="{x:Type local:Student}">
      <StackPanel>
        <Grid ColumnDefinitions="Auto,Auto" RowDefinitions="Auto,Auto">
          <TextBlock Grid.Row="0" Grid.Column="0">First Name:</TextBlock>
          <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding FirstName}"/>
          <TextBlock Grid.Row="1" Grid.Column="0">Last Name:</TextBlock>
          <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding LastName}"/>
        </Grid>
      </StackPanel>
    </DataTemplate>
  </Window.ContentTemplate>
  
  <local:Student FirstName="Jane" LastName="Deer"/>
</Window>
```

---

## 以程式碼建立 DataTemplate

**來源：** https://docs.avaloniaui.net/docs/concepts/templates/creating-data-templates-in-code

使用 `FuncDataTemplate<T>` 類別在程式碼中建立資料模板，支援 `IDataTemplate` 介面。

### 重點程式碼範例（如有）
```csharp
// 程式碼建立 DataTemplate
var template = new FuncDataTemplate<Student>((value, namescope) =>
    new TextBlock
    {
        [!TextBlock.TextProperty] = new Binding("FirstName"),
    });

// 等同的 XAML
// <DataTemplate DataType="{x:Type local:Student}">
//     <TextBlock Text="{Binding FirstName}"/>
// </DataTemplate>
```

---

## DataTemplates - 基本概念

**來源：** https://docs.avaloniaui.net/docs/concepts/templates/data-templates

當視窗的內容區域放入自訂類別物件時，若沒有資料模板，Avalonia 只會顯示完整類別名稱字串。需要透過 DataTemplate 來定義如何顯示該物件。

資料模板的工作流程：
1. 定義自訂類別（如 `Student { FirstName, LastName }`）
2. 在 XAML 中實例化物件作為視窗內容
3. 提供 DataTemplate 說明如何顯示該物件

---

## DataTemplates 集合

**來源：** https://docs.avaloniaui.net/docs/concepts/templates/data-templates-collection

所有 Avalonia 控制項都有 `DataTemplates` 集合，可放入多個資料模板定義。根據顯示物件的類別類型自動選擇匹配的模板。

匹配規則：物件類別與模板 `DataType` 屬性中指定的完整限定類別名稱相同時匹配。

### 重點程式碼範例（如有）
```xml
<Window.DataTemplates>
    <DataTemplate DataType="{x:Type local:Student}">
      <Grid ColumnDefinitions="Auto,Auto" RowDefinitions="Auto,Auto">
        <TextBlock Grid.Row="0" Grid.Column="0">First Name:</TextBlock>
        <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding FirstName}"/>
        <TextBlock Grid.Row="1" Grid.Column="0">Last Name:</TextBlock>
        <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding LastName}"/>
      </Grid>
    </DataTemplate>
</Window.DataTemplates>

<local:Student FirstName="Jane" LastName="Deer"/>
```

---

## 實作 IDataTemplate

**來源：** https://docs.avaloniaui.net/docs/concepts/templates/implement-idatatemplate

實作 `IDataTemplate` 介面可更精細地控制資料模板，需實作 `Build(object)` 和 `Match(object)` 兩個方法。

### 重點程式碼範例（如有）
```csharp
public class MyDataTemplate : IDataTemplate
{
    public Control Build(object param)
    {
        return new TextBlock() { Text = (string)param };
    }
    
    public bool Match(object data)
    {
        return data is string;
    }
}
```

---

## 重用 DataTemplates

**來源：** https://docs.avaloniaui.net/docs/concepts/templates/reusing-data-templates

Avalonia 搜索資料模板的層級：
1. 控制項自身的 `DataTemplates` 集合
2. 父控制項（遞迴查找）
3. 視窗的 `DataTemplates` 集合
4. **應用程式的 `DataTemplates` 集合**（`app.axaml`）

若要在整個應用程式重用模板，定義到 `Application.DataTemplates`：

### 重點程式碼範例（如有）
```xml
<!-- app.axaml -->
<Application.DataTemplates>
    <DataTemplate DataType="{x:Type vm:Teacher}">
        <Grid ColumnDefinitions="Auto,Auto" RowDefinitions="Auto,Auto">
            <TextBlock Grid.Row="0" Grid.Column="0">Name:</TextBlock>
            <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding Name}"/>
        </Grid>
    </DataTemplate>
</Application.DataTemplates>
```

**警告**：每個 DataTemplate 都必須指定 `DataType`，否則找不到匹配模板時不會顯示任何內容。

---

## Data Binding 指南索引

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/

各項資料綁定技術的指南集合，包含：類別綁定、程式碼綁定、控制項綁定、指令綁定等。

---

## 根據 Boolean 綁定 CSS 樣式類別

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/binding-classes

使用 `Classes.className="{Binding ...}"` 語法根據布林值動態套用樣式類別：

### 重點程式碼範例（如有）
```xml
<TextBlock
    Classes.class1="{Binding IsClass1}"
    Classes.class2="{Binding !IsClass1}"
    Text="{Binding Title}"/>
```

---

## 從程式碼進行資料綁定

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/binding-from-code

Avalonia 底層使用 Reactive Extensions 的 `IObservable`。可用 `GetObservable()` 訂閱屬性變化，用 `Bind()` 綁定可觀測物件：

### 重點程式碼範例（如有）
```csharp
// 訂閱屬性變化
var text = textBlock.GetObservable(TextBlock.TextProperty);
text.Subscribe(value => Console.WriteLine(value));

// 綁定到 Subject
var source = new Subject<string>();
var subscription = textBlock.Bind(TextBlock.TextProperty, source);
source.OnNext("hello");
subscription.Dispose(); // 終止綁定

// 在物件初始化器中使用索引器
var textBlock = new TextBlock {
    [!TextBlock.TextProperty] = source.ToBinding()
};
```

---

## 綁定到其他控制項

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/binding-to-controls

直接綁定到其他控制項（不透過 DataContext）：

### 重點程式碼範例（如有）
```xml
<!-- 綁定到具名控制項 -->
<TextBlock Text="{Binding #other.Text}"/>

<!-- 綁定到父控制項 -->
<TextBlock Text="{Binding $parent.Tag}"/>

<!-- 綁定到第二層父控制項 -->
<TextBlock Text="{Binding $parent[1].Tag}"/>

<!-- 綁定到指定型別的祖先 -->
<TextBlock Text="{Binding $parent[Border].Tag}"/>
```

---

## 使用 ReactiveUI 控制指令的 CanExecute

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-can-execute

用 `WhenAnyValue` 產生 observable 作為 `ReactiveCommand.Create` 的第二參數：

### 重點程式碼範例（如有）
```csharp
var isValidObservable = this.WhenAnyValue(
    x => x.Message,
    x => !string.IsNullOrWhiteSpace(x));

ExampleCommand = ReactiveCommand.Create(PerformAction, isValidObservable);
```

---

## 綁定圖片檔案

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-image-files

從資源或網路載入圖片，非同步來源使用 `^` 運算子：

### 重點程式碼範例（如有）
```csharp
// 從資源載入
public Bitmap? ImageFromBinding { get; } =
    ImageHelper.LoadFromResource(new Uri("avares://App/Assets/image.jpg"));

// 從網路非同步載入
public Task<Bitmap?> ImageFromWebsite { get; } =
    ImageHelper.LoadFromWeb(new Uri("https://example.com/image.jpg"));
```
```xml
<Image Source="{Binding ImageFromBinding}" MaxWidth="300" />
<Image Source="{Binding ImageFromWebsite^}" MaxWidth="300" />
```

---

## MultiBinding：同時綁定多個屬性

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-multiple-properties

使用 `MultiBinding` 搭配 `IMultiValueConverter` 將多個屬性合成一個目標值（僅支援 OneWay/OneTime）：

### 重點程式碼範例（如有）
```xml
<TextBlock.Foreground>
    <MultiBinding Converter="{StaticResource RgbToBrushMultiConverter}">
        <Binding Path="Value" ElementName="red" />
        <Binding Path="Value" ElementName="green" />
        <Binding Path="Value" ElementName="blue" />
    </MultiBinding>
</TextBlock.Foreground>
```
```csharp
public sealed class RgbToBrushMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 將 r, g, b 轉換為 ImmutableSolidColorBrush
    }
}
```

---

## 動態綁定 TabControl 的頁籤

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-tabs

將 `TabControl.ItemsSource` 綁定到 `ObservableCollection`，並用 `ItemTemplate`/`ContentTemplate` 定義頁籤標題與內容：

### 重點程式碼範例（如有）
```csharp
public ObservableCollection<ItemViewModel> Items { get; set; } = new() {
    new ItemViewModel("One", "Some content on first tab"),
    new ItemViewModel("Two", "Some content on second tab"),
};
```

---

## 綁定集合（ObservableCollection）

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-to-a-collection

將 `ObservableCollection` 綁定到 `ListBox`，並用 `DataTemplate` 定義項目呈現方式：

### 重點程式碼範例（如有）
```xml
<ListBox ItemsSource="{Binding People}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding Name}" Margin="0,0,10,0"/>
                <TextBlock Text="{Binding Age}"/>
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

---

## 使用 ReactiveUI 綁定指令

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-to-a-command-with-reactiveui

用 `ReactiveCommand.Create()` 建立指令，透過 `Command` 屬性綁定到控制項：

### 重點程式碼範例（如有）
```csharp
public ReactiveCommand<Unit, Unit> ExampleCommand { get; }

public MainWindowViewModel()
{
    ExampleCommand = ReactiveCommand.Create(PerformAction);
}

private void PerformAction() => Debug.WriteLine("Action called.");
```
```xml
<Button Command="{Binding ExampleCommand}">Run</Button>
<Button Command="{Binding ExampleCommand}" CommandParameter="From button">Run</Button>
```

---

## 不使用 ReactiveUI 綁定指令

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-to-a-command-without-reactiveui

直接將 ViewModel 方法名稱綁定到 `Command`，並可用 `Can[MethodName]` 命名慣例控制 CanExecute：

### 重點程式碼範例（如有）
```csharp
public void PerformAction(object msg) => Debug.WriteLine($"Action: {msg}");

public bool CanPerformAction(object msg)
    => msg != null && !string.IsNullOrWhiteSpace(msg.ToString());
```
```xml
<Button Command="{Binding PerformAction}" CommandParameter="{Binding #message.Text}">Run</Button>
```

---

## 綁定到 Task 結果

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-to-a-task-result

使用 `^` 串流綁定運算子，並用 `FallbackValue` 設定載入中的顯示值：

### 重點程式碼範例（如有）
```xml
<TextBlock Text="{Binding MyAsyncText^, FallbackValue='Wait a second'}" />
```
```csharp
public Task<string> MyAsyncText => GetTextAsync();
private async Task<string> GetTextAsync()
{
    await Task.Delay(1000);
    return "Hello from async operation";
}
```

---

## 綁定到 Observable

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-to-an-observable

使用 `^` 運算子訂閱 `IObservable<T>` 的值：

### 重點程式碼範例（如有）
```xml
<!-- 若 Name 是 IObservable<string>，綁定每個字串的長度 -->
<TextBlock Text="{Binding Name^.Length}"/>
```

---

## 建立自訂資料綁定轉換器

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/how-to-create-a-custom-data-binding-converter

實作 `IValueConverter` 介面，在 `Convert` 方法中用 `targetType.IsAssignableTo()` 判斷目標型別：

### 重點程式碼範例（如有）
```csharp
public class TextCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string sourceText && parameter is string targetCase)
            return targetCase == "upper" ? sourceText.ToUpper() : sourceText.ToLower();
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

也可用 `FuncValueConverter` 簡化：
```csharp
public static FuncValueConverter<decimal?, string> MyConverter { get; } =
    new FuncValueConverter<decimal?, string>(num => $"Your number is: '{num}'");
```

---

## INotifyPropertyChanged 與 MVVM

**來源：** https://docs.avaloniaui.net/docs/guides/data-binding/inotifypropertychanged

`INotifyPropertyChanged` 在屬性變更時通知 UI 更新。可手動實作，或使用 CommunityToolkit.Mvvm 的 `[ObservableProperty]` attribute 自動產生：

### 重點程式碼範例（如有）
```csharp
// 手動實作
public class MyViewModel : INotifyPropertyChanged
{
    private string _name;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

// 使用 CommunityToolkit.Mvvm
public partial class MyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;
}
```

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter08 — Data Binding Deep Dive

**繫結引擎核心**
- `DataContext`：沿邏輯樹繼承，大多數繫結相對於當前元素的 DataContext 解析
- `Binding`：描述路徑、模式、轉換器、後備值
- `BindingExpression`：執行期為每個繫結目標建立的求值物件
- `BindingOperations`：靜態輔助工具，用於程式化安裝/移除繫結

**INotifyPropertyChanged ViewModel**
```csharp
public class PersonViewModel : INotifyPropertyChanged
{
    private string _firstName = "Ada";
    public string FirstName
    {
        get => _firstName;
        set { if (_firstName != value) { _firstName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); } }
    }
    public string FullName => $"{FirstName} {LastName}".Trim();
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

**繫結模式（XAML）**
```xml
<StackPanel>
  <!-- TwoWay: UI ↔ ViewModel -->
  <TextBox Text="{Binding Person.FirstName, Mode=TwoWay}"/>
  <!-- OneWay: ViewModel → UI -->
  <TextBlock Text="{Binding Person.FullName, Mode=OneWay}" FontSize="20"/>
  <!-- OneTime: 載入時一次 -->
  <TextBlock Text="{Binding CreatedAt, Mode=OneTime, StringFormat='Created on {0:d}'}"/>
</StackPanel>
```

**ElementName 和 RelativeSource**
```xml
<!-- ElementName: 繫結至另一個具名元素 -->
<Slider x:Name="VolumeSlider" Minimum="0" Maximum="100" Value="50"/>
<ProgressBar Value="{Binding #VolumeSlider.Value}"/>

<!-- RelativeSource: 走邏輯樹找祖先 -->
<TextBlock Text="{Binding DataContext.Title, RelativeSource={RelativeSource AncestorType=Window}}"/>
```

**編譯繫結（CompiledBinding）**
```xml
<StackPanel DataContext="{Binding Person}" x:DataType="vm:PersonViewModel">
  <TextBlock Text="{CompiledBinding FullName}"/>
  <TextBox Text="{CompiledBinding FirstName}"/>
</StackPanel>
```

**MultiBinding**
```csharp
public sealed class NameAgeFormatter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = values[0] as string ?? "";
        var age = values[1] as int? ?? 0;
        return $"{name} ({age})";
    }
}
```

```xml
<TextBlock>
  <TextBlock.Text>
    <MultiBinding Converter="{StaticResource NameAgeFormatter}">
      <Binding Path="Person.FullName"/>
      <Binding Path="Person.Age"/>
    </MultiBinding>
  </TextBlock.Text>
</TextBlock>
```

**PriorityBinding**
```xml
<TextBlock>
  <TextBlock.Text>
    <PriorityBinding>
      <Binding Path="OverrideTitle"/>
      <Binding Path="Person.FullName"/>
      <Binding Path="'Unknown user'"/>
    </PriorityBinding>
  </TextBlock.Text>
</TextBlock>
```

**SelectionModel（虛擬化友好的多選）**
```csharp
public SelectionModel<PersonViewModel> PeopleSelection { get; } =
    new() { SelectionMode = SelectionMode.Multiple };
```

```xml
<ListBox Items="{Binding People}" Selection="{Binding PeopleSelection}"/>
```

**INotifyDataErrorInfo 驗證**
```csharp
public class ValidatingPersonViewModel : PersonViewModel, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new();
    public bool HasErrors => _errors.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName)
        => propertyName is not null && _errors.TryGetValue(propertyName, out var errors) ? errors : Array.Empty<string>();

    private void Validate(string? propertyName)
    {
        if (propertyName is nameof(Age))
        {
            if (Age < 0 || Age > 120)
                AddError(propertyName, "Age must be between 0 and 120");
            else
                ClearErrors(propertyName);
        }
    }
}
```

**程式化繫結操作**
```csharp
// 設定繫結
var binding = new Binding { Path = "Person.FullName", Mode = BindingMode.OneWay };
BindingOperations.SetBinding(nameTextBlock, TextBlock.TextProperty, binding);

// 清除繫結
BindingOperations.ClearBinding(nameTextBlock, TextBlock.TextProperty);

// 觀察 AvaloniaProperty
var subscription = AvaloniaPropertyObservable.Observe(this, TextBox.TextProperty)
    .Select(v => v as string ?? string.Empty)
    .Subscribe(text => ViewModel.TextLength = text.Length);
```

**繫結診斷**
```csharp
using Avalonia.Diagnostics;

BindingDiagnostics.Enable(
    log => Console.WriteLine(log.Message),
    new BindingDiagnosticOptions { Level = BindingDiagnosticLogLevel.Warning });
```

---

### Chapter09 — Commands, Input, and Events

**路由事件（RoutedEvent）**
```csharp
// 自訂路由事件
public static readonly RoutedEvent<RoutedEventArgs> DragStartedEvent =
    RoutedEvent.Register<Control, RoutedEventArgs>(
        nameof(DragStarted), RoutingStrategies.Bubble);

public event EventHandler<RoutedEventArgs> DragStarted
{
    add => AddHandler(DragStartedEvent, value);
    remove => RemoveHandler(DragStartedEvent, value);
}
```

**ViewModel 指令（Commands）**
```csharp
public sealed class MainWindowViewModel : ViewModelBase
{
    public RelayCommand SaveCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public MainWindowViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save(), _ => HasChanges);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    private async Task RefreshAsync()
    {
        try { IsBusy = true; await Task.Delay(1500); }
        finally { IsBusy = false; }
    }
}
```

**XAML 指令繫結**
```xml
<StackPanel Spacing="12">
  <TextBox Text="{Binding SelectedName, Mode=TwoWay}"/>
  <StackPanel Orientation="Horizontal" Spacing="12">
    <Button Content="Save" Command="{Binding SaveCommand}"/>
    <Button Content="Refresh" Command="{Binding RefreshCommand}"/>
    <Button Content="Delete" Command="{Binding DeleteCommand}"
            CommandParameter="{Binding SelectedName}"/>
  </StackPanel>
</StackPanel>
```

**鍵盤快捷鍵（KeyBinding）**
```xml
<Window.InputBindings>
  <KeyBinding Gesture="Ctrl+S" Command="{Binding SaveCommand}"/>
  <KeyBinding Gesture="Ctrl+R" Command="{Binding RefreshCommand}"/>
</Window.InputBindings>
```

**HotKeyManager（全域快捷鍵）**
```xml
<Button Content="Save"
        Command="{Binding SaveCommand}"
        controls:HotKeyManager.HotKey="Ctrl+Shift+S"/>
```

```csharp
HotKeyManager.SetHotKey(button, new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift));
```

**手勢識別器**
```xml
<Border Background="#1e293b" Padding="16">
  <Border.GestureRecognizers>
    <TapGestureRecognizer NumberOfTapsRequired="2" Command="{Binding DoubleTapCommand}"/>
    <ScrollGestureRecognizer CanHorizontallyScroll="True"/>
  </Border.GestureRecognizers>
</Border>
```

**指標捕捉（拖曳實作）**
```csharp
private bool _isDragging;
private Point _dragStart;

private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
{
    _isDragging = true;
    _dragStart = e.GetPosition((Control)sender!);
    e.Pointer.Capture((IInputElement)sender!);
}

private void Card_PointerMoved(object? sender, PointerEventArgs e)
{
    if (_isDragging && sender is Control control)
    {
        var offset = e.GetPosition(control) - _dragStart;
        Canvas.SetLeft(control, offset.X);
        Canvas.SetTop(control, offset.Y);
    }
}

private void Card_PointerReleased(object? sender, PointerReleasedEventArgs e)
{
    _isDragging = false;
    e.Pointer.Capture(null);
}
```

**非同步指令模式**
```csharp
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _isExecuting;

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

---

### Chapter35（data-binding 補充）— Code-First Binding Patterns

> *同見 styling.md — Chapter35；本節著重 MVVM binding infrastructure 的結構化模式。*

#### 關鍵概念

**Validation 回饋**
`DataValidationErrors.HasErrorsProperty` + `GetObservable` 可驅動 `:invalid` 偽類；`ValidatesOnDataErrors`/`ValidatesOnExceptions` 啟用 `BindingNotification` 傳遞錯誤。

**Binding Factory 封裝**
將 view-model 的所有 binding 集中在靜態類別中，消除分散的 magic strings。

**Expression Tree 路徑生成**
用 Expression trees 建立 binding 路徑，確保重構時路徑一起更新。

#### 代碼範例

```csharp
// Validation 觀察
var amountBinding = new Binding("Amount")
{
    Mode = BindingMode.TwoWay,
    ValidatesOnDataErrors = true,
    ValidatesOnExceptions = true
};
amountTextBox.Bind(TextBox.TextProperty, amountBinding);

amountTextBox.GetObservable(DataValidationErrors.HasErrorsProperty)
    .Subscribe(hasErrors => amountTextBox.Classes.Set(":invalid", hasErrors));
```

```csharp
// Binding Factory 模式
public static class DashboardBindings
{
    public static Binding TotalSales => new(nameof(DashboardViewModel.TotalSales))
        { Mode = BindingMode.OneWay };
    public static Binding RefreshCommand =>
        new(nameof(DashboardViewModel.RefreshCommand));
}

// 使用：
salesText.Bind(TextBlock.TextProperty, DashboardBindings.TotalSales);
refreshButton.Bind(Button.CommandProperty, DashboardBindings.RefreshCommand);
```

```csharp
// Expression Tree Binding Helper
public static class BindingFactory
{
    public static Binding Create<TViewModel, TValue>(
        Expression<Func<TViewModel, TValue>> expression,
        BindingMode mode = BindingMode.Default)
    {
        var path = ExpressionHelper.GetMemberPath(expression);
        return new Binding(path) { Mode = mode };
    }
}
```

---