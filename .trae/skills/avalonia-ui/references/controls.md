# Avalonia 控制項知識庫

此檔案包含 Avalonia 控制項相關文件摘要。

---

## 自訂控制項類型選擇 (Choosing a Custom Control Type)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/controls/creating-controls/choosing-a-custom-control-type

Avalonia 提供三種自訂控制項方式：

1. **UserControl**：組合現有控制項，用 XAML 定義版面。適合應用程式內特定的「視圖」（如使用者詳細資料頁面），不適合通用 UI 元素。
2. **Templated (Lookless) Controls**：行為與外觀分離，在程式碼定義行為，在 XAML 定義外觀模板。適合需要多主題/樣式的通用 UI 元素，多數內建控制項都是此類型。
3. **Custom-drawn Controls**：最高自訂程度，覆寫 `Render` 方法使用 `DrawingContext` API 繪製。適合主要是非互動式圖形元素。

---

## 自訂控制項主題 (Control Themes for Custom Controls)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/controls/creating-controls/control-themes

Lookless 控制項在沒有控制項主題（ControlTheme）時不會有視覺呈現。可為所有類型的控制項定義控制項主題。詳見樣式章節的控制項主題介紹。

---

## 定義事件 (Defining Events)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/controls/creating-controls/defining-events

Avalonia 使用路由事件（Routed Events）機制，事件可以在控制樹中向上或向下傳遞。

### 重點程式碼範例（如有）
```csharp
public class MyCustomSlider : Control
{
    public static readonly RoutedEvent<RoutedEventArgs> ValueChangedEvent =
        RoutedEvent.Register<MyCustomSlider, RoutedEventArgs>(
            nameof(ValueChanged), RoutingStrategies.Direct);

    public event EventHandler<RoutedEventArgs> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    protected virtual void OnValueChanged()
    {
        RoutedEventArgs args = new RoutedEventArgs(ValueChangedEvent);
        RaiseEvent(args);
    }
}
```

---

## 定義屬性 (Defining Properties)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/controls/creating-controls/defining-properties

使用 `AvaloniaProperty` 系統定義樣式屬性（StyledProperty），支援資料綁定與樣式系統。

### 重點程式碼範例（如有）
```csharp
public class MyCustomButton : Button
{
    public static readonly StyledProperty<int> RepeatCountProperty =
        AvaloniaProperty.Register<MyCustomButton, int>(
            nameof(RepeatCount), defaultValue: 1);

    public int RepeatCount
    {
        get => GetValue(RepeatCountProperty);
        set => SetValue(RepeatCountProperty, value);
    }
}
```

---


**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/controls/

Avalonia 控制項分類：
- **繪製控制項（Drawn Controls）**：自行負責渲染的控制項，如 `Border`、`TextBlock`、`Image`。無法重新模板化，修改需衍生新類別。
- **版面配置控制項（Layout Controls）**：沒有外觀，只負責子元素位置與大小，如 `Grid`、`StackPanel`。
- **使用者控制項（User Controls）**：應用程式自訂，框架本身不提供。
- **模板化控制項（Templated Controls）**：外觀定義在 XAML 控制項模板中，可完全自訂。分為「完全可自訂」（如 Button）和「部分可自訂」（如 DataGrid）。

---

## 內建控制項列表 (Built-in Controls)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/controls/builtin-controls

常用內建控制項分類：

**版面配置控制項**：Border、Canvas、DockPanel、Expander、Grid、GridSplitter、Panel、RelativePanel、ScrollViewer、SplitView、StackPanel、TabControl、UniformGrid、WrapPanel

**按鈕類**：Button、RepeatButton、RadioButton、ToggleButton、ButtonSpinner、SplitButton、ToggleSplitButton

**資料重複控制項**：DataGrid、ItemsControl、ItemsRepeater、ListBox、ComboBox

**文字顯示與編輯**：AutoCompleteBox、TextBlock（唯讀）、TextBox、SelectableTextBlock、MaskedTextBox

**值選擇**：CheckBox（布林）、Slider（浮點）、Calendar（日期）、CalendarDatePicker、ColorPicker（顏色）、DatePicker、TimePicker

**圖片顯示**：Image（點陣圖或向量圖）、PathIcon（向量圖）

**選單與彈出**：Menu、Flyouts、ToolTip

---

## Label 標籤控制項

**來源：** https://docs.avaloniaui.net/docs/reference/controls/label

`Label` 是一個不接收焦點的文字標籤控制項，但可以將焦點轉移到指定的目標控制項。當使用者點擊標籤或按下 Alt + 存取鍵組合時，焦點會轉移到目標控制項。

---

## Layout Controls 佈局控制項索引

**來源：** https://docs.avaloniaui.net/docs/reference/controls/layout-controls

佈局控制項讓您透過 UI 組合以多種方式排列介面。包含：Border、Canvas、DockPanel、Expander、Grid、GridSplitter、Panel、RelativePanel、ScrollBar、ScrollViewer、SplitView、StackPanel、TabControl、UniformGrid、WrapPanel。

---

## LayoutTransformControl 版面轉換控制項

**來源：** https://docs.avaloniaui.net/docs/reference/controls/layouttransformcontrol

`LayoutTransformControl` 可以在視圖中動態完整地轉換 UI 版面配置。

---

## ListBox 清單方塊

**來源：** https://docs.avaloniaui.net/docs/reference/controls/listbox

`ListBox` 以多行顯示來自集合來源的項目，並支援單選或多選。清單高度會自動展開以容納所有項目，除非設定了固定高度或由容器控制項（如 DockPanel）限制。超出範圍時內建 ScrollViewer 會自動顯示捲軸。

**選擇模式：**
- `Single`：單選（預設）
- `Multiple`：多選
- `Toggle`：點擊切換選取
- `AlwaysSelected`：永遠有一個項目被選取

### 重點程式碼範例（如有）
```xml
<StackPanel Margin="20">
  <TextBlock Margin="0 5">Choose an animal:</TextBlock>
  <ListBox x:Name="animals"/>
</StackPanel>
```
```csharp
animals.ItemsSource = new string[]
    {"cat", "camel", "cow", "chameleon", "mouse", "lion", "zebra" }
.OrderBy(x => x);
```

帶有 DataTemplate 的範例：
```xml
<ListBox x:Name="animals">
  <ListBox.ItemTemplate>
    <DataTemplate>
      <Border BorderBrush="Blue" BorderThickness="1" CornerRadius="4" Padding="4">
        <TextBlock Text="{Binding}"/>
      </Border>
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

---

## MaskedTextBox 遮罩文字方塊

**來源：** https://docs.avaloniaui.net/docs/reference/controls/maskedtextbox

`MaskedTextBox` 提供格式與字元受遮罩模式約束的文字輸入區域。遮罩由特殊字元組成：
- `0`：必填數字（0-9）
- `9`：選填數字或空格
- `L`：必填字母（a-z, A-Z）
- `A`：必填英數字元
- `\`：逸出字元，讓特殊字元變成文字

### 重點程式碼範例（如有）
```xml
<StackPanel Margin="20">
  <TextBlock Margin="0 5">International phone number:</TextBlock>
  <MaskedTextBox Mask="(+09) 000 000 0000" />
  <TextBlock Margin="0 15 0 5">UK VAT number:</TextBlock>
  <MaskedTextBox Mask="GB 000 000 000" />
</StackPanel>
```

---

## Menu 選單

**來源：** https://docs.avaloniaui.net/docs/reference/controls/menu

`Menu` 控制項可為應用程式新增選單結構，通常放置在 DockPanel 的頂端。選單項目使用 `<MenuItem>` 元素巢狀定義，第一層為水平選單，之後的層級為下拉選單。

**分隔線：** `<Separator>` 或 `<MenuItem Header="-" />`  
**加速鍵：** 在 Header 中用底線前置字母，例如 `Header="_File"`  
**命令綁定：** 透過 `Command="{Binding OpenCommand}"` 綁定 ICommand

### 重點程式碼範例（如有）
```xml
<Window>
  <DockPanel>
    <Menu DockPanel.Dock="Top">
      <MenuItem Header="_File">
        <MenuItem Header="_Open..."/>
        <Separator/>
        <MenuItem Header="_Exit"/>
      </MenuItem>
      <MenuItem Header="_Edit">
        <MenuItem Header="Copy"/>
        <MenuItem Header="Paste"/>
      </MenuItem>
    </Menu>
  </DockPanel>
</Window>
```

加入圖示：
```xml
<MenuItem Header="Copy">
   <MenuItem.Icon>
      <PathIcon Data="{StaticResource copy_regular}"/>
   </MenuItem.Icon>
</MenuItem>
```

---

## Menu Controls 選單控制項索引

**來源：** https://docs.avaloniaui.net/docs/reference/controls/menu-controls

選單控制項類型包含：ContextMenu（附加到控制項的右鍵選單）、Menu（頂層視窗選單）、NativeMenu（macOS 和部分 Linux 原生選單）、TabStrip（作為水平選單使用）。

---

## MenuFlyout 選單彈出選單

**來源：** https://docs.avaloniaui.net/docs/reference/controls/menu-flyout

`MenuFlyout` 讓您將簡易選單作為控制項的 Flyout 使用，可作為右鍵選單的替代方案。注意：在 MenuFlyout 中 `<Separator/>` 不起作用，應改用 `<MenuItem Header="-" />`。

### 重點程式碼範例（如有）
```xml
<Button Content="Button" HorizontalAlignment="Center">
  <Button.Flyout>
    <MenuFlyout>
      <MenuItem Header="Open"/>
      <MenuItem Header="-"/>
      <MenuItem Header="Close"/>
    </MenuFlyout>
  </Button.Flyout>
</Button>
```

動態 MenuFlyout（由 ViewModel 集合產生）：
```xml
<Button Content="Button">
  <Button.Flyout>
    <MenuFlyout ItemsSource="{Binding MyMenuItems}">
      <MenuFlyout.ItemContainerTheme>
        <ControlTheme TargetType="MenuItem" BasedOn="{StaticResource {x:Type MenuItem}}"
          x:DataType="l:MyMenuItemViewModel">
          <Setter Property="Header" Value="{Binding Header}"/>
          <Setter Property="ItemsSource" Value="{Binding Items}"/>
          <Setter Property="Command" Value="{Binding Command}"/>
        </ControlTheme>
      </MenuFlyout.ItemContainerTheme>
    </MenuFlyout>
  </Button.Flyout>
</Button>
```

---

## NativeMenu 原生選單

**來源：** https://docs.avaloniaui.net/docs/reference/controls/nativemenu

`NativeMenu` 在 macOS 和部分 Linux 發行版顯示原生選單。支援鍵盤手勢（Gesture 屬性），格式為 `修飾鍵+按鍵`，如 `Meta+O`。分隔線使用 `<NativeMenuItemSeparator/>`。

在 macOS 上，標頭為 `Edit` 的 NativeMenuItem 會自動包含額外的 macOS 功能。

### 重點程式碼範例（如有）
```xml
<NativeMenu.Menu>
    <NativeMenu>
        <NativeMenuItem Header="File" IsVisible="true">
            <NativeMenu>
                <NativeMenuItem Header="Open…" Click="FileOpen_OnClick" Gesture="Meta+O" />
                <NativeMenuItem Header="Save As…" Click="FileSaveAs_OnClick" Gesture="Meta+Shift+S" />
            </NativeMenu>
        </NativeMenuItem>
        <NativeMenuItem Header="Edit" IsEnabled="true">
            <NativeMenu>
                <NativeMenuItem Header="Cut" Command="{Binding CutCommand}" Gesture="Meta+X" />
            </NativeMenu>
        </NativeMenuItem>
    </NativeMenu>
</NativeMenu.Menu>
```

---

## NumericUpDown 數字上下選擇器

**來源：** https://docs.avaloniaui.net/docs/reference/controls/numericupdown

`NumericUpDown` 是一個可編輯的數值輸入框，附帶上下旋轉按鈕。非數字字元會被忽略。可透過按鈕、鍵盤方向鍵或滑鼠滾輪改變值。值和屬性為可為空的 decimal 型別，可設定自訂範圍和增量。

### 重點程式碼範例（如有）
```xml
<NumericUpDown Value="10" />
```

---

## Panel 面板

**來源：** https://docs.avaloniaui.net/docs/reference/controls/panel

`Panel` 是最基本可容納多個子控制項的容器。子控制項依照其水平和垂直對齊屬性排列，並依 XAML 中出現的順序繪製。若佔據相同空間則會相互重疊。

### 重點程式碼範例（如有）
```xml
<Panel Height="300" Width="300">
    <Rectangle Fill="Red" Height="100" VerticalAlignment="Top"/>
    <Rectangle Fill="Blue" Opacity="0.5" Width="100" HorizontalAlignment="Right" />
    <Rectangle Fill="Green" Opacity="0.5" Height="100" VerticalAlignment="Bottom"/>
    <Rectangle Fill="Orange" Width="100" HorizontalAlignment="Left"/>
</Panel>
```

---

## PathIcon 路徑圖示

**來源：** https://docs.avaloniaui.net/docs/reference/controls/path-icon

`PathIcon` 使用向量路徑幾何圖形顯示圖示。通常與 `StreamGeometry` 資源配合使用，透過 `Data` 屬性引用。常見於選單項目圖示和工具列。

---

## Popup Controls 彈出控制項索引

**來源：** https://docs.avaloniaui.net/docs/reference/controls/popup-controls

可附加到其他控制項提供彈出內容的控制項：ContextMenu（右鍵選單）、Flyouts（飛出選單）、ToolTip（工具提示）。

---

## ProgressBar 進度條

**來源：** https://docs.avaloniaui.net/docs/reference/controls/progressbar

`ProgressBar` 以比例填充的長條顯示進度值，可選擇顯示百分比文字。`ShowProgressText` 顯示預設百分比；`ProgressTextFormat` 可自訂格式字串。

### 重點程式碼範例（如有）
```xml
<ProgressBar Margin="0 10" Height="20"
             Minimum="0" Maximum="100" Value="14"
             ShowProgressText="True"/>
<ProgressBar Margin="0 10" Height="20"
             Minimum="0" Maximum="100" Value="92"
             Foreground="Red"
             ShowProgressText="True"/>
```

---

## RefreshContainer 重新整理容器

**來源：** https://docs.avaloniaui.net/docs/reference/controls/refreshcontainer

`RefreshContainer` 讓使用者透過下拉手勢重新整理內容。內容必須是 `ScrollViewer` 或包含 ScrollViewer 的控制項。使用 `PullDirection` 設定拉取方向。刷新完成後必須呼叫 `deferral.Complete()` 通知容器。

**視覺化狀態：** Idle → Interacting → Pending → Refreshing（或 Peeking）

### 重點程式碼範例（如有）
```xml
<RefreshContainer PullDirection="TopToBottom"
                RefreshRequested="RefreshContainerPage_RefreshRequested">
    <ListBox ItemsSource="{Binding Items}"/>
</RefreshContainer>
```
```csharp
private void RefreshContainerPage_RefreshRequested(object? sender, RefreshRequestedEventArgs e)
{
    var deferral = e.GetDeferral();
    // 重新整理資料
    deferral.Complete();
}
```

---

## RelativePanel 相對面板

**來源：** https://docs.avaloniaui.net/docs/reference/controls/relativepanel

`RelativePanel` 透過相對位置屬性來排列子控制項，可相對於面板本身或其他兄弟控制項。預設位置為左上角。

**常用附加屬性：**
- `AlignTopWithPanel`/`AlignBottomWithPanel`/`AlignLeftWithPanel`/`AlignRightWithPanel`：相對面板對齊
- `AlignTopWith`/`Above`/`Below`/`LeftOf`/`RightOf`：相對兄弟控制項對齊
- `AlignHorizontalCenterWithPanel`/`AlignVerticalCenterWithPanel`：置中對齊

⚠️ 注意：不可自我參照（循環參考），同一控制項同一方向屬性不可重複設定。

### 重點程式碼範例（如有）
```xml
<RelativePanel>
  <Rectangle x:Name="RedRect" Fill="Red" Height="50" Width="50"/>
  <Rectangle x:Name="BlueRect" Fill="Blue" Height="50" Width="150"
             RelativePanel.RightOf="RedRect" />
  <Rectangle x:Name="GreenRect" Fill="Green" Height="100"
             RelativePanel.Below="RedRect"
             RelativePanel.AlignLeftWith="RedRect"
             RelativePanel.AlignRightWith="BlueRect"/>
</RelativePanel>
```

---

## Repeating Data Controls 重複資料控制項索引

**來源：** https://docs.avaloniaui.net/docs/reference/controls/repeating-data-controls

顯示重複資料的控制項：DataGrid（表格）、ItemsControl（基本集合顯示）、ItemsRepeater（含版面和資料模板）、ListBox（可選取清單）、ComboBox（下拉清單）。

---

## ScrollBar 捲軸

**來源：** https://docs.avaloniaui.net/docs/reference/controls/scrollbar

`ScrollBar` 可水平或垂直顯示，預設值範圍為 0-100。可設定範圍及小步進/大步進。小步進用方向鍵控制，大步進用點擊軌道或 PageUp/PageDown 鍵。

### 重點程式碼範例（如有）
```xml
<ScrollBar Visibility="Auto"
           HorizontalAlignment="Left"
           Scroll="ScrollHandler"/>
```
```csharp
public void ScrollHandler(object source, ScrollEventArgs args)
{
    valueText.Text = args.NewValue.ToString();
}
```

---

## ScrollViewer 捲動檢視器

**來源：** https://docs.avaloniaui.net/docs/reference/controls/scrollviewer

`ScrollViewer` 當內容大於其容器時提供捲軸。⚠️ 不能放在高度或寬度無限的容器中（如 StackPanel）。若巢狀含有可捲動的控制項，可用附加屬性控制是否讓外層繼續捲動。

### 重點程式碼範例（如有）
```xml
<Border Background="AliceBlue" Width="300" Height="300">
  <ScrollViewer>
    <StackPanel>
      <TextBlock FontSize="22" Height="100" Background="LightBlue">Block 1</TextBlock>
      <TextBlock FontSize="22" Height="100">Block 2</TextBlock>
      <TextBlock FontSize="22" Height="100" Background="LightBlue">Block 3</TextBlock>
    </StackPanel>
  </ScrollViewer>
</Border>
```

---

## SelectableTextBlock 可選取文字區塊

**來源：** https://docs.avaloniaui.net/docs/reference/controls/selectable-textblock

`SelectableTextBlock` 是唯讀文字標籤，允許使用者選取並複製文字。支援多行顯示，可控制字型樣式。`SelectionBrush` 可自訂選取顏色，`SelectionStart`/`SelectionEnd` 設定預設選取範圍。

### 重點程式碼範例（如有）
```xml
<SelectableTextBlock Margin="0 5" FontSize="18" FontWeight="Bold">Heading</SelectableTextBlock>
<SelectableTextBlock Margin="0 5" FontStyle="Italic" SelectionBrush="Red">
  This is a single line.
</SelectableTextBlock>
```

---

## Separator 分隔線

**來源：** https://docs.avaloniaui.net/docs/reference/controls/separator

`Separator` 控制項提供 `Menu` 控制項內的視覺分隔線。

---

## Slider 滑桿

**來源：** https://docs.avaloniaui.net/docs/reference/controls/slider

`Slider` 以滑鈕在軌道上的相對位置表示數值。支援拖曳、鍵盤和點擊互動改變值。可與 TextBox 雙向綁定實現即時更新。

### 重點程式碼範例（如有）
```xml
<StackPanel Margin="20">
  <TextBlock Text="{Binding #slider.Value}" HorizontalAlignment="Center"/>
  <Slider x:Name="slider" />
</StackPanel>
```

與 ViewModel 綁定（ReactiveUI）：
```csharp
public class MainViewModel : ViewModelBase
{
    private int _damage;
    public int Damage
    {
        get => _damage;
        set => this.RaiseAndSetIfChanged(ref _damage, value);
    }
}
```

---

## SplitView 分割檢視

**來源：** https://docs.avaloniaui.net/docs/reference/controls/splitview

`SplitView` 提供主內容區域和側邊窗格的雙區容器。窗格可展開/收合，收合時可完全隱藏或保留小空間（放置圖示按鈕）。`DisplayMode` 控制窗格的展開收合行為：Inline、CompactInline、Overlay、CompactOverlay。

### 重點程式碼範例（如有）
```xml
<SplitView IsPaneOpen="True" DisplayMode="Inline" OpenPaneLength="300">
    <SplitView.Pane>
        <TextBlock Text="Pane" VerticalAlignment="Center" HorizontalAlignment="Center"/>
    </SplitView.Pane>
    <Grid>
        <TextBlock Text="Content" VerticalAlignment="Center" HorizontalAlignment="Center"/>
    </Grid>
</SplitView>
```

---

## StackPanel 堆疊面板

**來源：** https://docs.avaloniaui.net/docs/reference/controls/stackpanel

`StackPanel` 將子控制項水平或垂直堆疊排列。垂直方向時，未設定寬度的子控制項會自動伸展填滿可用寬度；水平方向時，未設定高度的子控制項會自動伸展。StackPanel 本身在堆疊方向上會展開以容納所有子控制項。

### 重點程式碼範例（如有）
```xml
<StackPanel Width="200">
    <Rectangle Fill="Red" Height="50"/>
    <Rectangle Fill="Blue" Height="50"/>
    <Rectangle Fill="Green" Height="100"/>
    <Rectangle Fill="Orange" Height="50"/>
</StackPanel>
```

---

## TabControl 頁籤控制項

**來源：** https://docs.avaloniaui.net/docs/reference/controls/tabcontrol

`TabControl` 將視圖細分為多個頁籤項目。每個 TabItem 有標題和內容區域，標題依 XAML 中的順序排列在頁籤列中。點擊標題後對應內容顯示在頁籤列下方。

### 重點程式碼範例（如有）
```xml
<TabControl Margin="5">
  <TabItem Header="Tab 1">
    <TextBlock Margin="5">This is tab 1 content</TextBlock>
  </TabItem>
  <TabItem Header="Tab 2">
    <TextBlock Margin="5">This is tab 2 content</TextBlock>
  </TabItem>
</TabControl>
```

---

## TabStrip 頁籤列

**來源：** https://docs.avaloniaui.net/docs/reference/controls/tabstrip

`TabStrip` 顯示頁籤標題列，可用作水平選單。與 TabControl 不同，TabStrip 不負責顯示選取頁籤的內容，需要開發者自行回應 `SelectionChanged` 事件或監控 `SelectedItem` 屬性來顯示內容（通常搭配 ContentControl）。

### 重點程式碼範例（如有）
```xml
<TabStrip ItemsSource="{Binding MyTabs}" SelectedItem="{Binding MySelectedItem}">
    <TabStrip.ItemTemplate>
        <DataTemplate x:DataType="vm:MyViewModel">
            <TextBlock Text="{Binding Header}"/>
        </DataTemplate>
    </TabStrip.ItemTemplate>
</TabStrip>
```

---

## Text Controls 文字控制項索引

**來源：** https://docs.avaloniaui.net/docs/reference/controls/text-controls

文字顯示與編輯控制項：AutoCompleteBox（自動完成）、TextBlock（唯讀文字）、SelectableTextBlock（可選取唯讀文字）、TextBox（可編輯文字）、MaskedTextBox（格式遮罩輸入）。

---

## TextBlock 文字區塊

**來源：** https://docs.avaloniaui.net/docs/reference/controls/textblock

`TextBlock` 是唯讀的文字顯示控制項，支援多行和完整字型控制。使用 `xml:space="preserve"` 保留換行和空白。支援 Inlines（Run、LineBreak、Bold、Italic、Underline、InlineUIContainer 等）實現多樣格式化。

**常用屬性：** Text、FontSize、FontWeight（Bold）、FontStyle（Italic）、TextDecorations（Underline/Strikethrough）

### 重點程式碼範例（如有）
```xml
<StackPanel Margin="20">
  <TextBlock Margin="0 5" FontSize="18" FontWeight="Bold">Heading</TextBlock>
  <TextBlock Margin="0 5" FontStyle="Italic" xml:space="preserve">This is a single line.</TextBlock>
  <TextBlock Margin="0 5" xml:space="preserve">
This is a multi-line display
that has returns in it.
  </TextBlock>
</StackPanel>
```

---

## TextBox 文字輸入框

**來源：** https://docs.avaloniaui.net/docs/reference/controls/textbox

`TextBox` 提供單行或多行鍵盤輸入區域。支援 `PasswordChar` 遮蔽密碼輸入、`Watermark` 提示文字、`AcceptsReturn` 多行輸入、`TextWrapping` 換行。

### 重點程式碼範例（如有）
```xml
<TextBox Watermark="Enter your name"/>
<TextBox PasswordChar="*" Watermark="Enter your password"/>
<TextBox Height="100" AcceptsReturn="True" TextWrapping="Wrap"/>
```

---

## TimePicker 時間選擇器

**來源：** https://docs.avaloniaui.net/docs/reference/controls/timepicker

`TimePicker` 提供 2-4 個旋轉控制項讓使用者選取時間。支援 12 或 24 小時制，可選擇顯示秒數。`ClockIdentifier`（12HourClock/24HourClock）、`UseSeconds`、`MinuteIncrement`、`SecondIncrement`、`SelectedTime`（nullable TimeSpan）。

### 重點程式碼範例（如有）
```xml
<TimePicker ClockIdentifier="24HourClock" MinuteIncrement="20"/>
```
```xml
<TimePicker SelectedTime="09:15"/>
```
```csharp
TimePicker timePicker = new TimePicker
{
    SelectedTime = new TimeSpan(9, 15, 0)
};
```

---

## ToolTip 工具提示

**來源：** https://docs.avaloniaui.net/docs/reference/controls/tooltip

`ToolTip` 是當使用者懸停於附加的「宿主」控制項時顯示的彈出視窗。可使用 `<ToolTip.Tip>` 元素提供更豐富的內容（如 StackPanel 組合）。`ToolTip.Placement` 控制顯示位置。

### 重點程式碼範例（如有）
```xml
<Rectangle Fill="Aqua" Height="200" Width="400" ToolTip.Placement="Bottom">
    <ToolTip.Tip>
      <StackPanel>
        <TextBlock FontSize="16">Rectangle</TextBlock>
        <TextBlock>Some explanation here.</TextBlock>
      </StackPanel>
    </ToolTip.Tip>
</Rectangle>
```

---

## TransitioningContentControl 轉場內容控制項

**來源：** https://docs.avaloniaui.net/docs/reference/controls/transitioningcontentcontrol

`TransitioningContentControl` 在內容變更時使用頁面轉場動畫。常用於投影片輪播。當 `Content` 綁定的屬性改變時，自動套用轉場效果。

### 重點程式碼範例（如有）
```xml
<TransitioningContentControl Content="{Binding SelectedImage}">
    <TransitioningContentControl.PageTransition>
        <PageSlide Orientation="Horizontal" Duration="0:00:00.500" />
    </TransitioningContentControl.PageTransition>
    <TransitioningContentControl.ContentTemplate>
        <DataTemplate DataType="Bitmap">
            <Image Source="{Binding}" />
        </DataTemplate>
    </TransitioningContentControl.ContentTemplate>
</TransitioningContentControl>
```

---

## TrayIcon 系統托盤圖示

**來源：** https://docs.avaloniaui.net/docs/reference/controls/tray-icon

`TrayIcon` 讓應用程式在系統托盤顯示圖示和原生選單。支援 Windows、macOS 和部分 Linux（確認支援 Ubuntu）。必須在 Application XAML 中定義。

### 重點程式碼範例（如有）
```xml
<Application>
  <TrayIcon.Icons>
    <TrayIcons>
      <TrayIcon Icon="/Assets/avalonia-logo.ico"
                ToolTipText="Avalonia Tray Icon ToolTip">
        <TrayIcon.Menu>
          <NativeMenu>
            <NativeMenuItem Header="Settings">
              <NativeMenu>
                <NativeMenuItem Header="Option 1" />
                <NativeMenuItemSeparator />
                <NativeMenuItem Header="Option 3" />
              </NativeMenu>
            </NativeMenuItem>
          </NativeMenu>
        </TrayIcon.Menu>
      </TrayIcon>
    </TrayIcons>
  </TrayIcon.Icons>
</Application>
```

---

## TreeDataGrid 樹狀資料網格

**來源：** https://docs.avaloniaui.net/docs/reference/controls/treedatagrid/

`TreeDataGrid` 在單一視圖中同時顯示階層式和表格式資料，是 TreeView 與 DataGrid 的結合。  
**需要安裝 NuGet 套件。**  
兩種操作模式：
- **階層式（Hierarchical）**：以樹狀顯示資料，附帶可選欄位
- **平坦式（Flat）**：以二維表格顯示，類似 DataGrid

---

## TreeDataGrid - 建立平坦式資料網格

**來源：** https://docs.avaloniaui.net/docs/reference/controls/treedatagrid/creating-a-flat-treedatagrid

使用 `FlatTreeDataGridSource<T>` 建立平坦式 TreeDataGrid，並定義 `TextColumn` 欄位。

### 重點程式碼範例（如有）
```csharp
PersonSource = new FlatTreeDataGridSource<Person>(_people)
{
    Columns =
    {
        new TextColumn<Person, string>("First Name", x => x.FirstName),
        new TextColumn<Person, string>("Last Name", x => x.LastName),
        new TextColumn<Person, int>("Age", x => x.Age),
    },
};
```

---

## TreeDataGrid - 建立階層式資料網格

**來源：** https://docs.avaloniaui.net/docs/reference/controls/treedatagrid/creating-a-hierarchical-treedatagrid

使用 `HierarchicalTreeDataGridSource<T>` 建立階層式 TreeDataGrid。第一欄使用 `HierarchicalExpanderColumn` 包裹，提供展開/收合按鈕並指定子項目選擇器。

### 重點程式碼範例（如有）
```xml
<TreeDataGrid Source="{Binding PersonSource}"/>
```
```csharp
PersonSource = new HierarchicalTreeDataGridSource<Person>(_people)
{
    Columns =
    {
        new HierarchicalExpanderColumn<Person>(
            new TextColumn<Person, string>("First Name", x => x.FirstName),
            x => x.Children),
        new TextColumn<Person, string>("Last Name", x => x.LastName),
        new TextColumn<Person, int>("Age", x => x.Age),
    },
};
```

---

## TreeDataGrid 欄位類型

**來源：** https://docs.avaloniaui.net/docs/reference/controls/treedatagrid/treedatagrid-column-types

TreeDataGrid 支援三種欄位類型：
- **TextColumn**：顯示文字值，`new TextColumn<T, TValue>("Header", x => x.Property)`
- **HierarchicalExpanderColumn**：僅用於階層模式，包裹內部欄位並顯示展開按鈕
- **TemplateColumn**：完全自訂欄位，使用 `FuncDataTemplate` 建立任意控制項

### 重點程式碼範例（如有）
```csharp
new TextColumn<ItemClass, string>("Column Header", x => x.Property)
new HierarchicalExpanderColumn<ItemClass>(
    new TextColumn<ItemClass, string>("Column Header", x => x.Property),
    x => x.Children)
new TemplateColumn<ItemClass>("Column Header",
    new FuncDataTemplate<T>((a,e) => new SomeControl()))
```

---

## TreeView 樹狀檢視

**來源：** https://docs.avaloniaui.net/docs/reference/controls/treeview-1

`TreeView` 顯示階層式資料的樹狀結構。支援 `ObservableCollection` 綁定和多選（`SelectedNodes`）。通常搭配 `HierarchicalDataTemplate` 定義各層級的顯示方式。

### 重點程式碼範例（如有）
```csharp
Nodes = new ObservableCollection<Node>
{
    new Node("Animals", new ObservableCollection<Node>
    {
        new Node("Mammals", new ObservableCollection<Node>
        {
            new Node("Lion"), new Node("Cat"), new Node("Zebra")
        })
    }),
};
```

---

## UniformGrid 均勻網格

**來源：** https://docs.avaloniaui.net/docs/reference/controls/uniform-grid

`UniformGrid` 將可用空間均勻分割為等大小的儲存格。使用 `Rows` 和 `Columns` 屬性設定行列數。

### 重點程式碼範例（如有）
```xml
<UniformGrid Rows="1" Columns="3" Width="300" Height="200">
    <Rectangle Fill="navy" />
    <Rectangle Fill="white" />
    <Rectangle Fill="red" />
</UniformGrid>
```

---

## UserControl 使用者控制項

**來源：** https://docs.avaloniaui.net/docs/reference/controls/usercontrol

`UserControl` 是 `ContentControl` 的子類別，代表預先定義佈局的可重用控制項集合。通常不直接建立 `UserControl` 實例，而是為每個應用程式「視圖」建立 `UserControl` 的子類別。

---

## Value Selector Controls 值選擇控制項索引

**來源：** https://docs.avaloniaui.net/docs/reference/controls/value-selector-controls

以圖形呈現並允許互動改變特定型別值的控制項：CheckBox（布林值）、Slider（Double）、Calendar（DateTime）、CalendarDatePicker（DateTime）、DatePicker（DateTime）、TimePicker（TimeSpan）。

---

## Viewbox 檢視方塊

**來源：** https://docs.avaloniaui.net/docs/reference/controls/viewbox

`Viewbox` 是可縮放其內容的容器控制項。`Stretch` 屬性控制縮放方式（Uniform/UniformToFill/Fill/None），`StretchDirection` 控制縮放方向（UpOnly/DownOnly/Both）。

### 重點程式碼範例（如有）
```xml
<Viewbox Stretch="Uniform" Width="300" Height="300">
   <Ellipse Width="50" Height="50" Fill="CornflowerBlue" />
</Viewbox>
```

---

## Window 視窗

**來源：** https://docs.avaloniaui.net/docs/reference/controls/window

`Window` 是頂層的 `ContentControl`。通常為每種視窗類型建立子類別而非直接使用。支援 Show/Hide/Close 方法，`ShowDialog` 以模態對話框方式顯示。可透過 `Closing` 事件的 `e.Cancel = true` 防止關閉。

**常用屬性：** Title、Icon、SizeToContent、WindowState

### 重點程式碼範例（如有）
```csharp
// 顯示視窗
var window = new MyWindow();
window.Show();

// 顯示模態對話框並等待結果
var result = await dialog.ShowDialog<string>(ownerWindow);

// 防止關閉
window.Closing += (s, e) => { e.Cancel = true; };
```

---

## WrapPanel 換行面板

**來源：** https://docs.avaloniaui.net/docs/reference/controls/wrappanel

`WrapPanel` 由左到右排列子控制項，當空間不足時自動換行。設定 `Orientation="Vertical"` 時改為由上到下排列，列滿時換欄。

### 重點程式碼範例（如有）
```xml
<WrapPanel>
    <Rectangle Fill="Navy" Width="100" Height="100" Margin="20"/>
    <Rectangle Fill="Yellow" Width="100" Height="100" Margin="20"/>
    <Rectangle Fill="Green" Width="100" Height="100" Margin="20"/>
    <Rectangle Fill="Red" Width="100" Height="100" Margin="20"/>
</WrapPanel>
```

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter03 — Controls Basics (ItemsControl, ContentControl, UserControl)

*(基礎控制項範例已在 getting-started.md Chapter03 中說明)*

**ContentControl vs UserControl**
- `ContentControl`：持有單一內容物件，Window/Button 都繼承自它
- `UserControl`：封裝可重用視圖，有自己的 XAML 和程式碼後置，各自有獨立 NameScope

**資料範本（DataTemplate）**
```xml
<ItemsControl Items="{Binding RecentOrders}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <ui:OrderRow />   <!-- DataContext = 當前 item（OrderViewModel）-->
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

---

### Chapter06 — Controls Showcase

**表單輸入控制項**
```xml
<Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto" RowSpacing="8" ColumnSpacing="12">
  <TextBlock Text="Name:"/>
  <TextBox Grid.Column="1" Text="{Binding Customer.Name}" Watermark="Full name"/>
  <TextBlock Grid.Row="1" Text="Email:"/>
  <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding Customer.Email}"/>
  <TextBlock Grid.Row="2" Text="Phone:"/>
  <MaskedTextBox Grid.Row="2" Grid.Column="1" Mask="(000) 000-0000" Value="{Binding Customer.Phone}"/>
</Grid>
<StackPanel Orientation="Horizontal" Spacing="12">
  <NumericUpDown Width="120" Minimum="0" Maximum="20" Value="{Binding Customer.Seats}" Header="Seats"/>
  <DatePicker SelectedDate="{Binding Customer.RenewalDate}" Header="Renewal"/>
</StackPanel>
```

**切換控制項與選項**
```xml
<GroupBox Header="Plan options" Padding="12">
  <StackPanel Spacing="8">
    <ToggleSwitch Header="Enable auto-renew" IsChecked="{Binding Customer.AutoRenew}"/>
    <StackPanel Orientation="Horizontal" Spacing="12">
      <CheckBox Content="Include analytics" IsChecked="{Binding Customer.IncludeAnalytics}"/>
      <CheckBox Content="Priority support" IsChecked="{Binding Customer.IncludeSupport}"/>
    </StackPanel>
    <StackPanel Orientation="Horizontal" Spacing="12">
      <RadioButton GroupName="Plan" Content="Starter" IsChecked="{Binding Customer.IsStarter}"/>
      <RadioButton GroupName="Plan" Content="Growth" IsChecked="{Binding Customer.IsGrowth}"/>
    </StackPanel>
    <Button Content="Save" Command="{Binding SaveCommand}"/>
  </StackPanel>
</GroupBox>
```

**ListBox + 資料模板**
```xml
<ListBox Items="{Binding Teams}" SelectedItem="{Binding SelectedTeam}" Height="160">
  <ListBox.ItemTemplate>
    <DataTemplate>
      <StackPanel Orientation="Horizontal" Spacing="12">
        <Ellipse Width="24" Height="24" Fill="{Binding Color}"/>
        <TextBlock Text="{Binding Name}" FontWeight="SemiBold"/>
      </StackPanel>
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

**虛擬化清單**
```xml
<ListBox Items="{Binding ManyItems}"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.CacheLength="2"/>
```

**TreeView（階層資料）**
```xml
<TreeView Items="{Binding Departments}">
  <TreeView.ItemTemplate>
    <TreeDataTemplate ItemsSource="{Binding Teams}">
      <TextBlock Text="{Binding Name}" FontWeight="SemiBold"/>
      <TreeDataTemplate.ItemTemplate>
        <DataTemplate>
          <TextBlock Text="{Binding Name}" Margin="24,0,0,0"/>
        </DataTemplate>
      </TreeDataTemplate.ItemTemplate>
    </TreeDataTemplate>
  </TreeView.ItemTemplate>
</TreeView>
```

**TabControl + SplitView + Expander**
```xml
<TabControl SelectedIndex="{Binding SelectedTab}">
  <TabItem Header="Overview"><TextBlock Text="Overview" Margin="12"/></TabItem>
  <TabItem Header="Reports"><TextBlock Text="Reports" Margin="12"/></TabItem>
</TabControl>

<SplitView DisplayMode="CompactInline"
           IsPaneOpen="{Binding IsPaneOpen}"
           OpenPaneLength="240" CompactPaneLength="56">
  <SplitView.Pane><!-- 導覽項目 --></SplitView.Pane>
</SplitView>

<Expander Header="Advanced filters" IsExpanded="False">
  <StackPanel Margin="12" Spacing="8">
    <ComboBox Items="{Binding FilterSets}" SelectedItem="{Binding SelectedFilter}"/>
  </StackPanel>
</Expander>
```

**AutoCompleteBox + ColorPicker**
```xml
<AutoCompleteBox Width="240"
                 Items="{Binding Suggestions}"
                 Text="{Binding Query, Mode=TwoWay}">
  <AutoCompleteBox.ItemTemplate>
    <DataTemplate>
      <StackPanel Orientation="Horizontal" Spacing="8">
        <TextBlock Text="{Binding Icon}"/>
        <TextBlock Text="{Binding Title}"/>
      </StackPanel>
    </DataTemplate>
  </AutoCompleteBox.ItemTemplate>
</AutoCompleteBox>
<ColorPicker SelectedColor="{Binding ThemeColor}"/>
```

**SplitButton + Menu（指令介面）**
```xml
<SplitButton Content="Export" Command="{Binding ExportAllCommand}">
  <SplitButton.Flyout>
    <MenuFlyout>
      <MenuItem Header="Export CSV" Command="{Binding ExportCsvCommand}"/>
      <MenuItem Header="Export JSON" Command="{Binding ExportJsonCommand}"/>
    </MenuFlyout>
  </SplitButton.Flyout>
</SplitButton>

<Menu>
  <MenuItem Header="File">
    <MenuItem Header="New" Command="{Binding NewCommand}"/>
    <Separator/>
    <MenuItem Header="Exit" Command="{Binding ExitCommand}"/>
  </MenuItem>
</Menu>
```

**通知與狀態列**
```csharp
var notifications = new WindowNotificationManager(this)
{
    Position = NotificationPosition.TopRight, MaxItems = 3
};
notifications.Show(new Notification("Update available", "Restart to apply updates.", NotificationType.Success));
```

```xml
<StatusBar>
  <StatusBarItem>
    <StackPanel Orientation="Horizontal" Spacing="8">
      <TextBlock Text="Ready"/>
      <ProgressBar Width="120" IsIndeterminate="{Binding IsBusy}"/>
    </StackPanel>
  </StatusBarItem>
</StatusBar>
```

**控制項虛擬狀態樣式**
```xml
<Style Selector="Button.primary">
  <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
  <Setter Property="Foreground" Value="White"/>
</Style>
<Style Selector="Button.primary:pointerover">
  <Setter Property="Background" Value="{DynamicResource AccentBrush2}"/>
</Style>
```

---

### Chapter23 — Custom Controls & Drawing

#### 關鍵概念

**選擇方式**

| 情境 | 自訂繪製 (override Render) | 模板控件 (ControlTemplate) |
|---|---|---|
| 像素級圖形、圖表 | ✓ | |
| 繪圖基元驅動動畫 | ✓ | |
| 由既有控件組合 | | ✓ |
| 消費者需 XAML 重設樣式 | | ✓ |

**AffectsRender / InvalidateVisual**
在靜態建構子呼叫 `AffectsRender<TControl>(prop1, prop2)` 自動在屬性變更時安排重繪，避免手動呼叫 `InvalidateVisual()`。

**DrawingContext 基礎操作**
`DrawGeometry`、`DrawRectangle`、`DrawEllipse`、`DrawImage`、`DrawText`、`PushClip/Opacity/Transform`（使用 `using` 自動 pop）。

**TemplatedControl 生命週期**
`TemplateApplied` → `OnApplyTemplate(e)` → 透過 `e.NameScope.Find<T>("PART_*")` 取得命名部件。

**PseudoClasses**
`PseudoClasses.Set(":state", true)` 讓樣式/動畫響應控件狀態，無需重寫模板。

#### 代碼範例

```csharp
// 自訂繪製控件 — Sparkline
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush>(nameof(Stroke), Brushes.DeepSkyBlue);

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var values = Values;
        var bounds = Bounds;
        if (values is null || values.Count < 2) return;

        double min = values.Min(), max = values.Max();
        double range = Math.Max(1e-9, max - min);

        using var geometry = new StreamGeometry();
        using (var gctx = geometry.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                double t = i / (double)(values.Count - 1);
                double x = bounds.X + t * bounds.Width;
                double yNorm = (values[i] - min) / range;
                double y = bounds.Y + (1 - yNorm) * bounds.Height;
                if (i == 0) gctx.BeginFigure(new Point(x, y), isFilled: false);
                else gctx.LineTo(new Point(x, y));
            }
            gctx.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(Stroke, StrokeThickness), geometry);
    }
}
```

```xml
<!-- Sparkline 使用 -->
<local:Sparkline Width="160" Height="36" Values="3,7,4,8,12" StrokeThickness="2"/>
```

```xml
<!-- TemplatedControl — Badge ControlTheme -->
<ControlTheme TargetType="local:Badge">
  <Setter Property="Template">
    <ControlTemplate TargetType="local:Badge">
      <Border x:Name="PART_Border"
              Background="{TemplateBinding Background}"
              CornerRadius="{TemplateBinding CornerRadius}"
              Padding="6,0">
        <ContentPresenter x:Name="PART_Content"
                          Content="{TemplateBinding Content}"
                          HorizontalAlignment="Center"
                          VerticalAlignment="Center"
                          Foreground="{TemplateBinding Foreground}"/>
      </Border>
    </ControlTemplate>
  </Setter>
  <Setter Property="Background" Value="#E53935"/>
  <Setter Property="Foreground" Value="White"/>
  <Setter Property="CornerRadius" Value="8"/>
</ControlTheme>
```

```csharp
// Badge — OnApplyTemplate
public sealed class Badge : TemplatedControl
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<Badge, object?>(nameof(Content));

    Border? _border;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _border = e.NameScope.Find<Border>("PART_Border");
    }
}
```

---

### Chapter31 — Advanced & Extended Controls

#### 關鍵概念

**ColorPicker**
`Avalonia.Controls.ColorPicker` 命名空間；`ColorPicker` 繼承 `ColorView`，提供預覽區域 + flyout 編輯 UI（ColorSpectrum、滑桿、調色盤）。

**RefreshContainer（Pull-to-Refresh）**
`Avalonia.Controls.PullToRefresh`；使用 `RefreshCompletionDeferral` 等待非同步工作完成。

**WindowNotificationManager（Toast 通知）**
`Avalonia.Controls.Notifications`；設定 Position + MaxItems，呼叫 `Show(INotification)` 顯示 toast。

**DatePicker/TimePicker**
`DateTimeOffset?` 綁定；`Calendar` 支援多選（`SelectionMode="MultipleRange"`）。

**SplitView**
4 種 DisplayMode（Overlay/Inline/CompactOverlay/CompactInline）；管理焦點：Pane 開啟時移動焦點到第一個可焦點元素。

**SplitButton**
主要動作 + 次要 Flyout；`ToggleSplitButton` 加入 `:checked` 狀態。

#### 代碼範例

```xml
<!-- ColorPicker 自訂 ContentTemplate -->
<ColorPicker SelectedColor="{Binding AccentColor, Mode=TwoWay}">
  <ColorPicker.ContentTemplate>
    <DataTemplate>
      <StackPanel Orientation="Horizontal" Spacing="8">
        <Border Width="24" Height="24" Background="{Binding}" CornerRadius="4"/>
        <TextBlock Text="{Binding Converter={StaticResource RgbFormatter}}"/>
      </StackPanel>
    </DataTemplate>
  </ColorPicker.ContentTemplate>
</ColorPicker>
```

```xml
<!-- RefreshContainer -->
<ptr:RefreshContainer RefreshRequested="OnRefresh">
  <ptr:RefreshContainer.Visualizer>
    <ptr:RefreshVisualizer Orientation="TopToBottom"/>
  </ptr:RefreshContainer.Visualizer>
  <ScrollViewer>
    <ItemsControl ItemsSource="{Binding FeedItems}"/>
  </ScrollViewer>
</ptr:RefreshContainer>
```

```csharp
// RefreshContainer 非同步處理
private async void OnRefresh(object? sender, RefreshRequestedEventArgs e)
{
    using var deferral = e.GetDeferral();
    await ViewModel.LoadLatestAsync();
}
```

```xml
<!-- SplitView 導覽殼 -->
<SplitView IsPaneOpen="{Binding IsMenuOpen, Mode=TwoWay}"
           DisplayMode="CompactOverlay"
           CompactPaneLength="56"
           OpenPaneLength="240">
  <SplitView.Pane>
    <StackPanel>
      <Button Content="Dashboard" Command="{Binding GoHome}"/>
      <Button Content="Reports" Command="{Binding GoReports}"/>
    </StackPanel>
  </SplitView.Pane>
  <Frame Content="{Binding CurrentPage}"/>
</SplitView>
```

```xml
<!-- SplitButton -->
<SplitButton Content="Export" Command="{Binding ExportAll}">
  <SplitButton.Flyout>
    <MenuFlyout>
      <MenuItem Header="Export CSV" Command="{Binding ExportCsv}"/>
      <MenuItem Header="Export JSON" Command="{Binding ExportJson}"/>
    </MenuFlyout>
  </SplitButton.Flyout>
</SplitButton>
```

---

### Chapter34 — Code-First Layout & Property System

#### 關鍵概念

**Layout 基礎元件（純 C#）**
- `StackPanel`：直接設 `Orientation`、`Spacing`、`Margin`
- `Grid`：用 `RowDefinitions`/`ColumnDefinitions` 集合，附加屬性用靜態方法 `Grid.SetRow/Column/Span`
- `DockPanel`：`DockPanel.SetDock(control, Dock.Left)` 設定停靠

**AvaloniaObject API**
- `SetValue`：設定本地值，觸發變更通知
- `SetCurrentValue`：更新有效值但保留 Binding/Animation
- `GetObservable<T>(prop)`：響應式屬性觀察

**Fluent Helper Extensions**
為常用操作建立擴充方法（如 `DockLeft()`、`WithGridPosition(row, col)`、`WithMargin()`）保持程式碼可讀性。

**NameScope 管理**
手動 `NameScope.SetNameScope(container, scope)` + `scope.Register("name", control)` 複製 XAML 的命名功能。

#### 代碼範例

```csharp
// StackPanel
var layout = new StackPanel
{
    Orientation = Orientation.Vertical,
    Spacing = 12,
    Margin = new Thickness(24),
    Children =
    {
        new TextBlock { Text = "Customer" },
        new TextBox { Watermark = "Name" },
        new TextBox { Watermark = "Email" }
    }
};
```

```csharp
// Grid
var grid = new Grid
{
    ColumnDefinitions =
    {
        new ColumnDefinition(GridLength.Auto),
        new ColumnDefinition(GridLength.Star)
    },
    RowDefinitions =
    {
        new RowDefinition(GridLength.Auto),
        new RowDefinition(GridLength.Star)
    }
};

var title = new TextBlock { Text = "Orders", FontSize = 22 };
Grid.SetColumnSpan(title, 2);
grid.Children.Add(title);

var filterBox = new ComboBox { Items = Enum.GetValues<OrderStatus>() };
Grid.SetRow(filterBox, 1);
Grid.SetColumn(filterBox, 1);
grid.Children.Add(filterBox);
```

```csharp
// DockPanel + Fluent Extensions
var dock = new DockPanel
{
    LastChildFill = true,
    Children =
    {
        CreateSidebar().DockLeft(),
        CreateFooter().DockBottom(),
        CreateMainRegion()
    }
};

// Extension 定義
public static class DockExtensions
{
    public static T DockLeft<T>(this T control) where T : Control
    {
        DockPanel.SetDock(control, Dock.Left);
        return control;
    }
}
```

```csharp
// SetCurrentValue（保留 Binding）
var box = new TextBox();
box.SetCurrentValue(TextBox.WidthProperty, 240);

// 屬性觀察
box.GetObservable(TextBox.TextProperty)
   .Subscribe(text => _logger.Information("Text: {Text}", text));
```

---