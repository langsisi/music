# Android 音频焦点闪避 + 通知栏/锁屏「正在播放」集成方案

## Context

用户使用安卓手机，提出两个缺口：

1. **导航播报时音乐不会自动降音量**：其他音乐 App 在导航播报时会立刻把音乐压低（ducking），本应用却继续全音量播放。
2. **没有「正在播放」的系统控制条**：QQ 音乐播放时，锁屏左侧/顶部下拉通知栏都有媒体控制卡片，本应用完全不显示。

现状根因（已确认）：

- 播放器走 libvlc（[VlcAudioPlayer.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Audio/VlcAudioPlayer.cs)，Core 内跨平台共用），**从未申请过 Android 音频焦点** `AudioFocus`，系统通知不到我们「该让路」，所以没有闪避行为，也无法应对来电等打断。
- [AndroidSystemMediaService.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/AndroidSystemMediaService.cs) 只创建了 `MediaSession` 并推送元数据，**没有发 `MediaStyle` 前台通知**。Android 11+ 的通知栏/锁屏媒体控制卡片要求会话关联一个带 `MediaStyle` 的通知：没有它，只有蓝牙 AVRCP 能拿到歌名，系统 UI 不显示控制条。

目标平台：**仅 Android**（用户已确认）。桌面/iOS 不受影响（保留 Noop 实现）。

## 一、音频焦点与导航自动闪避

### 1. Core 新增抽象（[src/Music/Services/Audio/IAudioFocusService.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Audio/IAudioFocusService.cs)）

```csharp
public enum AudioFocusChange { Gain, Loss, LossTransient, LossTransientCanDuck }

public interface IAudioFocusService : IDisposable
{
    event EventHandler<AudioFocusChange>? FocusChanged;
    bool RequestFocus();
    void AbandonFocus();
}

public sealed class NoopAudioFocusService : IAudioFocusService { /* RequestFocus 返回 true，事件不触发 */ }
```

在 [ServiceCollectionExtensions.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/ServiceCollectionExtensions.cs) 的 `AddCoreServices` 里注册 `AddSingleton<IAudioFocusService, NoopAudioFocusService>()`（与现有 `ISystemMediaService` 同一模式）。

### 2. Android 实现（[src/Music.Android/AndroidAudioFocusService.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/AndroidAudioFocusService.cs)）

- 继承 `Java.Lang.Object`，实现 `AudioManager.IOnAudioFocusChangeListener`。
- `RequestFocus()`：
  - API 26+：`new AudioFocusRequest.Builder(AudioFocus.Gain)`，`SetAudioAttributes(...Usage=Media, ContentType=Music)`，关键 `SetWillPauseWhenDucked(false)`——这样系统对「可闪避的临时占用」（导航播报走 `AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK`）下发的是 `LossTransientCanDuck` 而不是 `LossTransient`，我们才能自己降音量而非暂停。
  - API < 26：退回 `RequestAudioFocus(listener, Stream.Music, AudioFocus.Gain)`。
- 回调 `OnAudioFocusChange` 在主线程 `Handler` 上映射成 `AudioFocusChange` 抛出。
- 与现有 [AndroidSystemMediaService.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/AndroidSystemMediaService.cs) 一致：全程 try/catch 静默降级，任何平台异常都不影响播放。

### 3. PlaybackService 接入（[src/Music/Services/Audio/PlaybackService.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Audio/PlaybackService.cs)）

构造函数新增 `IAudioFocusService` 参数（DI 自动注入）。

- 新增「期望音量」字段 `_volume`（默认 100）与 `bool _ducked`；`Volume` 属性的 get/set 改用 `_volume`，新增私有 `ApplyVolume()`：
  - 正常：`_player.Volume = _volume`
  - 闪避：`_player.Volume = (int)(_volume * DuckFactor)`，`DuckFactor = 0.2`
  - 这样闪避**不污染期望音量**，[PlayerViewModel.cs](file:///e:/Code/Avalonia/Music/src/Music/ViewModels/PlayerViewModel.cs#L159-L177) 的音量滑块/持久化完全不跳动。
- 焦点生命周期：`PlayCurrentAsync` 开播前、`TogglePlay` 的「播放」分支调用 `_focus.RequestFocus()`；`Pause`/`Stop` 分支调用 `_focus.AbandonFocus()`。
- 订阅 `FocusChanged`：
  - `Gain` → `_ducked = false; ApplyVolume();`
  - `LossTransientCanDuck` → `_ducked = true; ApplyVolume();`（**导航播报场景**）
  - `LossTransient` / `Loss` → 取消闪避并暂停播放。

### 4. Android head 覆盖注册（[src/Music.Android/Application.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/Application.cs)）

在 `CustomizeAppBuilder` 的 `AppHost.Configure` 中追加 `services.AddSingleton<IAudioFocusService, AndroidAudioFocusService>();`。

## 二、通知栏 / 锁屏「正在播放」控制条

### 1. 新增前台服务（[src/Music.Android/MediaPlaybackService.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/MediaPlaybackService.cs)）

```csharp
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class MediaPlaybackService : Service
```

- `public static MediaPlaybackService? Current`；`OnCreate` 赋值、`OnDestroy` 置空。
- `OnStartCommand` 读取静态待发布通知并调用 `StartForeground`（满足 Android 12+ 「5 秒内必须 startForeground」要求）：
  ```csharp
  if (OperatingSystem.IsAndroidVersionAtLeast(29))
      StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
  else
      StartForeground(NotificationId, notification);
  ```
- 对外提供 `SetNotification(Notification n)`（服务已在运行时直接刷新前台通知）与静态 `PendingNotification`（服务尚未启动时由 `AndroidSystemMediaService` 预置）。
- `OnDestroy` 里 `StopForeground(StopForegroundFlags.Remove)`。

### 2. 通知动作接收器（[src/Music.Android/MediaActionReceiver.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/MediaActionReceiver.cs)）

`[BroadcastReceiver(Exported = false)]` 的 `BroadcastReceiver`，按 `Intent.Action` 把「上一首 / 播放暂停 / 下一首」转发给 `AndroidSystemMediaService` 的静态实例 → 复用已有 `CommandReceived` 事件链路（[PlayerViewModel.OnSystemMediaCommand](file:///e:/Code/Avalonia/Music/src/Music/ViewModels/PlayerViewModel.cs#L1228-L1250) 已实现），不新增命令通道。通知按钮用显式组件 `PendingIntent.GetBroadcast`，`Exported=false` 不影响同应用投递。

### 3. 扩展 [AndroidSystemMediaService.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/AndroidSystemMediaService.cs)

- 新增静态实例引用（供 `MediaActionReceiver` 转发）。
- 新增 `BuildNotification()`：`Notification.Builder` + `Notification.MediaStyle`
  - `SetSmallIcon(Resource.Drawable.ic_stat_music)`（新增单色矢量图）
  - `SetLargeIcon(封面位图)`（复用已有 `LoadArt`）
  - `SetContentTitle/Text`（标题/艺术家，专辑位保持现有歌词行逻辑）
  - `SetStyle(new Notification.MediaStyle().SetMediaSession(session.SessionToken).SetShowActionsInCompactView(0,1,2))`
  - 三个动作：上一首 / 播放暂停 / 下一首，各自 `PendingIntent` → `MediaActionReceiver`
  - 点击通知 → 打开 `MainActivity`
- 通知渠道（API 26+）：`NotificationChannel`，重要性 `Low`、无声，创建一次。
- 发布时机：
  - `Update(info)` → 构建通知；`MediaPlaybackService.Current` 存在则 `SetNotification`，否则 `Application.Context.StartForegroundService(intent)`（外层 try/catch 兜住 Android 12+ 后台启动限制）。
  - `UpdateLine`（歌词行变化）→ 仅刷新已发布通知的文本，不重启服务。
  - `Clear()` → 停服务（`StopService`）+ `NotificationManager.Cancel`，并清理 `MediaSession` 状态。

Android 11+ 的锁屏/下拉控制条由系统按 `MediaSession` 的 `PlaybackState` + `Actions` + 元数据渲染（已有 `Actions` 位包含 Play/Pause/SkipToNext/SkipToPrevious/Stop），`MediaStyle` 通知是让会话「可见」的必要条件；低版本则由通知自带按钮兜底。

### 4. Manifest 与权限

[Properties/AndroidManifest.xml](file:///e:/Code/Avalonia/Music/src/Music.Android/Properties/AndroidManifest.xml) 增加：

```xml
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK" />
<uses-permission android:name="android.permission.WAKE_LOCK" />
```

[MainActivity.cs](file:///e:/Code/Avalonia/Music/src/Music.Android/MainActivity.cs) 在 `RequestStoragePermission` 之外补一次通知权限申请（仅 API 33+，复用同款 `CheckSelfPermission` / `RequestPermissions` 写法，独立请求码）。

### 5. 新增资源

[Resources/drawable/ic_stat_music.xml](file:///e:/Code/Avalonia/Music/src/Music.Android/Resources/drawable/ic_stat_music.xml)：单色 `<vector>` 音符图标（状态栏小图标必须单色，直接用彩色 `Icon.png` 会显示成白块）。

## 涉及文件汇总

| 文件 | 动作 |
| --- | --- |
| `src/Music/Services/Audio/IAudioFocusService.cs` | 新增（接口 + 枚举 + Noop） |
| `src/Music/Services/ServiceCollectionExtensions.cs` | 注册 Noop 音频焦点 |
| `src/Music/Services/Audio/PlaybackService.cs` | 接入焦点 + 闪避音量 |
| `src/Music.Android/AndroidAudioFocusService.cs` | 新增（AudioManager 实现） |
| `src/Music.Android/MediaPlaybackService.cs` | 新增（前台服务） |
| `src/Music.Android/MediaActionReceiver.cs` | 新增（通知按钮转发） |
| `src/Music.Android/AndroidSystemMediaService.cs` | 扩展（MediaStyle 通知 + 服务协同） |
| `src/Music.Android/Application.cs` | 注册 AndroidAudioFocusService |
| `src/Music.Android/MainActivity.cs` | 申请通知权限（33+） |
| `src/Music.Android/Properties/AndroidManifest.xml` | 新增权限 |
| `src/Music.Android/Resources/drawable/ic_stat_music.xml` | 新增单色小图标 |

桌面与 iOS head 不做改动，行为保持原样（Noop）。

## 验证

1. 编译：`dotnet build src/Music.Android/Music.Android.csproj -c Debug -f net10.0-android`（0 警告 0 错误）。
2. 真机安装后：
   - **通知栏/锁屏**：播放歌曲 → 顶部下拉出现带封面的媒体卡片；锁屏可见控制条；上一首/下一首/暂停按钮均生效；切歌后标题与封面更新。
   - **音频焦点闪避**：开高德/百度地图导航播报 → 音乐音量立刻降到约 20%，播报结束自动恢复；播放中接听来电 → 音乐暂停，挂断后不自动续播（符合常见行为）。
   - **边界**：暂停/停止播放后通知消失、服务退出；重启后不残留通知；Android 13+ 首次播放弹通知权限申请，拒绝时不崩溃（仅无控制条）。
3. 回归：桌面端 `dotnet build src/Music.Desktop/Music.Desktop.csproj` 确认 Noop 路径无编译影响。