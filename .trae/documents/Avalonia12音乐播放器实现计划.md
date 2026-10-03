# Avalonia 12 跨平台音乐播放器 — 实现计划

## 一、背景与目标（Context）

`e:\Code\Avalonia\Music` 目前是**空目录**，这是一次从零开始（greenfield）的建设。

目标是用 **Avalonia 12** 做一个现代、美观的跨平台音乐播放器，同时满足 6 项需求：

1. 界面现代化、美观漂亮
2. 同时支持**桌面端**与**手机端**
3. 音乐源支持**本地音乐 / FTP / Navidrome**
4. 支持**歌词显示**与**「蓝牙推送歌词」**
5. 支持**收藏**与**分类**
6. 支持**设置缓存大小**，播放过的歌曲命中缓存不再重新下载
7. 支持**在线升级**，自动更新软件到最新版本

预期产出：一个可编译、可运行、功能可点击验证的桌面应用；同时搭好 Android 头项目脚手架与跨平台共享架构。

### 已与用户确认的四项决策

| 决策点 | 结论 |
|---|---|
| 移动端 | **桌面优先 + Android 项目脚手架**（iOS 无法在 Windows 构建，不纳入） |
| 蓝牙歌词 | **系统媒体信息（SMTC）+ 内置广播服务** |
| 界面主题 | **深色为主 + 可切换浅色**，自动跟随系统 |
| 音频引擎 | **LibVLCSharp**（本地/ftp/http 统一后端，格式最全） |

---

## 二、技术选型（版本已通过 NuGet 核实）

| 用途 | 包 | 版本 |
|---|---|---|
| UI 框架 | `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` / `Avalonia.Fonts.Inter` | 12.1.3 |
| MVVM | `CommunityToolkit.Mvvm` | 8.4.2 |
| 音频引擎（托管层） | `LibVLCSharp` | 3.10.1 |
| 音频原生库（Windows 头） | `VideoLAN.LibVLC.Windows` | 3.0.24 |
| 音频原生库（Android 头） | `VideoLAN.LibVLC.Android` | 3.6.5 |
| 本地元数据 | `TagLibSharp` | 2.3.0 |
| 本地数据库（收藏/分类/缓存索引） | `Microsoft.Data.Sqlite` | 10.x |
| 依赖注入 | `Microsoft.Extensions.DependencyInjection` | 10.x |
| 歌词解析 | **自研**（NuGet 上 LRC 包全部停更，不值得引入） | — |

**关键版本约束**：libvlc 3.0.24 ↔ LibVLCSharp 3.x 主版本必须匹配（LibVLCSharp 4.x 只配 libvlc 4，混用会抛 `VLCException`）。✅ 当前组合正确。

---

## 三、解决方案结构

```
Music.sln
├─ src/Music/              (net10.0)        共享核心：模型 / 服务 / VM / View / 样式
├─ src/Music.Desktop/      (net10.0;net10.0-windows10.0.19041.0)  桌面头
└─ src/Music.Android/      (net10.0-android) Android 头（脚手架，需 workload）
```

脚手架命令：

```powershell
dotnet new install Avalonia.Templates::12.1.3
dotnet new avalonia.xplat -o Music -n Music
# 精简掉用不到的 Browser / iOS 头
```

**分层铁律（防平台依赖泄漏，这是本项目最关键的架构约束）**：

- `Music`（Core）**只**引用可跨平台的托管程序集（`LibVLCSharp` 托管层、`TagLibSharp`、`Microsoft.Data.Sqlite`、BCL）。**绝不**引用 `VideoLAN.LibVLC.Windows` 或任何 WinRT API，否则 Android 编译不过。
- **原生库只放在各 head**：Windows 头引 `VideoLAN.LibVLC.Windows`，Android 头引 `VideoLAN.LibVLC.Android`。
- 平台相关能力（SMTC、Android 生命周期）通过 **Core 中定义的接口**抽象，实现在各 head 注册。

Android 头不参与日常构建（本机未装 android workload）：日常用 `dotnet build src/Music.Desktop` 验证；Android 待 `dotnet workload install android` 后单独构建。

---

## 四、核心抽象（Core 中的关键接口）

| 接口 | 职责 | 实现 |
|---|---|---|
| `IAudioPlayer` | 播放/暂停/跳转/音量/进度/结束事件 | `VlcAudioPlayer`（Core，音频-only） |
| `IMusicSource` | 统一的曲库浏览/搜索/取流 | `LocalMusicSource` / `FtpMusicSource` / `NavidromeMusicSource` |
| `IMediaResolver` | 把「曲目」解析为**可播放的本地路径或 URL** | 负责命中缓存 → 否则按源类型下载或直连 |
| `IAudioCache` | 缓存写入/命中/淘汰/容量统计 | `SqliteAudioCache` |
| `ISystemMediaService` | 系统媒体信息（歌名/封面/媒体键） | `NoopSystemMediaService`（默认）/ Windows 头 `SmtcSystemMediaService` |
| `ILyricsBroadcaster` | 对外广播当前歌词 | `HttpLyricsBroadcaster`（TcpListener + SSE） |
| `ISettingsStore` | 设置持久化 | `JsonSettingsStore` |
| `ILibraryStore` | 收藏 / 分类 / 歌单 | `SqliteLibraryStore` |

### 音频引擎要点（`VlcAudioPlayer`）

纯音频**不需要 `VideoView`**，`MediaPlayer` 独立于渲染控件。必须显式禁用视频输出，防止带封面的 MP4/MKV 弹出独立窗口：

```csharp
Core.Initialize(AppContext.BaseDirectory);   // 用带路径重载，避免单文件下返回空串
_libvlc = new LibVLC("--no-video", "--no-audio-visual", "--no-video-title-show",
                     "--network-caching=3000", "--file-caching=1000");
_player = new MediaPlayer(_libvlc);          // 全生命周期只建一个
_player.Volume = 80;                          // 0..100
_player.Position = 0.35f;                     // 0..1，进度用 TimeChanged 事件而非轮询
```

**必须遵守的线程/生命周期规则**（否则死锁或崩溃）：

- `EndReached` / `EncounteredError` 在 **libvlc 内部线程**触发，回调里**绝不能**直接 `Play/Stop/Dispose`，必须 `Dispatcher.UIThread.Post(...)` 切回 UI 线程再切歌。
- 切歌顺序：`Stop()` → 旧 `Media.Dispose()` → 赋新 `Media`；`Player/LibVLC` 仅在应用退出时释放。
- 应用固定 **x64**；**禁用单文件发布与 NativeAOT**（libvlc 依赖 `plugins/` 目录结构按路径加载）。

### 曲源与缓存策略（需求 6 的核心）

缓存目录：`%LOCALAPPDATA%/Music/cache/{key[0..1]}/{key}{ext}`，`key = SHA256(归一化 URI)`。

| 源 | 策略 | 原因 |
|---|---|---|
| 本地文件 | 直接播放，不入缓存 | 已在本地 |
| **HTTP（Navidrome）** | **直连播放 + 后台异步落盘缓存** | Subsonic `stream` 支持 Range，可正常 seek；边播边缓存体验最好 |
| **FTP** | **强制「先下载到缓存再播放」** | FTP 无 Range/随机寻址，libvlc 拖动进度会重传整个文件且错误不可观测；下载即缓存一举两得，顺带让 TagLibSharp 能读元数据 |

缓存写入**原子化**：写 `{key}.tmp` → 校验 `Content-Length` → `File.Move(tmp, final, overwrite:true)`；启动时清理孤儿 `.tmp`。

`SqliteAudioCache` 索引表：`Cache(key PK, sourceUri, contentType, bytes, addedUtc, lastPlayedUtc, isPinned)`；
**LRU 淘汰**：命中即刷新 `lastPlayedUtc`；超限时按 `lastPlayedUtc ASC` 删除直到 `totalBytes <= MaxMb*0.9`（留 10% 余量），跳过 `isPinned` 与正在播放/下载项；设置里调小 `MaxMb` 立即触发一次淘汰；`MaxMb=0` 表示禁用缓存。

### Navidrome（Subsonic API）

公共参数：`u` 用户名、`t = md5(password + salt)`、`s` = 随机 salt、`v=1.16.1`、`c` 客户端名、`f=json`。

| 用途 | 端点 |
|---|---|
| 认证/探活 | `/rest/ping.view` |
| 歌手列表 | `/rest/getArtists.view` |
| 专辑列表 | `/rest/getAlbumList2.view?type=alphabeticalByArtist` |
| 专辑详情+曲目 | `/rest/getAlbum.view?id=al_xxx` → `album.song[]` |
| 流式播放 | `/rest/stream.view?id=so_xxx&format=raw` |
| 封面 | `/rest/getCoverArt.view?id=al_xxx&size=600` |

注意：Navidrome ID 带 `al_`/`so_`/`ar_` 前缀，**不要当数字**；错误在根节点 `status="failed"` + `error code`（40 密码错 / 70 未找到）。

### 歌词（需求 4）

- **解析**：自研 LRC 解析器（约 120 行）。正则 `\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]`，支持一行多时间戳、`[offset:±ms]`、两位/三位小数、`[ti:][ar:][al:]`；输出按时间排序的行数组，播放时**二分查找**当前行。
- **歌词来源**：优先同名 `.lrc` / `Navidrome getLyricsBySongId`，无则显示「暂无歌词」。
- **显示**：`PlayerView` 内滚动歌词视图，当前行高亮 + 平滑滚动。

### 「蓝牙推送歌词」的实现与边界（需求 4，**必须向用户说明的诚实限制**）

经核实：**SMTC 只有标题/艺术家/专辑/流派/缩略图/播放状态，没有任何歌词字段；蓝牙 AVRCP 协议本身也不承载歌词**。因此拆成两条路径：

1. **系统媒体信息（Windows 头，`#if WINDOWS`）** — 通过 `net10.0-windows10.0.19041.0` TFM + `SystemMediaTransportControlsInterop.GetForWindow(hwnd)` 推送歌名/歌手/专辑/封面并接管媒体键。效果：**蓝牙耳机/车机显示歌名封面、可遥控**，但**不会**滚动显示歌词。
2. **内置广播服务（Core，跨平台）** — `TcpListener` 自建极简 HTTP 服务，实现：
   - `GET /` 返回内置的**滚动歌词网页**（手机浏览器打开即可）；
   - `GET /events` 用 **SSE（Server-Sent Events）** 实时推送「当前行歌词 + 曲目信息」（SSE 比 WebSocket 简单得多，浏览器自动重连，无需 admin/URL ACL，因为直接绑定 socket 而非 `HttpListener`）。
   
   → 手机连蓝牙音箱、同时打开该网页，即可实现「歌词跟随蓝牙设备播放场景」显示。

**取舍**：非 MSIX 打包应用在部分 Windows 版本上 SMTC 会话可见性不保证，因此设计为**可降级**——SMTC 不可用时静默降级为 Noop，不影响播放。

### 收藏与分类（需求 5）

`SqliteLibraryStore`：`Favorites(trackKey PK, addedUtc)`、`Categories(id, name, sort)`、`CategoryTracks(categoryId, trackKey)`、`Playlists`。曲目用稳定的 `trackKey`（本地=相对路径哈希，网络源=`sourceId:trackId`）跨源统一标识。

---

## 五、界面设计（需求 1、2）

- **主题系统**：`Styles/` 下定义语义化颜色 Token 资源（`ThemeDictionaries`），深色为主 + 浅色一套，设置里切换 + 跟随系统（`Application.Current.RequestedThemeVariant`）。
- **视觉风格**：Fluent 基底 + 自定义样式。左侧/底部导航、封面卡片网格、渐变强调色、圆角、半透明「毛玻璃」横幅、平滑过渡动画。
- **自适应布局（需求 2）**：用 `VisualStateManager` / 宽度断点切换——桌面为「左侧导航 + 内容区 + 底部播放条」，窄屏（手机）切换为「底部标签栏 + 全屏播放页」。**同一套 View 复用**，不写两套。
- 主要页面：`NowPlayingView`（播放/歌词）、`LibraryView`（收藏/分类/歌单）、`SourcesView`（本地/FTP/Navidrome 配置与浏览）、`SearchView`、`SettingsView`（缓存大小、主题、广播服务开关与地址）。

---

## 六、实施阶段（按序执行，每阶段保证可编译）

| 阶段 | 内容 | 完成标志 |
|---|---|---|
| **P1 骨架** | 装模板、生成 sln、清理 iOS/Browser、接 DI、跑通空窗口 | `dotnet run` 出窗口 |
| **P2 主题与外壳** | 颜色 Token、深浅主题、导航外壳、自适应布局框架、自定义控件（封面/进度条） | 界面成型，可切主题 |
| **P3 领域与设置** | Models、`ISettingsStore`、`ILibraryStore`（SQLite）、本地扫描（TagLibSharp 读元数据/封面） | 能扫出本地曲库 |
| **P4 播放引擎** | `VlcAudioPlayer`、播放队列、播放/暂停/上下曲/进度/音量/循环/随机、NowPlaying 页 | 能播本地歌 |
| **P5 缓存** | `IAudioCache` + `SqliteAudioCache` + `IMediaResolver`，设置页缓存容量与占用显示 | 二次播放命中缓存 |
| **P6 歌词** | LRC 解析、歌词视图与滚动高亮、`HttpLyricsBroadcaster`（SSE 网页）、SMTC（Windows 头） | 歌词滚动、手机网页可见 |
| **P7 Navidrome** | Subsonic 客户端、登录配置、浏览/搜索/播放/封面、歌词 | 连真实 Navidrome 播放 |
| **P8 FTP** | FTP 配置与浏览、下载到缓存再播放 | FTP 曲目可播且二次命中缓存 |
| **P9 收藏分类** | 收藏、分类/歌单管理、筛选 | 收藏与分类可用 |
| **P10 Android 脚手架** | Android 头项目配置、移动端布局校验、LibVLC Android 接入、`dotnet workload install android` 后构建 | Android 项目结构就绪 |

---

## 七、验证方式

1. **构建**：`dotnet build src/Music.Desktop` 每个阶段必须 0 error。
2. **运行**：`dotnet run --project src/Music.Desktop` 启动，确认无崩溃、界面渲染正常；用 PowerShell 截屏辅助核对视觉效果。
3. **播放**：调用 LibVLC 播放本地样例音频，验证进度/音量/切歌/结束自动下一首。
4. **缓存**：连续播放同一网络曲目两次，检查第二次不再发起下载、缓存目录与设置页占用数字一致。
5. **歌词广播**：启动广播服务，本机浏览器打开 `http://127.0.0.1:<port>/` 确认歌词随播放滚动；SSE 断线自动重连。
6. **Navidrome**：以用户提供的地址/账号实测 ping → 浏览 → 播放 → 封面（若用户暂无服务器，用 mock/跳过并标注）。
7. **FTP**：用测试 FTP 账号验证下载到缓存再播放、二次命中缓存。
8. **Android**：`dotnet workload install android` 后 `dotnet build src/Music.Android -f net10.0-android` 通过（视网络情况，可选）。

---

## 八、主要风险与应对

| 风险 | 应对 |
|---|---|
| libvlc 原生库加载失败（最高频） | 固定 x64、`Core.Initialize(AppContext.BaseDirectory)`、禁用单文件/AOT、构建后断言 `libvlc\win-x64\libvlc.dll` + `plugins\` 存在 |
| VLC 回调线程死锁 | 回调一律 `Dispatcher.UIThread.Post`，严格遵守停止/释放顺序 |
| 平台依赖泄漏进 Core | Core 只引托管包；原生包与 WinRT 只进 head；接口在 Core、实现在 head |
| Android workload 缺失阻塞整解决方案构建 | 日常只构建 `Music.Desktop`；Android 单独构建 |
| 「蓝牙推歌词」被误解 | 明确告知：AVRCP/SMTC 不含歌词，歌词走自建 SSE 网页；SMTC 仅传歌名封面且可降级 |
| Navidrome/FTP 无实测环境 | 客户端逻辑写全，用本地 mock 验证；无服务器处明确标注未实测 |

---

## 九、需用户后续提供（非阻塞）

- Navidrome 服务器地址 + 账号（用于 P7 实测；缺失则用 mock）
- FTP 服务器地址 + 账号（用于 P8 实测；缺失则用 mock）
- 是否需要在完成后执行 `dotnet workload install android`（下载较大）
