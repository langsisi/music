# Avalonia 參考文件知識庫

此檔案包含 Avalonia 控件參考、樣式選擇器語法等技術參考內容。

---

## 參考文件索引

**來源：** https://docs.avaloniaui.net/docs/reference/

Avalonia 的參考文件包含：控件參考、內建資料綁定轉換器、動畫設置、手勢、屬性、樣式等。

---

## 動畫緩動函數（Easing Functions）

**來源：** https://docs.avaloniaui.net/docs/reference/animation-settings

`Animation` 元素的 `Easing` 屬性描述動畫屬性值從起始到結束的變化速率。`Avalonia.Animation.Easings` 包含以下緩動函數：LinearEasing（線性）、SineEaseIn/Out/InOut（正弦）、QuadraticEaseIn/Out/InOut（二次方）、CubicEaseIn/Out/InOut（三次方）、QuarticEaseIn/Out/InOut（四次方）。

---

## 內建資料綁定轉換器

**來源：** https://docs.avaloniaui.net/docs/reference/built-in-data-binding-converters

Avalonia 內建常用轉換器：`!` 否定運算子（反轉 Boolean）、`StringConverters.IsNullOrEmpty`、`StringConverters.IsNotNullOrEmpty`、`ObjectConverters.IsNull`、`ObjectConverters.IsNotNull`、`BoolConverters.And`（MultiBinding 全 true 才 true）、`BoolConverters.Or`（MultiBinding 任一 true 就 true）。

### 重點程式碼範例（如有）
```xml
<!-- 否定運算子 -->
<TextBlock IsVisible="{Binding !AllowInput}">Input is not allowed</TextBlock>
<!-- 集合為空時顯示 -->
<TextBlock IsVisible="{Binding !Items.Count}">No results found</TextBlock>
<!-- StringConverters -->
<TextBlock IsVisible="{Binding MyText,
    Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>
<!-- MultiBinding -->
<TextBlock.IsVisible>
  <MultiBinding Converter="{x:Static BoolConverters.And}">
    <Binding Path="MyText" Converter="{x:Static StringConverters.IsNotNullOrEmpty}"/>
    <Binding Path="IsVisible"/>
  </MultiBinding>
</TextBlock.IsVisible>
```

---

## AutoCompleteBox

**來源：** https://docs.avaloniaui.net/docs/reference/controls/autocompletebox

`AutoCompleteBox` 提供文字輸入框及下拉清單，在使用者輸入時顯示符合項目。支援 `StartsWith`、`Contains`、`Equals` 等篩選模式（可區分大小寫/Ordinal）。複雜物件使用 `ValueMemberBinding` 指定顯示欄位，或用 `ItemFilter` 實作自訂篩選邏輯。

### 重點程式碼範例（如有）
```xml
<AutoCompleteBox x:Name="animals" FilterMode="StartsWith" />
```
```csharp
animals.ItemsSource = new string[]
    {"cat", "camel", "cow", "mouse", "lion", "zebra"}.OrderBy(x=>x);
// 自訂物件篩選
animals.ItemFilter = (search, item) =>
    item is Product p && p.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
```

---

## Border

**來源：** https://docs.avaloniaui.net/docs/reference/controls/border

`Border` 為子控件加上邊框和背景，支援圓角。`CornerRadius` 可指定單一值（四角相同）或 `Top Bottom` 格式（兩個值）或 `TopLeft TopRight BottomRight BottomLeft`（四個值，不允許三個）。`BoxShadow` 設定陰影（支援 inset、offset-x、offset-y、blur-radius、spread-radius、color）。

### 重點程式碼範例（如有）
```xml
<Border Background="Gainsboro" BorderBrush="Black"
        BorderThickness="2" CornerRadius="3"
        BoxShadow="5 5 10 0 DarkGray" Padding="10" Margin="40">
  <TextBlock>Box with a drop shadow</TextBlock>
</Border>
```

---

## Button

**來源：** https://docs.avaloniaui.net/docs/reference/controls/buttons/button

`Button` 響應指標操作。判斷點擊應使用 `Click` 事件而非 `PointerPressed`（Button 內部已處理 PointerPressed，外部不會收到）。透過 `Command` 屬性綁定 `ICommand`。

### 重點程式碼範例（如有）
```xml
<Button Click="ClickHandler">Press Me!</Button>
```
```csharp
public void ClickHandler(object sender, RoutedEventArgs args)
{
    message.Text = "Button clicked!";
}
```

---

## ButtonSpinner

**來源：** https://docs.avaloniaui.net/docs/reference/controls/buttons/buttonspinner

`ButtonSpinner` 提供上/下旋轉按鈕的容器控件，內容可自訂（需自行實作行為邏輯）。`ButtonSpinnerLocation` 指定按鈕位置（Left/Right），`ValidSpinDirection` 限制旋轉方向。

---

## RadioButton

**來源：** https://docs.avaloniaui.net/docs/reference/controls/buttons/radiobutton

`RadioButton` 在同一 `GroupName` 的一組選項中只能選一個。選中後使用者無法透過 UI 取消選取（但可以選其他選項）。

### 重點程式碼範例（如有）
```xml
<RadioButton GroupName="Group1" Content="Option 1"/>
<RadioButton GroupName="Group1" Content="Option 2"/>
```

---

## RepeatButton

**來源：** https://docs.avaloniaui.net/docs/reference/controls/buttons/repeatbutton

`RepeatButton` 在按住時持續產生 Click 事件。`Delay` 指定開始重複前等待時間（預設 300ms），`Interval` 指定每次間隔（預設 100ms）。

---

## SplitButton

**來源：** https://docs.avaloniaui.net/docs/reference/controls/buttons/splitbutton

`SplitButton` 結合主要 Button 和次要 Flyout。主要部分執行最常用操作，次要部分開啟 `Flyout` 選擇其他相關操作。所有操作（主要或次要）都應立即執行。

### 重點程式碼範例（如有）
```xml
<SplitButton Content="Export">
    <SplitButton.Flyout>
        <MenuFlyout Placement="Bottom">
            <MenuItem Header="Export as CSV"/>
            <MenuItem Header="Export as PDF"/>
        </MenuFlyout>
    </SplitButton.Flyout>
</SplitButton>
```

---

## ToggleButton

**來源：** https://docs.avaloniaui.net/docs/reference/controls/buttons/togglebutton

`ToggleButton` 以 Boolean 值表示狀態。透過偽類別 `:checked` 切換不同外觀。常用於音效靜音、格式切換等情境。

### 重點程式碼範例（如有）
```xml
<ToggleButton IsChecked="True">
  <Panel>
    <PathIcon Classes="audio-on" Data="..."/>
    <PathIcon Classes="audio-mute" Data="..."/>
  </Panel>
</ToggleButton>
```
```xml
<Style Selector="ToggleButton PathIcon.audio-on">
    <Setter Property="IsVisible" Value="False"/>
</Style>
<Style Selector="ToggleButton:checked PathIcon.audio-on">
    <Setter Property="IsVisible" Value="True"/>
</Style>
```

---

## ToggleSplitButton

**來源：** https://docs.avaloniaui.net/docs/reference/controls/buttons/togglesplitbutton

`ToggleSplitButton` 結合 ToggleButton 和 SplitButton 功能。只有兩個狀態（checked/unchecked），不支援 Indeterminate。主要按鈕切換功能開關，次要部分選擇該功能的配置。Flyout 中的選項應立即套用，並開啟功能（但永遠不應在 Flyout 中關閉功能）。

---

## CalendarDatePicker

**來源：** https://docs.avaloniaui.net/docs/reference/controls/calendar/calendar-date-picker

擴展 Calendar 控件，加入文字框和按鈕。點擊按鈕顯示日曆選擇器，選好日期後顯示在文字框中。支援多種日期格式輸入。

---

## Canvas

**來源：** https://docs.avaloniaui.net/docs/reference/controls/canvas

`Canvas` 以絕對座標定位子控件。**注意：必須設定子控件的寬高，否則不會顯示。** 不建議隨意使用（UI 不會自適應視窗大小調整）。

| 附加屬性 | 說明 |
|---|---|
| `Canvas.Left` | 距左邊距離 |
| `Canvas.Top` | 距上邊距離 |
| `Canvas.Right` | 距右邊距離 |
| `Canvas.Bottom` | 距下邊距離 |
| `Canvas.ZIndex` | 繪製順序 |

---

## Carousel

**來源：** https://docs.avaloniaui.net/docs/reference/controls/carousel

`Carousel` 依序顯示集合中的每個項目（全頁），可製作幻燈片效果。透過 `Next()` 和 `Previous()` 方法切換，`PageTransition` 設定切換動畫。

### 重點程式碼範例（如有）
```xml
<Carousel Name="slides">
  <Carousel.PageTransition>
    <CompositePageTransition>
      <PageSlide Duration="0:00:01.500" Orientation="Horizontal"/>
    </CompositePageTransition>
  </Carousel.PageTransition>
  <Carousel.Items>
    <Image Source="avares://App/Assets/img1.jpg"/>
    <Image Source="avares://App/Assets/img2.jpg"/>
  </Carousel.Items>
</Carousel>
```
```csharp
slides.Next();    // 下一頁
slides.Previous(); // 上一頁
```

---

## CheckBox

**來源：** https://docs.avaloniaui.net/docs/reference/controls/checkbox

`CheckBox` 表示 Boolean 值（勾選=true，空=false）。可設定為三態（nullable Boolean），null 值顯示灰色方塊代表「未知」。點擊順序：checked → unchecked → unknown（三態時）。

---

## ColorView

**來源：** https://docs.avaloniaui.net/docs/reference/controls/colorpicker/colorview

完整顏色選擇器，提供三個子視圖：Spectrum（色譜）、Components（分量滑桿）和 Palette（調色盤）。建議使用 `HsvColor` 屬性（避免精度損失），`Color` 屬性用於 RGB 模型讀寫。

---

## ComboBox

**來源：** https://docs.avaloniaui.net/docs/reference/controls/combobox

`ComboBox` 顯示選中項目及下拉選單。支援複雜內容組合、資料綁定和 `ItemTemplate`。`MaxDropDownHeight` 限制下拉清單高度。

### 重點程式碼範例（如有）
```xml
<ComboBox SelectedIndex="0" MaxDropDownHeight="300"
          ItemsSource="{Binding FontFamilies}">
  <ComboBox.ItemTemplate>
    <DataTemplate>
      <TextBlock Text="{Binding Name}" FontFamily="{Binding}"/>
    </DataTemplate>
  </ComboBox.ItemTemplate>
</ComboBox>
```

---

## ContentControl

**來源：** https://docs.avaloniaui.net/docs/reference/controls/contentcontrol

`ContentControl` 顯示 `Content` 屬性的內容。配合資料綁定和 `ContentTemplate`/`DataTemplate` 可靈活顯示複雜物件。

### 重點程式碼範例（如有）
```xml
<ContentControl Content="{Binding Content}">
  <ContentControl.ContentTemplate>
    <DataTemplate>
      <Grid ColumnDefinitions="Auto,Auto">
        <TextBlock Grid.Column="0">First Name:</TextBlock>
        <TextBlock Grid.Column="1" Text="{Binding FirstName}"/>
      </Grid>
    </DataTemplate>
  </ContentControl.ContentTemplate>
</ContentControl>
```

---

## ContextMenu

**來源：** https://docs.avaloniaui.net/docs/reference/controls/contextmenu

`ContextMenu` 透過附加屬性附加到宿主控件，實現右鍵選單。也可使用 `ContextFlyout` 提供更豐富的 UI（兩者不可同時使用在同一控件上）。

### 重點程式碼範例（如有）
```xml
<TextBox AcceptsReturn="True">
  <TextBox.ContextMenu>
    <ContextMenu>
      <MenuItem Header="Copy"/>
      <MenuItem Header="Paste"/>
    </ContextMenu>
  </TextBox.ContextMenu>
</TextBox>
```

---

## DataGrid

**來源：** https://docs.avaloniaui.net/docs/reference/controls/datagrid/

`DataGrid` 在可自訂網格中顯示重複資料，需綁定 ObservableCollection。需要額外安裝 `Avalonia.Controls.DataGrid` NuGet 套件並在應用樣式中引用其樣式。

---

## DatePicker

**來源：** https://docs.avaloniaui.net/docs/reference/controls/datepicker

`DatePicker` 有三個旋轉控件讓使用者選擇日期。日期屬性無法直接在 XAML 屬性中設定（無字串到 DateTime 轉換），需在程式碼中設定：
```csharp
datePicker.SelectedDate = new DateTimeOffset(new DateTime(1950, 1, 1));
```

---

## Expander

**來源：** https://docs.avaloniaui.net/docs/reference/controls/expander

`Expander` 有標頭區域（始終可見）和可折疊的內容區域（只能包含一個子控件）。

### 重點程式碼範例（如有）
```xml
<Expander>
    <Expander.Header>Hidden Search</Expander.Header>
    <Grid RowDefinitions="*,*" ColumnDefinitions="150,*">
        <TextBox Grid.Row="0" Grid.Column="1" Watermark="Search text"/>
        <CheckBox Grid.Row="1" Grid.Column="1"/>
    </Grid>
</Expander>
```

---

## Flyout

**來源：** https://docs.avaloniaui.net/docs/reference/controls/flyouts

Flyout 是可解除的浮動容器，非控件本身。只有 `Button` 和 `SplitButton` 支援 `Flyout` 屬性；其他控件使用 `FlyoutBase.AttachedFlyout`（需從程式碼手動顯示）。可定義在資源中共用。透過 `FlyoutPresenterClasses` 自訂外觀。

### 重點程式碼範例（如有）
```xml
<Button Content="Click">
  <Button.Flyout>
    <Flyout>This is the flyout.</Flyout>
  </Button.Flyout>
</Button>

<!-- 共用 Flyout -->
<Window.Resources>
    <Flyout x:Key="SharedFlyout"><!-- content --></Flyout>
</Window.Resources>
<Button Flyout="{StaticResource SharedFlyout}"/>
```
```csharp
// 手動顯示 AttachedFlyout
FlyoutBase.ShowAttachedFlyout(control);
```

---

## Grid

**來源：** https://docs.avaloniaui.net/docs/reference/controls/grid/

`Grid` 以列和行排列子控件。大小定義：絕對（整數 px）、比例（`*`，可加倍數如 `2*`）、自動（`Auto`）。可混用。省略座標的子控件全部堆疊在左上角（column=0, row=0）。

### 重點程式碼範例（如有）
```xml
<Grid ColumnDefinitions="100,1.5*,4*" RowDefinitions="Auto,Auto,Auto">
  <TextBlock Grid.Row="0" Grid.Column="0" Text="Label:"/>
  <CheckBox Grid.Row="0" Grid.Column="2" Content="Check"/>
  <!-- 跨越多格 -->
  <Button Grid.Row="1" Grid.Column="1" Grid.RowSpan="2" Grid.ColumnSpan="2"
          Content="Spans 2x2"/>
</Grid>
```

---

## GridSplitter

**來源：** https://docs.avaloniaui.net/docs/reference/controls/gridsplitter

`GridSplitter` 讓使用者在執行時調整 Grid 欄寬或列高。**重要：`ResizeDirection` 必須與位置方向對應（欄分割器用 Columns，列分割器用 Rows）。**

### 重點程式碼範例（如有）
```xml
<Grid ColumnDefinitions="*, 4, *">
    <Rectangle Grid.Column="0" Fill="Blue"/>
    <GridSplitter Grid.Column="1" Background="Black" ResizeDirection="Columns"/>
    <Rectangle Grid.Column="2" Fill="Red"/>
</Grid>
```

---

## Image

**來源：** https://docs.avaloniaui.net/docs/reference/controls/image

`Image` 顯示點陣圖片，來源可為應用資源、綁定或記憶體串流。`Stretch` 控制縮放（Uniform、UniformToFill 等），`BlendMode` 設定混合模式（Multiply、Screen 等）。

### 重點程式碼範例（如有）
```xml
<Image Height="200" Width="200"
       Source="avares://App/Assets/photo.jpg"/>
<!-- 混合模式疊加 -->
<Panel>
    <Image Source="./Cat.jpg"/>
    <Image Source="./Overlay.png" BlendMode="Multiply"/>
</Panel>
```

---

## ListBox

**來源：** https://docs.avaloniaui.net/docs/reference/controls/listbox

`ListBox` 多行顯示項目，支援單/多選。高度不設定時自動擴展。使用 `ItemTemplate` 自訂項目外觀，`SelectionMode` 控制選取行為（Single/Multiple/Toggle/AlwaysSelected 可組合）。

### 重點程式碼範例（如有）
```xml
<ListBox SelectionMode="Multiple,Toggle" x:Name="animals">
  <ListBox.ItemTemplate>
    <DataTemplate>
      <Border BorderBrush="Blue" CornerRadius="4" Padding="4">
        <TextBlock Text="{Binding}"/>
      </Border>
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

---

## Menu

**來源：** https://docs.avaloniaui.net/docs/reference/controls/menu

`Menu` 通常配合 `DockPanel.Dock="Top"` 放在視窗頂部。巢狀 `<MenuItem>` 定義選單結構；`Header` 中的 `_` 前置字元定義加速鍵；`Command` 綁定命令；`<MenuItem.Icon>` 添加圖示；`<Separator/>` 添加分隔線。

### 重點程式碼範例（如有）
```xml
<DockPanel>
  <Menu DockPanel.Dock="Top">
    <MenuItem Header="_File">
      <MenuItem Header="_Open..." Command="{Binding OpenCommand}"/>
      <Separator/>
      <MenuItem Header="_Exit"/>
    </MenuItem>
  </Menu>
</DockPanel>
```

---

## NumericUpDown

**來源：** https://docs.avaloniaui.net/docs/reference/controls/numericupdown

帶上/下旋轉按鈕的數字輸入控件，忽略非數字輸入。支援鍵盤方向鍵和滑鼠滾輪調整。`Value` 為可空 decimal，可設定自訂增減量和範圍。

---

## ProgressBar

**來源：** https://docs.avaloniaui.net/docs/reference/controls/progressbar

以比例填充進度條顯示數值，`ShowProgressText` 顯示完成百分比，`ProgressTextFormat` 自訂格式字串（`{0}`=值, `{1}`=Minimum, `{2}`=Maximum, `{3}`=百分比）。

### 重點程式碼範例（如有）
```xml
<ProgressBar Minimum="0" Maximum="100" Value="14" ShowProgressText="True"/>
```

---

## ScrollViewer

**來源：** https://docs.avaloniaui.net/docs/reference/controls/scrollviewer

為超出可視區域的內容提供捲動。**不能放在無限高/寬的容器（如 StackPanel）中**，需設定固定尺寸或使用其他容器。當內嵌可捲動控件達到邊界時，可控制是否讓外層 ScrollViewer 繼續捲動。

---

## Slider

**來源：** https://docs.avaloniaui.net/docs/reference/controls/slider

以滑塊位置表示數值，支援拖曳、鍵盤和點擊調整。預設範圍 0-100。常見用法：控制間綁定顯示當前值，或與 ViewModel 雙向綁定。

### 重點程式碼範例（如有）
```xml
<TextBlock Text="{Binding #slider.Value}"/>
<Slider x:Name="slider" Maximum="100"/>
```

---

## TabControl

**來源：** https://docs.avaloniaui.net/docs/reference/controls/tabcontrol

`TabControl` 將視圖分割為多個標籤頁。每個 `TabItem` 包含 `Header`（顯示在標籤列）和內容（點擊後顯示）。

### 重點程式碼範例（如有）
```xml
<TabControl>
  <TabItem Header="Tab 1">
    <TextBlock>Content 1</TextBlock>
  </TabItem>
  <TabItem Header="Tab 2">
    <TextBlock>Content 2</TextBlock>
  </TabItem>
</TabControl>
```

---

## TextBox

**來源：** https://docs.avaloniaui.net/docs/reference/controls/textbox

鍵盤文字輸入控件，支援單行或多行。常用屬性：`Watermark`（水印）、`PasswordChar`（密碼遮蔽）、`AcceptsReturn`（允許換行）、`TextWrapping`（文字換行）。

### 重點程式碼範例（如有）
```xml
<TextBox Watermark="Enter name"/>
<TextBox PasswordChar="*" Watermark="Password"/>
<TextBox Height="100" AcceptsReturn="True" TextWrapping="Wrap"/>
```

---

## ToolTip

**來源：** https://docs.avaloniaui.net/docs/reference/controls/tooltip

懸停宿主控件時顯示的彈出視窗。`ToolTip.Tip` 元素可提供豐富 UI 內容（非純文字），`ToolTip.Placement` 設定顯示位置。

### 重點程式碼範例（如有）
```xml
<Rectangle ToolTip.Placement="Bottom">
    <ToolTip.Tip>
      <StackPanel>
        <TextBlock FontSize="16">Title</TextBlock>
        <TextBlock>Description here.</TextBlock>
      </StackPanel>
    </ToolTip.Tip>
</Rectangle>
```

---

## Window

**來源：** https://docs.avaloniaui.net/docs/reference/controls/window

頂層 `ContentControl`，通常透過子類別建立各類型視窗。`ShowDialog` 顯示模態對話框（可 await 等待關閉，可傳回結果）；`Hide/Show` 可隱藏後重新顯示（Close 後則不能再 Show）；處理 `Closing` 事件並設 `e.Cancel = true` 可阻止關閉。

### 重點程式碼範例（如有）
```csharp
// 顯示模態對話框並取得結果
var dialog = new MyDialog();
var result = await dialog.ShowDialog<string>(ownerWindow);

// 阻止關閉，改為隱藏
window.Closing += (s, e) =>
{
    ((Window)s).Hide();
    e.Cancel = true;
};
```

---

## 偽類別（Pseudo Classes）

**來源：** https://docs.avaloniaui.net/docs/reference/styles/pseudo-classes

偽類別類似 CSS 偽類別，表示控件特定狀態，按慣例以 `:` 開頭。透過 `PseudoClasses` 屬性（protected）追蹤狀態，只能在繼承類別中設定。

常用偽類別：
- `:disabled` - 控件已停用
- `:pointerover` - 指標在控件上方
- `:focus` - 控件有焦點
- `:focus-within` - 控件或其後代有焦點
- `:focus-visible` - 有焦點且應顯示視覺指示器（鍵盤導航）

### 重點程式碼範例（如有）
```xml
<Style Selector="CheckBox:checked">
    <Setter Property="FontWeight" Value="Bold"/>
</Style>
```
```csharp
[PseudoClasses(":left", ":right", ":middle")]
public class AreaButton : Button
{
    private void SetPseudoclasses(bool left, bool right, bool middle)
    {
        PseudoClasses.Set(":left", left);
        PseudoClasses.Set(":right", right);
        PseudoClasses.Set(":middle", middle);
    }
}
```

---

## 樣式選擇器語法

**來源：** https://docs.avaloniaui.net/docs/reference/styles/style-selector-syntax

Avalonia 樣式選擇器語法（類似 CSS）：

| 語法 | 說明 |
|---|---|
| `Button` | 按控件類別選取（不含衍生類型） |
| `:is(Button)` | 含衍生類型 |
| `#myButton` | 按 Name 屬性（加 `#` 前綴） |
| `Button.large` | 按樣式類別 |
| `Button:focus` | 按偽類別 |
| `StackPanel > Button` | 僅直接子控件（邏輯樹） |
| `StackPanel Button` | 任意後代控件 |
| `Button[IsDefault=true]` | 按屬性值 |
| `TextBlock[(Grid.Row)=0]` | 附加屬性需加括號 |
| `Button /template/ ContentPresenter` | 模板內控件 |
| `TextBlock:not(.h1)` | 排除符合條件 |
| `TextBlock, Button` | OR 選取（逗號分隔） |
| `TextBlock:nth-child(2n+3)` | 按子元素位置（An+B 公式） |

### 重點程式碼範例（如有）
```xml
<!-- 直接子 vs 所有後代 -->
<Style Selector="StackPanel > Button"><!-- 只有直接子 Button --></Style>
<Style Selector="StackPanel Button"><!-- 所有後代 Button --></Style>

<!-- 屬性匹配 -->
<Style Selector="Button[IsDefault=true]">...</Style>
<!-- 附加屬性需括號 -->
<Style Selector="TextBlock[(Grid.Row)=0]">...</Style>

<!-- 組合：有 red 類別、focus 且 pointerover 的 Button -->
<Style Selector="Button.red:focus:pointerover">...</Style>
```

---

## Gestures 手勢識別器索引

**來源：** https://docs.avaloniaui.net/docs/reference/gestures/

Avalonia 手勢識別器參考索引頁面，包含 PinchGestureRecognizer、PullGestureRecognizer、ScrollGestureRecognizer。

---

## PinchGestureRecognizer 縮放手勢識別器

**來源：** https://docs.avaloniaui.net/docs/reference/gestures/pinchgesturerecognizer

`PinchGestureRecognizer` 追蹤兩指縮放手勢（雙指靠近或分開）。透過控制項的 `GestureRecognizers` 屬性附加。觸發 `Gestures.PinchEvent`（開始）和 `Gestures.PinchEndedEvent`（結束）。事件引數中的 `Scale` 屬性包含自手勢開始後的相對縮放比例。

### 重點程式碼範例（如有）
```xml
<Image Source="/image.jpg">
  <Image.GestureRecognizers>
    <PinchGestureRecognizer/>
  </Image.GestureRecognizers>
</Image>
```
```csharp
image.AddHandler(Gestures.PinchEvent, (s, e) => { });
image.AddHandler(Gestures.PinchEndedEvent, (s, e) => { });
```

---

## PullGestureRecognizer 拉取手勢識別器

**來源：** https://docs.avaloniaui.net/docs/reference/gestures/pullgesturerecognizer

`PullGestureRecognizer` 追蹤從控制項邊緣拉取的手勢，`PullDirection` 設定拉取方向（TopToBottom/BottomToTop/LeftToRight/RightToLeft）。觸發 `Gestures.PullGestureEvent` 和 `Gestures.PullGestureEndedEvent`。

### 重點程式碼範例（如有）
```xml
<Border Width="500" Height="500" Name="border">
  <Border.GestureRecognizers>
    <PullGestureRecognizer PullDirection="TopToBottom"/>
  </Border.GestureRecognizers>
</Border>
```
```csharp
border.GestureRecognizers.Add(new PullGestureRecognizer()
{
    PullDirection = PullDirection.TopToBottom,
});
```

---

## ScrollGestureRecognizer 捲動手勢識別器

**來源：** https://docs.avaloniaui.net/docs/reference/gestures/scrollgesturerecognizer

`ScrollGestureRecognizer` 追蹤在控制項範圍內的捲動手勢，特別適合實現平移（panning）互動。使用 `CanHorizontallyScroll` 和 `CanVerticallyScroll` 分別啟用水平/垂直捲動。觸發 `Gestures.ScrollGestureEvent` 和 `Gestures.ScrollGestureEndedEvent`。

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

## Properties 屬性參考索引

**來源：** https://docs.avaloniaui.net/docs/reference/properties/

Avalonia 控制項屬性的參考資訊，目前包含 TextTrimming。

---

## TextTrimming 文字截斷

**來源：** https://docs.avaloniaui.net/docs/reference/properties/texttrimming

`TextTrimming` 屬性控制文字超出可用空間時的顯示方式（以省略符號 `…` 表示截斷）。適用於 TextBlock、SelectableTextBlock、ContentPresenter。

**五種截斷模式：**
- `None`：直接截斷，無省略符號
- `CharacterEllipsis`：字元後截斷（一般用途）
- `WordEllipsis`：完整單字後截斷（最大化可讀性）
- `PrefixCharacterEllipsis`：中間截斷，保留開頭和結尾（適合路徑/URL）
- `LeadingCharacterEllipsis`：從頭截斷，只顯示結尾（適合只需要末段的路徑）

### 重點程式碼範例（如有）
```xml
<!-- 與 MaxWidth 結合使用 -->
<TextBlock Text="{Binding UserName}"
           MaxWidth="300"
           TextTrimming="CharacterEllipsis" />

<!-- 與 TextWrapping 結合，限制最多 3 行 -->
<TextBlock Text="{Binding Content}"
           Width="300"
           MaxLines="3"
           TextWrapping="Wrap"
           TextTrimming="WordEllipsis" />

<!-- 顯示檔案路徑（保留開頭和結尾） -->
<TextBlock Text="C:\Users\Documents\Projects\source.cs"
           TextTrimming="PrefixCharacterEllipsis"
           Width="200" />
```

---
