# Music 增加 iOS 端（head 工程）实施方案

## 一、摘要

按项目现有的 **head 工程模式**（`src/Music` 平台无关 Core + `src/Music.Desktop` / `src/Music.Android` 薄接入层）新增 `src/Music.iOS`：

- TFM `net10.0-ios`，引用 `Avalonia.iOS` 12.1.3 与 `..\Music\Music.csproj`。
- 用 **AVFoundation / AVPlayer** 实现 `IAudioPlayer`，在 `AppHost.Configure` 中注册以**覆盖** Core 默认的 `VlcAudioPlayer`（iOS 上不使用、也不引入 libvlc）。
- 通过环境变量 `MUSIC_APP_DATA_DIR` 把 `AppPaths.Root` 指向沙盒 `Documents/MusicData`，使数据库、缓存、下载文件都在「文件共享」可见范围内。
- Core 仅做两处最小改动（包版本、升级按钮可见性）。
- **不改 `Music.slnx`**（避免在无 iOS workload 的 Windows 上执行 `dotnet build Music.slnx` 整体失败）。

> **环境前提（已与用户确认）**：开发机只有 Windows，无 Mac。iOS 工程在本机只能"写完 + 保证不破坏现有三端构建"，**真正的编译/运行验证必须在 macOS + Xcode 上完成**。

---

## 二、现状分析（已核实的代码事实）

| 事实 | 出处 |
| --- | --- |
| 三个工程：Core `net10.0`（无平台 API）、Desktop、Android | [Music.slnx](file:///e:/Code/Avalonia/Music/Music.slnx)、[Music.csproj](file:///e:/Code/Avalonia/Music/src/Music/Music.csproj) |
| head 通过 `AppHost.Configure(...)` 注册平台实现，**后注册者覆盖 Core 默认** | [AppHost.cs](file:///e:/Code/Avalonia/Music/src/Music/AppHost.cs#L25-L36) |
| Core 默认注册 `IAudioPlayer → VlcAudioPlayer`、`IUpdateInstaller → UnsupportedUpdateInstaller`、`ISystemMediaService → NoopSystemMediaService` | [ServiceCollectionExtensions.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/ServiceCollectionExtensions.cs#L44-L69) |
| 平台覆盖写法沿用 `services.AddSingleton<IXxx, Impl>()`（不要求 Replace） | [Application.cs（Android）](file:///e:/Code/Avalonia/Music/src/Music.Android/Application.cs#L39-L45)、[Program.cs（Desktop）](file:///e:/Code/Avalonia/Music/src/Music.Desktop/Program.cs#L18-L27) |
| `IAudioPlayer` 契约：**方法只在 UI 线程调用**，事件允许来自播放器内部线程 | [IAudioPlayer.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Audio/IAudioPlayer.cs#L5-L8) |
| 取流链路：**本地**直接给路径；**FTP** 先下载到缓存再给本地路径；**HTTP（Navidrome）** 给 http(s) URL | [CachedMediaResolver.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Media/CachedMediaResolver.cs#L23-L50) |
| 数据目录可由 `MUSIC_APP_DATA_DIR` 环境变量整体覆盖（静态 `AppPaths.Root`） | [AppPaths.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/AppPaths.cs#L13-L58) |
| 已有 `ISingleViewApplicationLifetime` 分支（iOS 走这条） | [App.axaml.cs](file:///e:/Code/Avalonia/Music/src/Music/App.axaml.cs#L46-L52) |
| 升级：设置页「下载并安装」按钮 `IsVisible="{Binding HasPendingUpdate}"`；`CanSelfUpdate`（= `IUpdateInstaller.CanSelfUpdate`）已存在但只用于底部提示文案 | [SettingsView.axaml](file:///e:/Code/Avalonia/Music/src/Music/Views/Pages/SettingsView.axaml#L312-L338)、[SettingsViewModel.cs](file:///e:/Code/Avalonia/Music/src/Music/ViewModels/Pages/SettingsViewModel.cs#L215-L216) |
| 包版本集中管理 | [Directory.Packages.props](file:///e:/Code/Avalonia/Music/Directory.Packages.props) |

**关键推论（决定 AVPlayer 可行）**：iOS 上 `IAudioPlayer.Load()` 实际只会收到 *本地文件路径* 或 *http(s) URL*，不会收到 `ftp://`（FTP 已被 `CachedMediaResolver` 提前落盘）。AVPlayer 原生支持这两类，因此**无需引入任何 libvlc**。

---

## 三、方案决策（已定，不再二选一）

1. **音频后端**：AVFoundation `AVPlayer`（用户已确认"系统原生 AVPlayer"）。
2. **工程形态**：独立 head 工程 `src/Music.iOS`，不把 iOS TFM 塞进 Core。
3. **AudioSession**：类别 `Playback`（支持后台播放、不受静音开关影响），`Info.plist` 声明 `UIBackgroundModes = [audio]`。
4. **Phase 1 范围**：能启动、能扫描/播放本地音乐、能连 Navidrome 播放、能后台播放、不支持自更新时 UI 正确降级。**不做**锁屏控制中心/耳机线控（`MPNowPlayingInfoCenter` + `MPRemoteCommandCenter`）、不做 CarPlay、不做 App Store 上架配置——这些留 Phase 2。
5. **不引入** `VideoLAN.LibVLC.iOS`。
6. **不改 `Music.slnx`**。

---

## 四、改动清单

### 4.1 新增工程 `src/Music.iOS/Music.iOS.csproj`

**做什么**：新建 iOS head 工程文件。

**为什么**：head 模式要求平台宿主是独立可执行工程；`net10.0-ios` 的 SDK 来自 `dotnet workload`，不能污染 Core。

**怎么做**：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-ios</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>

    <!-- 版本号与 Core / Android 对齐；发布时由发布脚本用 -p:Version 覆盖 -->
    <Version>1.0.0</Version>
    <ApplicationTitle>ZMusic</ApplicationTitle>
    <ApplicationId>zhusl.music</ApplicationId>
    <ApplicationDisplayVersion>1.0.0</ApplicationDisplayVersion>
    <ApplicationVersion>1</ApplicationVersion>

    <!-- 刻意不设置 RuntimeIdentifier / RuntimeIdentifiers：
         设置了会在未指定模拟器 RID 时解析不到资产，且会让 dotnet build 默认按真机 RID 出包，
         模拟器调试（-t:Run）会失败。真机/模拟器 RID 由构建命令用 -p:RuntimeIdentifier 显式指定。 -->

    <!-- 无 Apple 开发者账号时可在 Mac 上临时用 -p:CodesignKey="" 关闭签名做模拟器调试 -->
    <CodesignKey>iPhone Developer</CodesignKey>
    <CodesignProvision></CodesignProvision>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia.iOS" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Music\Music.csproj" />
  </ItemGroup>
</Project>
```

注意：Core 中的 `VlcAudioPlayer` 属于托管代码，只要不被解析（DI 中已被覆盖）就不会触发 `Core.Initialize()`，因此 iOS 不需要 libvlc 原生库。

---

### 4.2 入口与平台配置（新增文件）

#### `src/Music.iOS/Main.cs`

**为什么**：iOS 必须从 `UIApplication.Main` 进入；并且要在**任何**静态初始化读到 `AppPaths.Root` 之前把数据目录改到沙盒 `Documents` 下。

```csharp
using UIKit;

namespace Music.iOS;

public static class Program
{
    private static void Main(string[] args)
    {
        // 必须最先执行：AppPaths.Root 是静态只读，一旦被其他类型触发就改不回来了。
        PlatformPaths.Configure();
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
```

#### `src/Music.iOS/PlatformPaths.cs`

**做什么**：建目录 + 设置环境变量。

**为什么**：iOS 沙盒里只有 `Documents` 能被「文件共享 / 文件 App」看到；把 DB、缓存、下载都放进去，用户才能用自己的方式导入音乐。`Environment.SpecialFolder.MyDocuments` 在 .NET for iOS 上映射到沙盒 `Documents`。

```csharp
using System;
using System.IO;

namespace Music.iOS;

internal static class PlatformPaths
{
    public static string DocumentsDir { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>应用数据根目录（会被 MUSIC_APP_DATA_DIR 指向它）。</summary>
    public static string DataDir { get; } = Path.Combine(DocumentsDir, "MusicData");

    /// <summary>用户放音乐文件、或把文件分享进本应用后的落点。</summary>
    public static string MusicImportDir { get; } = Path.Combine(DocumentsDir, "Music");

    public static void Configure()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(MusicImportDir);

        // AppPaths.ResolveRoot() 优先读这个变量，从而绕开 iOS 上不可见的 Library 目录。
        Environment.SetEnvironmentVariable("MUSIC_APP_DATA_DIR", DataDir);
    }
}
```

#### `src/Music.iOS/AppDelegate.cs`

**为什么**：`AvaloniaAppDelegate<App>` 是 Avalonia.iOS 的标准入口；平台 DI 覆盖必须在这里、`base.CustomizeAppBuilder` 之前完成（`App.Initialize()` 会用到 `AppHost.Services`）。

```csharp
using Avalonia;
using Avalonia.iOS;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Music.Services.Audio;
using Music.Services.SystemMedia;
using Music.Services.Update;

namespace Music.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        AppHost.Configure(services =>
        {
            // 覆盖 Core 里的 VlcAudioPlayer：后注册者生效，libvlc 不会被解析。
            services.AddSingleton<IAudioPlayer, IosAudioPlayer>();
            // iOS 明确不支持应用内自更新（只写了显式注册，便于日后替换成跳 App Store 的实现）。
            services.AddSingleton<IUpdateInstaller, UnsupportedUpdateInstaller>();
            // Phase 1 沿用 Core 的 NoopSystemMediaService；Phase 2 换成 MPNowPlayingInfoCenter 实现。
            services.AddSingleton<ISystemMediaService, NoopSystemMediaService>();
        });

        return base.CustomizeAppBuilder(builder);
    }
}
```

> 说明：`AppDomain.UnhandledException` 崩溃日志照搬 Android 的做法可选加；iOS 上落盘位置用 `PlatformPaths.DataDir/crash.log`，Phase 1 建议加（排查成本低）。

#### `src/Music.iOS/Info.plist`

**做什么**：声明应用标识、启动方式、后台音频、明文网络、本地网络用途、文件共享。

**怎么做**（关键键位说明）：

| 键 | 值 | 原因 |
| --- | --- | --- |
| `CFBundleIdentifier` | `zhusl.music` | 与 Android ApplicationId 保持一致 |
| `CFBundleShortVersionString` / `CFBundleVersion` | `1.0.0` / `1` | 与 csproj 对齐 |
| `LSRequiresIPhoneOS` | `true` | 必须 |
| `MinimumOSVersion` | `15.0` | 与 `SupportedOSPlatformVersion` 一致 |
| `UIDeviceFamily` | `[1,2]` | iPhone + iPad |
| `UILaunchScreen` | 空 `<dict/>` | iOS 14+ 无需 storyboard 文件即可全屏启动 |
| `UISupportedInterfaceOrientations` | Portrait + Landscape | 现有 UI 已做宽屏/紧凑自适应 |
| `UIBackgroundModes` | `[audio]` | **后台播放必需** |
| `NSAppTransportSecurity` → `NSAllowsArbitraryLoads` | `true` | 允许 `http://` 直连 Navidrome / 局域网 FTP |
| `NSLocalNetworkUsageDescription` | 中文说明 | 访问局域网音源时系统会弹权限框，需要这段文案 |
| `UIFileSharingEnabled` | `true` | Documents 出现在「文件」App / Finder 中，便于放音乐 |
| `LSSupportsOpeningDocumentsInPlace` | `true` | 配合上一条 |
| `XSAppIconAssets` | `Assets.xcassets/AppIcon.appiconset` | 图标资源 |

**刻意不加** `UIApplicationSceneManifest`：Avalonia.iOS 的 `AvaloniaAppDelegate` 自己管理 window/scene，加了反而会与 `AvaloniaSceneDelegate` 冲突。

#### `src/Music.iOS/Entitlements.plist`

空 `<dict/>`（Phase 1 不需要任何 capability；后台音频只需 `UIBackgroundModes`，不需要 entitlement）。

#### `src/Music.iOS/Assets.xcassets/AppIcon.appiconset/`

`Contents.json` + 一张 1024×1024 **无 alpha 通道** PNG（可复用 Android 的 `Icon.png` 转制；iOS 拒绝带透明通道的 AppIcon）。

---

### 4.3 `src/Music.iOS/IosAudioPlayer.cs`（AVPlayer 实现 `IAudioPlayer`）

**为什么**：iOS 没有可用的 libvlc；AVPlayer 是系统唯一稳的音频方案。必须严格满足 [IAudioPlayer](file:///e:/Code/Avalonia/Music/src/Music/Services/Audio/IAudioPlayer.cs) 的语义，否则 `PlaybackService` / `PlayerViewModel` 的进度、自动下一曲会失准。

**怎么做——逐成员映射**：

| 成员 | 实现 |
| --- | --- |
| `IsAvailable` | `true`（AVFoundation 一定存在）。构造中配置 `AVAudioSession` 失败也不置 false，只在 `Play()` 时通过 `PlaybackFailed` 上报 |
| `IsPlaying` | `_player?.Rate > 0` |
| `PositionMs` | `(long)(_player?.CurrentTime.Seconds * 1000)`，NaN 防护 |
| `DurationMs` | `_item?.Duration.Seconds` 有效时换算，否则 0 |
| `IsSeekable` | `_item is not null && _item.Status == AVPlayerItemStatus.ReadyToPlay && DurationMs > 0` |
| `Volume` | AVPlayer 是 0..1：`get => (int)(_player.Volume * 100)`，`set => _player.Volume = value / 100f` |
| `IsMuted` | 映射 `_player.Muted` |
| `Load(pathOrUrl)` | **先** `DetachItemObservers()` + `_item?.Dispose()`；`pathOrUrl` 含 `://` 用 `NSUrl.FromString`，否则 `new NSUrl(pathOrUrl, false)`；用 `AVPlayerItem` 承载，`_player.ReplaceCurrentItemWithPlayerItem(_item)`（首播时 `_player = new AVPlayer(_item)`）；挂 KVO/通知（见下）。装载后**不自动播放** |
| `Play()` | `ConfigureAudioSession()`（幂等）+ `_player.Play()` |
| `Pause()` | `_player.Pause()` |
| `Stop()` | `_player.Pause()` + `Seek(0)` |
| `Seek(ms)` | `_player.Seek(CMTime.FromSeconds(ms / 1000.0, 1000))`，仅在 `IsSeekable` 时执行 |
| `Dispose()` | 移除 time observer、移除通知与 KVO、`_player.Pause()`、Dispose item/player |

**事件与观察者（正确性关键）**：

- `PlaybackEnded`：`NSNotificationCenter.DefaultCenter.AddObserver(AVPlayerItem.DidPlayToEndTimeNotification, ..., _item)`；**只观察当前 item**，换曲时必须移除。
- `PlaybackFailed`：两条来源——① KVO `AVPlayerItem.StatusProperty` 变为 `Failed`；② `AVPlayerItem.FailedToPlayToEndTimeNotification`。消息统一为 `"播放失败：{item.Error?.LocalizedDescription ?? "格式不受支持或文件损坏"}"`。
- `PositionChanged`：`_player.AddPeriodicTimeObserver(CMTime.FromSeconds(0.25, 1000), null, cm => PositionChanged?.Invoke(this, ...))`。回调按 `IAudioPlayer` 契约可直接 fire（上层负责切 UI 线程，和 `VlcAudioPlayer` 从 libvlc 线程 fire 的现状一致）。
- `DurationChanged`：`Status == ReadyToPlay` 时读一次 `Duration` 发一次；并在周期性回调里比对，若 duration 由 0 变为有效则补发一次。
- **换曲前必须 `RemoveTimeObserver` / 移除通知 / KVO `RemoveObserver`**，否则会回调到已释放的 item 上直接崩溃。

**音频会话**：

```
var session = AVAudioSession.SharedInstance();
session.SetCategory(AVAudioSessionCategory.Playback);
session.SetActive(true);
```

放在 `Play()` 内（幂等，用 flag 只做一次），避免启动即抢音频焦点。

**新增 `src/Music.iOS/IosAudioFormatSupport.cs`**：

AVFoundation 不支持的格式（`.ogg/.opus/.wma/.ape/.wv/.dsf/.dff` 等）应在 `Load()` 里按扩展名拦截，直接 `PlaybackFailed` 提示"iOS 不支持该格式"，而不是让用户听到静默失败。

---

### 4.4 Core 的最小适配（Windows 上可验证）

#### `Directory.Packages.props`

新增一行（放在 Avalonia 包组内，保持版本同步注释）：

```xml
<PackageVersion Include="Avalonia.iOS" Version="12.1.3" />
```

#### `src/Music/Views/Pages/SettingsView.axaml`（L312–321）

「下载并安装」按钮的可见性从 `HasPendingUpdate` 改为 `CanSelfUpdate`：

```xml
<Button Classes="ghost small" Content="下载并安装"
        IsVisible="{Binding CanSelfUpdate}"
        IsEnabled="{Binding !IsCheckingUpdate}"
        Command="{Binding InstallUpdateCommand}" />
```

**为什么**：iOS 上 `UnsupportedUpdateInstaller.CanSelfUpdate == false`，按钮必须消失（点了也没有安装器）。Android/Windows 上 `CanSelfUpdate == true`，行为与现状**完全一致**，零回归。底部那段 `IsVisible="{Binding !CanSelfUpdate}"` 的提示文案已存在，iOS 上会自然出现。

#### `src/Music/ViewModels/Pages/SettingsViewModel.cs`（L257–259）

`InstallUpdateAsync` 的完成文案补 iOS 分支（虽然按钮已隐藏，但保持逻辑自洽）：

```csharp
UpdateStatusText = OperatingSystem.IsAndroid()
    ? "下载完成，正在打开系统安装界面…"
    : OperatingSystem.IsIOS()
        ? "iOS 不支持应用内自更新，请通过 App Store 更新。"
        : "下载完成，正在重启应用以完成升级…";
```

> 以下**明确不改**：`Music.slnx`、`Music.csproj`、`AppHost.cs`、`ServiceCollectionExtensions.cs`、`AppPaths.cs`、任何 ViewModel 的业务逻辑。

---

## 五、关键设计细节

### 5.1 取流链路在 iOS 上的实际表现

| 音源 | `IAudioPlayer.Load()` 收到什么 | AVPlayer 是否可用 |
| --- | --- | --- |
| 本地文件夹 | 沙盒内文件路径 | ✅ |
| FTP | 缓存目录中的**本地路径**（已提前下载） | ✅ |
| Navidrome | `http(s)://.../rest/stream?...` | ✅（需 `NSAllowsArbitraryLoads` 才允许 http） |

因此 **iOS 端不需要任何 FTP 客户端能力**，AVPlayer 也不会遇到它不支持的 `ftp://` 协议。

### 5.2 后台播放

- `UIBackgroundModes = [audio]`（Info.plist）
- `AVAudioSession` 类别 `Playback`
- 两者缺一不可；缺任一项，锁屏后音频会被挂起。

### 5.3 数据目录与音乐导入

- `AppPaths.Root` → `Documents/MusicData`（DB / cache / covers / lyrics / 下载）
- `Documents/Music` → 预留给用户导入（通过「文件」App 或 Finder 拖入）
- 添加本地音源**复用现有 Sources 页的文件夹选择**（`AvaloniaFilePickerService` 走 `TopLevel.StorageProvider`，iOS 上打开「文件」App 的系统选择器）。**不在 head 里硬编码种子音源**，避免触碰 Core 逻辑。

### 5.4 自更新限制

iOS 沙盒禁止应用自更新，只能跳 App Store。Phase 1 通过 `CanSelfUpdate == false` 让 UI 自动降级；Phase 2 可选实现 `AppStoreUpdateInstaller`（`UIApplication.SharedApplication.OpenUrl` 跳转，需先提供一个 App Store 链接来源）。

---

## 六、假设与风险

| # | 风险 | 影响 | 处置 |
| --- | --- | --- | --- |
| R1 | **Windows 无法编译验证 iOS 工程** | 交付的代码可能首次在 Mac 上编译失败 | 不承诺"已验证可编译"；首次 Mac 构建按第八章步骤逐个排错 |
| R2 | `Avalonia.iOS` 12.1.3 与 `net10.0-ios` 资产不匹配（T 前缀版本需与 workload 对齐） | restore 报 NU1202 / 版本不兼容 | 若失败，改用 `net10.0-ios18.0` 显式平台版本，或临时降 `SupportedOSPlatformVersion` 重试 |
| R3 | `Microsoft.Data.Sqlite` 的原生 `e_sqlite3` 在 iOS 未正确链接 | 启动即崩（DB 打不开） | 首次运行时若崩，在 iOS csproj 加 `<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" />` 显式引入 |
| R4 | Release 裁剪（TrimMode）误裁 `System.Text.Json` 反射路径 | 设置/清单反序列化失败 | iOS Release 首次先加 `<TrimMode>partial</TrimMode>`，稳定后再收紧 |
| R5 | `App.Initialize()` 里 `#if DEBUG this.AttachDeveloperTools()` 在 iOS 上异常 | Debug 启动白屏 | 若命中，临时在该处加平台判断（这是 Core 改动，需用户确认） |
| R6 | 未加 `UIApplicationSceneManifest` 但 Avalonia 版本行为变化 | 启动白屏 | 先在模拟器验证；若白屏，改用 `AvaloniaSceneDelegate` 并补上 manifest |
| R7 | `Environment.SpecialFolder.MyDocuments` 在 .NET for iOS 上返回非预期路径 | 数据/音乐目录不可见 | `PlatformPaths.Configure()` 内加日志落盘，Mac 首跑时确认 |
| R8 | 首启弹「本地网络」权限框 | 用户体验 | 已知且必要（局域网音源）；不属于缺陷 |
| R9 | AVPlayer 对 CBR/损坏文件报错信息不明确 | 用户看到"播放失败" | 已按 R 列兜底文案；格式白名单在 `IosAudioFormatSupport` 中给出更具体提示 |
| R10 | iOS 后台播放被系统回收 | 长时间后台暂停播放 | Phase 1 接受；Phase 2 接 `MPNowPlayingInfoCenter`/`MPRemoteCommandCenter` 改善 |

---

## 七、分步实施顺序

> Phase 1 全部步骤可在 Windows 上完成"写代码 + 保证现有三端不回归"；第 6 步起必须在 Mac。

1. `Directory.Packages.props` 加 `Avalonia.iOS`（唯一影响 restore 的 Core 改动）。
2. 建 `src/Music.iOS/`：`Music.iOS.csproj`、`Main.cs`、`PlatformPaths.cs`、`AppDelegate.cs`、`Info.plist`、`Entitlements.plist`、`Assets.xcassets/`。
3. 写 `IosAudioPlayer.cs` + `IosAudioFormatSupport.cs`。
4. 改 `SettingsView.axaml`（按钮可见性）+ `SettingsViewModel.cs`（iOS 文案分支）。
5. **Windows 回归验证**：`dotnet build src/Music/Music.csproj`、`src/Music.Desktop`、`src/Music.Android`，确认 0 错误、现有行为不变（`Music.slnx` 不动，因此 `dotnet build Music.slnx` 不受影响）。
6. （Mac）`dotnet workload install ios` → `dotnet restore src/Music.iOS/Music.iOS.csproj`。
7. （Mac）`dotnet build src/Music.iOS/Music.iOS.csproj -f net10.0-ios -c Debug`。
8. （Mac）模拟器运行 `dotnet build src/Music.iOS/Music.iOS.csproj -f net10.0-ios -t:Run -p:RuntimeIdentifier=iossimulator-arm64`。
9. 按第八章验收，修 R2–R7 中的实际命中项。
10. （Phase 2，另立计划）`MPNowPlayingInfoCenter` + `MPRemoteCommandCenter` 锁屏/耳机控制；App Store 跳转更新。

---

## 八、Mac 上的首次构建与验证步骤

```bash
# 0) 前置：macOS + Xcode（命令行工具已 accept license）
xcode-select --install

# 1) 安装 iOS workload
dotnet workload install ios

# 2) 还原
dotnet restore src/Music.iOS/Music.iOS.csproj

# 3) 编译（先真机 RID，能暴露原生链接问题）
dotnet build src/Music.iOS/Music.iOS.csproj -f net10.0-ios -c Debug -p:RuntimeIdentifier=ios-arm64

# 4) 模拟器运行
dotnet build src/Music.iOS/Music.iOS.csproj -f net10.0-ios -t:Run \
  -p:RuntimeIdentifier=iossimulator-arm64
```

**必查项（7 条）**：

1. 应用能启动到主界面，不白屏、不闪退（对应 R5/R6）。
2. 添加本地音源：Sources 页 → 选文件夹 → 能选到 `Documents/Music` 并成功扫描入库（对应 R7）。
3. 播放本地曲目：进度条走动、可拖动进度、可暂停/恢复。
4. 自动下一曲：一首播完能自然切下一首（验证 `PlaybackEnded`）。
5. 后台播放：锁屏 / 切到桌面后音频继续（验证 `UIBackgroundModes` + AudioSession）。
6. 不支持格式：放一个 `.ogg` 进去播放，应弹出明确的"iOS 不支持该格式"提示而非静默失败。
7. Navidrome（http）：配置后能播放（验证 `NSAllowsArbitraryLoads`）。

---

## 九、验收清单

- [ ] `src/Music.iOS` 工程存在，`Music.slnx` 保持三工程不变。
- [ ] Windows 上 `Music` / `Music.Desktop` / `Music.Android` 三端构建 0 错误。
- [ ] iOS 上应用可启动、可扫描并播放本地曲目、可连 Navidrome 播放、可后台播放。
- [ ] 设置页在 iOS 上不出现「下载并安装」按钮，且显示"当前平台未实现应用内自更新"提示。
- [ ] Android / Windows 的设置页升级流程与现状完全一致（无回归）。
- [ ] `MarqueeText` 跑马灯等既有 UI 在 iOS 上表现与 Android 一致（同一套 XAML）。

---

## 十、附：本次不做的事（避免范围蔓延）

- 不实现锁屏/控制中心/耳机线控（Phase 2）。
- 不实现 App Store 自更新跳转（Phase 2）。
- 不引入 libvlc 的 iOS 版本。
- 不改动取流、缓存、刮削、歌词、跑马灯等任何既有业务逻辑。
- 不修改 `Music.slnx`、`AppHost.cs`、`ServiceCollectionExtensions.cs`。

---

# 十一、AOT 编译可行性（新增章节）

## 11.1 结论：分平台，不可一概而论

| 平台 | 能否开 AOT | 结论与依据 |
| --- | --- | --- |
| **Desktop** | ❌ 不能 | `Music.Desktop.csproj` 已显式 `PublishAot=false` / `PublishSingleFile=false`，理由是 libvlc 从 `plugins/` 目录树加载原生插件。叠加两个更硬的阻断：`LibVLCSharp` 是 interop 绑定层；`SmtcSystemMediaService` 依赖 CsWinRT（WinRT 互操作）。**保持关闭，不在本次范围。** |
| **Android** | ⚠️ 可选，但属另一套体系 | 现为 `AndroidEnableProfiledAot=false`。Android 走 **Mono AOT / Profiled AOT**（`RunAOTCompilation=true`），不是 NativeAOT：开启后启动更快，但构建更慢、包体积更大，且 Mono AOT 对泛型值类型会回退到 JIT/解释器。**本次保持关闭**，等 iOS 稳定后单独评估。 |
| **iOS** | ✅ 可以，且 Release 本来就是 AOT | iOS 禁止 JIT，真机 Release 构建本身就是 AOT（Mono AOT），可选进一步切 NativeAOT（`PublishAot`）。iOS head 用 AVPlayer、不引用 libvlc，天然绕开 Desktop 的致命阻断。**本次在 iOS 工程显式声明 AOT/裁剪配置。** |

## 11.2 代码层 AOT 阻断项（必须在 Windows 上先消除）

| # | 位置 | 问题 | 处置 |
| --- | --- | --- | --- |
| 1 | [ViewLocator.cs](file:///e:/Code/Avalonia/Music/src/Music/ViewLocator.cs#L22-L27) 的 `Type.GetType(name)` + `Activator.CreateInstance` | **硬阻断**：AOT/裁剪后类型解析返回 null，所有页面会渲染成 `Not Found: ...`。该类已注册在 [App.axaml](file:///e:/Code/Avalonia/Music/src/Music/App.axaml#L10) 作为全局 DataTemplate | 改成**显式 `switch` 映射**（ViewModel 类型 → View 工厂委托），彻底移除反射。`Match` 仍保留 `data is ViewModelBase` |
| 2 | [JsonSettingsStore.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/JsonSettingsStore.cs#L38-L71) 反射序列化 `AppSettings`（含多态 `MusicSourceConfig`） | 裁剪后元数据站点被裁掉，读写设置崩溃 | 新增 `JsonSerializerContext` 源生成；多态已用 `[JsonPolymorphic]`+`[JsonDerivedType]` 特性声明，源生成器可识别。`WriteIndented`/`IgnoreReadOnlyProperties` 用 `[JsonSourceGenerationOptions]` 表达 |
| 3 | [UpdateService.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Update/UpdateService.cs#L112-L113) 反序列化 `UpdateManifest` | 同上 | 并入同一个 context；`PropertyNameCaseInsensitive` 用 `[JsonSourceGenerationOptions]` 表达 |
| 4 | [UpdateDefaults.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Update/UpdateDefaults.cs#L37-L38) 反序列化私有嵌套类 `UpdateConfig` | 私有嵌套类型同样需要元数据 | 并入同一个 context（必要时提升为 internal 类型） |
| 5 | [LyricsBroadcastServer.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Broadcast/LyricsBroadcastServer.cs#L304-L326) 序列化**匿名类型** | 匿名类型无法写进 source-gen context | 改成具名 `record`（`TrackEventPayload` / `LineEventPayload`）后并入 context |
| — | 6 个元数据 Provider、`NavidromeClient` | 全部用 `JsonDocument`/`JsonElement` 导航，不依赖反射序列化 | **AOT 安全，无需改** |
| — | `CommunityToolkit.Mvvm` | 源生成器（`[ObservableProperty]` / `[RelayCommand]`） | **AOT 友好，无需改** |
| — | `AvaloniaUI.DiagnosticsSupport` | Release 下 `PrivateAssets=All` 已排除 | 无需改 |
| — | View 绑定 | 8 个含绑定的 View 均声明了 `x:DataType`（共 22 处），走编译绑定；iOS 只用 `MainView`，`MainWindow` 不参与 | 无需改 |
| — | Avalonia XAML | XamlIL 在构建期编译成 IL | AOT 友好，无需改 |

> ⚠️ 第 1–5 项都是**共享 Core 的改动**，会同时影响 Desktop 与 Android。改动为行为保持型，但必须做三端回归构建验证。

## 11.3 iOS 工程的 AOT 配置

- iOS 的 AOT 由 TFM + Configuration 决定：真机 Release 走 AOT，模拟器 Debug 走 JIT。csproj 中显式声明裁剪与 AOT 相关属性（`PublishAot` / `PublishTrimmed` / `TrimMode`）。
- 首次先在 Mac 上跑 **Release publish** 验证，而不是一开始就上最激进的裁剪档：

  ```bash
  dotnet publish src/Music.iOS/Music.iOS.csproj -f net10.0-ios -c Release -r ios-arm64
  ```

- 若出现裁剪相关错误，按序排查：补 `JsonSerializerContext` 遗漏类型 → 加 `[DynamicDependency]` → 加 `<TrimmerRootDescriptor>` 兜底，**不要**直接关掉裁剪。
- **最终验证只能在 Mac**。Windows 上能做且必须做的是：消除 11.2 的 1–5 项，并用三端构建确认无回归。

## 11.4 修订后的实施顺序（覆盖原第七章）

1. **AOT 前置改造（Windows 可验证）**：ViewLocator 去反射 → 新增 `JsonSerializerContext` 并迁移 4 个调用点 → 广播负载匿名类型具名化。
2. **三端回归构建**：`Music` / `Music.Desktop` / `Music.Android` 全部 0 错误，现有行为不变。
3. **建 iOS 工程**：csproj（含 AOT/裁剪属性）、`Main.cs`、`PlatformPaths.cs`、`AppDelegate.cs`、`Info.plist`、`Entitlements.plist`、`Assets.xcassets/`。
4. **写音频层**：`IosAudioPlayer.cs`（AVPlayer）+ `IosAudioFormatSupport.cs`。
5. **Core 平台适配**：`Directory.Packages.props` 加 `Avalonia.iOS`；设置页升级按钮可见性改 `CanSelfUpdate`；`SettingsViewModel` 加 iOS 文案分支。
6. **（Mac）** `dotnet workload install ios` → restore → Debug 构建 → 模拟器运行 → 按第八章 7 条验收。
7. **（Mac）** Release publish 验证 AOT，按 11.3 逐个排除裁剪问题。