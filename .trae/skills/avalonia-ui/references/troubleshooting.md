# Avalonia 疑難排解知識庫

此檔案包含 Avalonia 疑難排解相關文件摘要。

---

## 樣式疑難排解 (Styling Troubleshooting)

**來源：** https://docs.avaloniaui.net/docs/basics/user-interface/styling/troubleshooting

常見問題與解決方案：

1. **找不到 Control Theme**：確保 StyleKey 與 ControlTheme 的 `x:Key` 和 `TargetType` 相符
2. **Control Theme 影響其他控制項**：針對某類型建立的樣式會套用到所有該類型的控制項（包括複合控制項內部的子控制項）。例如針對 `TextBlock` 的樣式會影響 `ListBox` 內部的 TextBlock
3. **視窗透明或沒有內容**：確認已安裝並引入 Avalonia 主題（Fluent 或 Simple）

---

## FAQ - 常見問題解答

**來源：** https://docs.avaloniaui.net/docs/faq

**Avalonia 是什麼？**
Avalonia 是開源、跨平台的 .NET UI 框架，支援 Windows、Linux、macOS、iOS、Android 和 WebAssembly。使用 C#/F# 等 .NET 語言和 XAML 標記語言，採用自行繪製 UI（不依賴原生 OS 控制項），確保跨平台一致性。

**與 WPF/MAUI 的差異？**
- 跨平台設計（WPF 僅支援 Windows）
- 獨立渲染引擎（MAUI 使用原生控制項，Avalonia 全部自繪）
- 靈活樣式系統（類似 WPF+CSS，可動態調整）
- 開源社群驅動

**支援哪些 .NET 版本？**
- .NET Framework 4.6.2+
- .NET Core 2.0+
- .NET 5+（含最新 .NET 10）

**可以用程式碼取代 XAML 嗎？**
可以，可以完全用 C# 等 .NET 語言撰寫 UI。

**支援 Hot Reload 嗎？**
可透過社群專案 [HotAvalonia](https://github.com/Kira-NT/HotAvalonia) 實現 Hot Reload。

**可以使用 WPF/UWP 知識嗎？**
是的！Avalonia 深受 WPF/UWP 影響，概念相通（XAML、資料綁定、MVVM），但有一些獨特功能和差異。

**支援跨編譯嗎？**
可以在 Windows 上為 macOS、Linux、Android 和 WebAssembly 進行交叉編譯，但 iOS 需要 Mac 環境。

**是否有拖放視覺設計器？**
正在開發中（預計 2025 年），將作為 Avalonia Accelerate（付費）的一部分提供。

---


---

## 來自 AvaloniaBook

> 以下內容來源：https://wieslawsoltes.github.io/AvaloniaBook/
### Chapter24 — Performance Diagnostics & Troubleshooting

#### 關鍵概念

**基本原則**
- **必須在 Release build 測量**（`dotnet run -c Release`），JIT 優化影響顯著
- 一次改一個變數，重新測量確認效果
- 用小型重現案例隔離問題控件/視圖

**DevTools (F12)**
- Visual Tree / Logical Tree：檢查層級、屬性、偽類
- Layout Explorer：measure/arrange 資訊
- Events：事件流偵測
- Styles & Resources：樣式/偽類狀態

**Debug Overlays (`RendererDebugOverlays`)**
- `Fps`：每秒幀數
- `DirtyRects`：每幀重繪區域（大 dirty rect = 找出觸發全視窗重繪的元素）
- `LayoutTimeGraph`：Layout 時間（尖峰 = 高成本 measure/arrange）
- `RenderTimeGraph`：渲染時間（尖峰 = 昂貴繪製）

**常見效能問題與修復**

| 問題 | 修復 |
|---|---|
| 長列表卡頓 | 使用 `VirtualizingStackPanel` |
| Binding 風暴 | `LogArea.Binding` 日誌找出來源，加 debounce |
| 大圖片模糊/慢 | 設 `RenderOptions.BitmapInterpolationMode` |
| Layout 顫抖 | 減少觸發大樹重排的屬性變更 |
| 重度 CPU 背景 | 移至 `Task.Run`，用 `IProgress<T>` 回報 |

#### 代碼範例

```csharp
// 開啟 DevTools
public override void OnFrameworkInitializationCompleted()
{
    // ...設定視窗/根視圖...
    this.AttachDevTools();
    base.OnFrameworkInitializationCompleted();
}
```

```csharp
// 程式化啟用 Overlay
if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
{
    desktop.MainWindow.AttachedToVisualTree += (_, __) =>
    {
        if (desktop.MainWindow?.Renderer is { } renderer)
            renderer.DebugOverlays = RendererDebugOverlays.Fps | RendererDebugOverlays.DirtyRects;
    };
}
```

```csharp
// 針對性 Log
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .LogToTrace(LogEventLevel.Information,
        new[] { LogArea.Binding, LogArea.Layout, LogArea.Render, LogArea.Property })
    .StartWithClassicDesktopLifetime(args);
```

```csharp
// 監聽 SceneInvalidated
using System.Diagnostics;

if (TopLevel is { Renderer: { } renderer })
{
    renderer.SceneInvalidated += (_, e) =>
    {
        Debug.WriteLine($"Invalidated {e.Rect}");
    };
}
```

**效能工作流程**
1. Release + 關閉 log → 建立 baseline
2. 開啟 DevTools overlays（FPS、dirty rects、時間圖）→ 找出模式
3. 開啟目標性 log（Binding/Layout/Render）→ 與 overlay 對照
4. 套用修復（virtualization、caching、減少 layout churn）
5. 重新測量確認改善

---