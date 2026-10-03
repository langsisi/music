# Music

用 **Avalonia 12** 写的跨平台音乐播放器。桌面端为主，同一套界面通过断点自动适配手机；音源支持**本地文件夹 / FTP / Navidrome（Subsonic）**。

## 功能

- **现代化界面**：深浅两套语义化配色、渐变强调色、圆角卡片；窄屏自动切换成「底部标签栏 + 全屏播放页」，桌面端为「左侧导航 + 底部播放条」。
- **三种音源**：本地文件夹、FTP、Navidrome，统一的添加 / 编辑 / 测试连接 / 同步入口。
- **播放**：基于 LibVLC（几乎支持所有格式），队列、上一首 / 下一首、随机、单曲循环、音量与拖动进度。
- **歌词**：LRC 解析 + 逐行高亮自动滚动；Windows 下推送歌名 / 封面到系统媒体中心（SMTC），蓝牙耳机与车机可显示并遥控。
- **收藏与分类**：收藏曲目，自建多个分类并归类，曲库页按「全部 / 收藏 / 分类」筛选。
- **缓存**：可设置容量上限，播放过的网络曲目落盘复用，二次播放不再下载；超出上限按 LRU 淘汰。
- **在线升级**：从静态服务器拉取版本清单，校验 SHA256 后覆盖安装并自动重启。

## 技术栈

| 用途 | 包 | 版本 |
|---|---|---|
| UI 框架 | Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent / Avalonia.Fonts.Inter | 12.1.3 |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| 音频引擎（托管层） | LibVLCSharp | 3.10.1 |
| 原生 libvlc（Windows / Android） | VideoLAN.LibVLC.Windows / VideoLAN.LibVLC.Android | 3.0.24 / 3.6.5 |
| 本地元数据 | TagLibSharp | 2.3.0 |
| FTP | FluentFTP | 55.0.0 |
| 本地索引（收藏 / 分类 / 缓存） | Microsoft.Data.Sqlite | 10.0.12 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | 10.0.12 |

版本集中在 `Directory.Packages.props` 里管理（Central Package Management）。

> libvlc 与 LibVLCSharp 的主版本必须匹配（libvlc 3.x 配 LibVLCSharp 3.x），混用会抛 `VLCException`。

## 项目结构

```
Music.slnx
├─ src/Music/            (net10.0)                            共享核心：模型 / 服务 / VM / View / 样式
├─ src/Music.Desktop/    (net10.0-windows…;net10.0, win-x64)   桌面头
├─ src/Music.Android/    (net10.0-android)                     Android 头
└─ scripts/              发布脚本
```

核心层的主要目录：

| 目录 | 内容 |
|---|---|
| `Models/` | `Track`、`MusicSourceConfig`、`AppSettings`、`LyricDocument`、`UpdateManifest` 等 |
| `Services/Sources/` | 音源抽象与三种实现、`LibrarySyncService`、`NavidromeClient` |
| `Services/Ftp/` | FTP 客户端抽象与 FluentFTP 实现 |
| `Services/Audio/` | `VlcAudioPlayer`、`PlaybackService` |
| `Services/Cache/` | SQLite + 文件系统实现的音频缓存 |
| `Services/Lyrics/` | LRC 解析、歌词来源、高亮服务 |
| `Services/Broadcast/` | 局域网歌词广播（TcpListener + SSE 网页） |
| `Services/Update/` | 在线升级 |
| `ViewModels/`、`Views/`、`Styles/` | 界面 |

**分层铁律**：`Music`（核心）只引用跨平台的托管程序集，**绝不**引用 `VideoLAN.LibVLC.Windows` 或任何 WinRT API；原生库只放在各 head 项目里，平台能力通过核心层定义的接口（`ISystemMediaService`、`IFtpFileClient` 等）由 head 提供实现。

## 快速开始

### 环境要求

- **.NET 10 SDK**
- Windows 上运行需安装 **.NET 10 Desktop Runtime**（除非用自包含方式发布）
- 国内网络建议使用 NuGet 镜像：

  ```powershell
  dotnet restore -s https://nuget.azure.cn/v3/index.json
  ```

### 构建与运行

桌面头是多目标的，**必须带 `-f`**：

```powershell
dotnet build src\Music.Desktop\Music.Desktop.csproj -c Debug -f net10.0-windows10.0.19041.0
dotnet run --project src\Music.Desktop -f net10.0-windows10.0.19041.0
```

### 数据目录

默认 `%LOCALAPPDATA%\Music`；若该目录不可写会自动退回临时目录。可用环境变量 `MUSIC_APP_DATA_DIR` 覆盖（便携模式 / 测试）。

```
settings.json    应用设置与音源配置（密码目前以明文保存）
library.db       曲目索引 + 收藏 / 分类 / 缓存索引（SQLite）
cache/           音频缓存
covers/          从标签或服务器导出的封面
```

## 使用说明

### 添加音源

在「音源」页：

- **添加本地文件夹**：选目录后立即扫描入库，用 TagLibSharp 读取标签并导出内嵌封面。扫描的扩展名白名单见 [AudioFileTypes.cs](src/Music/Services/AudioFileTypes.cs)。
- **添加 FTP**：填主机、端口、用户名、密码、起始目录。FTP 没有元数据接口，**标题取文件名、专辑取所在目录名**。FTP 无 Range 语义，播放时会**先整文件下载到缓存**再本地播放。
- **添加 Navidrome**：填服务器地址（如 `https://music.example.com`）、用户名、密码。同步时会拉取全部专辑与曲目，**每张专辑只下载一次封面**并复用到该专辑所有曲目；歌词走 OpenSubsonic 的 `getLyricsBySongId`。

保存前可以点「测试连接」验证地址与账号。

### 收藏与分类

- 曲库列表每行右侧有心形按钮，点击即收藏 / 取消；播放条上的心形同效。
- 顶部胶囊可在「全部 / 收藏 / 各分类」之间切换。
- 输入分类名点「新建分类」即可新建并自动切过去；列表行上的「分类」按钮（有分类时才出现）打开弹层勾选归属；选中某个分类时胶囊右侧出现删除按钮。

### 缓存

「设置 → 缓存」可调上限（`0` 表示不缓存）与查看占用、清理缓存。策略：

| 音源 | 策略 |
|---|---|
| 本地文件 | 直接播放，不进缓存 |
| HTTP（Navidrome） | 直连播放（支持 Range，可拖动进度），同时后台落盘，下次命中 |
| FTP | 必须先下载到缓存再播放 |

写入是原子的（写 `.tmp` → 校验 → 改名），所以不会播到半截文件。

### 歌词与「蓝牙推歌词」

需要说明一个协议层面的限制：**蓝牙 AVRCP 与 Windows 的 SMTC 都不承载歌词**，能推送的只有歌名 / 艺术家 / 专辑 / 封面 / 播放状态。因此做成了两条路径：

1. **系统媒体中心（Windows）**：推送歌名、封面并接管媒体键。蓝牙耳机、车机能显示歌名并遥控，但**不会**滚动显示歌词。非 MSIX 打包的应用在部分 Windows 版本上会话可见性不保证，不可用时会静默降级，不影响播放。
2. **局域网歌词广播**：「设置 → 歌词广播」开启后，用手机浏览器打开显示的地址，即可跟随播放逐行显示歌词（内置网页 + SSE 实时推送）。手机连蓝牙音箱、同时打开该网页，就是「歌词跟随蓝牙设备」的可用方案。

### 在线升级

「设置 → 在线升级」填入版本清单地址即可。工作流程：

1. GET 清单 JSON，比较版本号（当前版本取自程序集的 `Version`）；
2. 有新版则下载 zip，若清单给了 `sha256` 就校验，不匹配直接丢弃；
3. 启动一个独立进程等待本应用退出 → 用 `Expand-Archive` 覆盖安装目录 → 重新启动。

清单格式（键名大小写不敏感，带不带 BOM 都能解析）：

```json
{
  "version": "1.1.0",
  "url": "Music-1.1.0.zip",
  "sha256": "20d234efe3b36211a549a6b15770e7d47df75e4faecedb640a53abb611582fce",
  "notes": "新增歌词广播"
}
```

`url` 既可以写完整地址，也可以写**相对清单所在目录**的文件名（上例即与清单同目录），后者方便把清单和安装包放在同一个静态目录。

## 发布新版本

### 一键发布

```powershell
# 精简包（目标机需已装 .NET 10 桌面运行时），清单里写相对文件名
.\scripts\publish-release.ps1 -Version 1.1.0 -Notes "新增歌词广播"

# 自包含包（体积更大，免装运行时），清单里写完整地址
.\scripts\publish-release.ps1 -Version 1.1.0 -SelfContained `
    -FeedBaseUrl https://gitee.com/your-name/music/raw/master -Notes "修复若干问题"
```

脚本会做这些事：`dotnet publish`（用 `-p:Version` 注入版本号）→ 剔除 libvlc 里非当前架构的目录 → 剔除 pdb 符号文件（省约 100MB）→ 校验 zip 根目录有 `Music.Desktop.exe` → 打包 → 计算 SHA256 → 生成 `latest.json`。

产物在 `artifacts/`（已被 `.gitignore` 忽略）：

```
artifacts/Music-<版本>.zip    约 67MB，解压后覆盖安装目录
artifacts/latest.json         上传后把直链填到应用「设置 → 在线升级」
```

手动发布等价于：

```powershell
dotnet publish src\Music.Desktop -c Release -f net10.0-windows10.0.19041.0 `
    -r win-x64 --self-contained false -p:Version=1.1.0 -o publish
```

> **最容易踩的坑**：发布时必须让版本号进到程序集（脚本已用 `-p:Version` 处理好）。否则应用始终认为自己是 `1.0.0`，永远检测不到更新。

### 上传到 Gitee / GitCode 等静态服务器

客户端只做「GET 一个 JSON + GET 一个 zip」，不需要 API、鉴权或服务端逻辑，所以任何能直链下载的地方都行（GitHub / Gitee / GitCode / 对象存储 / Cloudflare Pages…）。Gitee 与 GitCode 仓库里文件的 **Raw 直链**即可，不需要 Gitee Pages。

注意事项：

1. **单文件上限 100MB**：Gitee 免费仓库单文件上限 100MB，本项目的包约 67MB（已剔除 pdb 与多余架构）。再加平台会超限，建议 zip 放**发行版附件**或对象存储。
2. **先验证直链**：用无痕浏览器确认能直接下载（有些平台会跳转或限制未登录下载），再填进应用。
3. **zip 必须把文件放在根目录**，不能多套一层——升级脚本是「解压到安装目录」，多一层就覆盖不到。脚本已自动校验。
4. **覆盖只覆盖同名文件，不删除旧文件**：新版删掉的 dll 会残留在安装目录（对 .NET 应用一般无害）。
5. **安装目录必须可写**：若装在 `C:\Program Files` 会因权限失败，建议装到 `%LOCALAPPDATA%\Programs\Music` 这类用户目录。
6. `scripts/publish-release.ps1` 保存为 **UTF-8 with BOM**（Windows PowerShell 5.1 需要 BOM 才能正确读中文），编辑时请勿改成 ANSI 或无 BOM 的 UTF-8。

## Android

Android 头已完成依赖注入接入与 libvlc 原生库引用（`VideoLAN.LibVLC.Android`），界面复用同一套 View 的自适应布局；但**尚未在本机编译验证过**，构建需要具备：

- `dotnet workload install android`
- **Android SDK**（含 API 36 平台，可参考 <https://aka.ms/dotnet-android-install-sdk>）
- **JDK 17+**（必须是完整 JDK，仅 JRE 不行；可设 `JavaSdkDirectory` 指定路径）

```powershell
dotnet build src\Music.Android\Music.Android.csproj -f net10.0-android
```

注意：Android 上的 `UpdateService.CanSelfUpdate` 为 `false`（无法自我覆盖安装），升级走应用商店。`Music.Android` 已加入解决方案，因此 `dotnet build Music.slnx` 需要上述前置条件齐全；日常只构建桌面头即可。

## 已知限制

- **蓝牙歌词**：如上一节所述，协议本身不承载歌词，只能推歌名 / 封面；歌词请用内置的局域网广播网页。
- **Navidrome / FTP 需要真实服务器联调**：仓库里没有可用的测试服务器，这两条链路是按其协议规范实现并用本地假实现验证的，首次对接真实服务器时建议先用「测试连接」确认。
- **FTP 无标签元数据**：标题取文件名、专辑取目录名。
- **SMTC 可见性**：非打包应用在部分 Windows 版本上不保证系统媒体中心能显示会话，不可用时静默降级。
- **升级**：只覆盖同名文件（不删旧文件），没有增量更新、断点续传与自动回滚。
- **尚未实现**：歌单（Playlists 表已预留）、搜索联想、桌面歌词悬浮窗。
- **同一账号的密码目前以明文保存在 `settings.json` 中**，仅用于向对应服务器认证。
