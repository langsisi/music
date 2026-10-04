# Avalonia 教學知識庫

此檔案包含 Avalonia 教學相關文件摘要。

---


## 入門教學：溫度轉換器 App

**來源：** https://docs.avaloniaui.net/docs/get-started/starter-tutorial/

透過建立一個溫度轉換器 App 學習 Avalonia 基礎概念，包含：加入控制項、版面配置、視窗自訂、事件處理、資料轉換。

`.axaml` 是 Avalonia XAML 的副檔名（Avalonia eXtensible Application Markup Language）。

---

## 加入控制項

**來源：** https://docs.avaloniaui.net/docs/get-started/starter-tutorial/adding-a-control

控制項（Control）是允許與 App 互動的 UI 元素，如按鈕、滑桿、核取方塊、選單等。

### 重點程式碼範例（如有）
```xml
<!-- 加入按鈕（置中對齊） -->
<Button HorizontalAlignment="Center">Calculate</Button>
```

控制項使用 XML 屬性定義外觀和行為，`HorizontalAlignment` 預設值為 `Left`。

---

## 加入版面配置

**來源：** https://docs.avaloniaui.net/docs/get-started/starter-tutorial/adding-some-layout

每個 Avalonia 視窗只允許一個控制項在其內容區域，若要放多個元素需使用**版面配置控制項（Layout Control）**。

### 重點程式碼範例（如有）
```xml
<StackPanel>
    <Border Margin="5" CornerRadius="10" Background="LightBlue">
        <TextBlock Margin="5" FontSize="24" HorizontalAlignment="Center"
                   Text="Temperature Converter"/>
    </Border>
    <Grid ShowGridLines="True" Margin="5"
          ColumnDefinitions="120, 100"
          RowDefinitions="Auto, Auto">
        <TextBlock Grid.Row="0" Grid.Column="0" Margin="10">Celsius</TextBlock>
        <TextBox Grid.Row="0" Grid.Column="1" Margin="0 5" Text="0"/>
        <TextBlock Grid.Row="1" Grid.Column="0" Margin="10">Fahrenheit</TextBlock>
        <TextBox Grid.Row="1" Grid.Column="1" Margin="0 5" Text="0"/>
    </Grid>
    <Button HorizontalAlignment="Center">Calculate</Button>
</StackPanel>
```

`StackPanel`：垂直（預設）或水平排列元素（`Orientation="Horizontal"`）
`Grid`：使用欄列定義的格子佈局，`Grid.Row`/`Grid.Column` 指定儲存格（從 0 開始）

---

## 自訂 Avalonia 視窗

**來源：** https://docs.avaloniaui.net/docs/get-started/starter-tutorial/customizing-the-avalonia-window

Avalonia 視窗有四個版面配置區域：Margin、Border、Padding、Content。

### 重點程式碼範例（如有）
```xml
<Window xmlns="https://github.com/avaloniaui"
        mc:Ignorable="d" d:DesignWidth="400" d:DesignHeight="450"
        x:Class="GetStartedApp.Views.MainWindow"
        Title="GetStartedApp"
        Width="400"
        Height="450">
```

- `d:DesignWidth`/`d:DesignHeight`：預覽器尺寸（不影響執行時）
- `Width`/`Height`：執行時視窗實際尺寸

---

## 建立事件處理

**來源：** https://docs.avaloniaui.net/docs/get-started/starter-tutorial/establishing-events-and-responses

XAML 可與 C# code-behind 關聯，用於事件處理。

### 重點程式碼範例（如有）
```csharp
// MainWindow.axaml.cs
using Avalonia.Interactivity;
using System.Diagnostics;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }
    
    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        Debug.WriteLine("Click!");
    }
}
```

```xml
<!-- MainWindow.axaml 中連結事件 -->
<Button HorizontalAlignment="Center" Click="Button_OnClick">Calculate</Button>
```

---

## 資料轉換（完成溫度轉換 App）

**來源：** https://docs.avaloniaui.net/docs/get-started/starter-tutorial/converting-data

使用 `Name` 屬性為控制項命名，以便在 code-behind 中存取。

### 重點程式碼範例（如有）
```xml
<TextBox Grid.Row="0" Grid.Column="1" Margin="0 5" Text="0" Name="Celsius"/>
<TextBox Grid.Row="1" Grid.Column="1" Margin="0 5" Text="0" Name="Fahrenheit"/>
```

```csharp
private void Button_OnClick(object? sender, RoutedEventArgs e)
{
    if (double.TryParse(Celsius.Text, out double C))
    {
        var F = C * (9d / 5d) + 32;
        Fahrenheit.Text = F.ToString("0.0");
    }
    else
    {
        Celsius.Text = "0";
        Fahrenheit.Text = "0";
    }
}
```

---

## 練習題

**來源：** https://docs.avaloniaui.net/docs/get-started/starter-tutorial/exercises

1. **★**：隱藏 Grid 格線（`ShowGridLines="False"`）
2. **★★**：設 Fahrenheit TextBox 為唯讀（`IsReadOnly="True"`）
3. **★★★**：改為即時轉換（使用 `TextChanged` 事件取代 Button Click）

```csharp
// 即時轉換 TextChanged 處理器
private void Celsius_TextChanged(object? sender, RoutedEventArgs e)
{
    if (string.IsNullOrEmpty(Celsius.Text) || Celsius.Text == "-")
    {
        Fahrenheit.Text = "";
    }
    else if (double.TryParse(Celsius.Text, out double C))
    {
        var F = C * (9d / 5d) + 32;
        Fahrenheit.Text = F.ToString("0.0");
    }
    else
    {
        Celsius.Text = "0";
        Fahrenheit.Text = "0";
    }
}
```

---

## Tutorials 教學索引

**來源：** https://docs.avaloniaui.net/docs/tutorials/

Avalonia 實作教學：
- **To Do List App**：使用 MVVM 模式，涵蓋 Bindings、Commands、基本 Styling 和 I/O
- **Music Store App**：JetBrains 網路研討會展示，高度圖形化的 MVVM 應用，包含對話框、圖像和集合資料顯示、資料持久化

---

## GroupBox 教學（自訂 HeaderedContentControl）

**來源：** https://docs.avaloniaui.net/docs/tutorials/groupbox

展示如何透過 `HeaderedContentControl` 的控制項主題自訂樣式，模擬 WPF 的 GroupBox 效果。使用 Grid 佈局：第一行顯示標題（Border + TextBlock），標題下方覆蓋邊框（Grid.RowSpan），內容放在 ContentPresenter 中。

### 重點程式碼範例（如有）
```xml
<Style Selector="HeaderedContentControl">
    <Setter Property="Template">
        <ControlTemplate>
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <!-- Header -->
                <Border ZIndex="1"
                        Background="{DynamicResource SystemControlBackgroundAltHighBrush}"
                        Padding="5,0,5,0" Margin="5,0,0,0">
                    <TextBlock Text="{TemplateBinding Header}" FontWeight="Bold"/>
                </Border>
                <!-- Content Area -->
                <Border Grid.RowSpan="2" Grid.ColumnSpan="2"
                        CornerRadius="4" Margin="0,10,0,0"
                        BorderBrush="{DynamicResource SystemControlForegroundBaseMediumBrush}"
                        BorderThickness="1">
                    <ContentPresenter Name="PART_ContentPresenter"
                                      Padding="8" Content="{TemplateBinding Content}"/>
                </Border>
            </Grid>
        </ControlTemplate>
    </Setter>
</Style>
```

---

## Samples 範例索引

**來源：** https://docs.avaloniaui.net/docs/tutorials/samples

官方 Avalonia 範例集合，涵蓋以下類別：

**MVVM：** Basic MVVM、Binding & Converters、Commands、ValueConverter、Data Validation、Dialogs、Dialog Manager

**DataTemplates：** Basic DataTemplate、FuncDataTemplate、IDataTemplate

**Controls, Styles & Drawing：** Customized Button、Making Lists、Native Menus、Splash Screen、Rect Painter、Image Loading、Using Google Fonts、BattleCity（2D 遊戲）

**Custom Controls：** Custom Rating Control（自訂評分控制項）、Custom Snowflakes Control（覆寫 OnRender）

**Miscellaneous：** Clipboard Operations、Drag and Drop、Native File Dialogs、Basic Localization、Basic ViewLocator、Native AOT

**Automated UI Testing：** Headless Testing with XUnit/NUnit、Testing with Appium

所有範例原始碼在 [Avalonia.Samples GitHub](https://github.com/AvaloniaUI/Avalonia.Samples) 和 [AvaloniaUI.QuickGuides GitHub](https://github.com/AvaloniaUI/AvaloniaUI.QuickGuides)。

---
