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
# 默认：包上传到 GitCode Release，清单里的 url 自动指向 <ReleaseBaseUrl>/v<版本>/Music-<版本>.zip
.\scripts\publish-release.ps1 -Version 1.1.0 -Notes "新增歌词广播"

# 自包含包（体积更大，免装运行时）
.\scripts\publish-release.ps1 -Version 1.1.0 -SelfContained -Notes "修复若干问题"

# Tag 与包版本不一致时显式指定（默认 Tag = v<版本>）
.\scripts\publish-release.ps1 -Version 1.1.0 -Tag v1.0.0

# 清单与安装包放在同一个静态目录时，改成写相对文件名
.\scripts\publish-release.ps1 -Version 1.1.0 -ReleaseBaseUrl '' -Notes "说明"

# 换到别的托管平台
.\scripts\publish-release.ps1 -Version 1.1.0 `
    -ReleaseBaseUrl https://gitee.com/your-name/music/releases/download -Notes "说明"
```

脚本会做这些事：`dotnet publish`（用 `-p:Version` 注入版本号）→ 剔除 libvlc 里非当前架构的目录 → 剔除 pdb 符号文件（省约 100MB）→ 校验 zip 根目录有 `Music.Desktop.exe` → 打包 → 计算 SHA256 → **把 `latest.json` 写到仓库根目录**。

产物：

```
artifacts\Music-<版本>.zip   约 67MB，上传到 Release 附件（artifacts/ 已被 .gitignore 忽略）
latest.json                  仓库根目录，提交推送后即可被应用读取
```

以 `-Version 1.1.0` 为例，生成的清单是：

```json
{
  "version": "1.1.0",
  "url": "https://gitcode.com/evoq58/music/releases/download/v1.1.0/Music-1.1.0.zip",
  "sha256": "6de8d237b10db9a7da98bef39b354da85453d84fee269ae9bcc8da0259d573f7",
  "notes": "新增歌词广播"
}
```

> **sha256 必须对应你实际上传的那个包**。zip 里含文件时间戳，**重新跑一次脚本产出的包哈希就会变**，所以：改了代码重新发布时，要用本次产出的 zip 覆盖 Release 上的同名文件，否则应用会因「校验失败」丢弃更新。想复核远端包的哈希，可以下载后自己算：
>
> ```powershell
> Invoke-WebRequest 'https://gitcode.com/<owner>/<repo>/releases/download/<tag>/Music-<版本>.zip' -OutFile "$env:TEMP\uploaded.zip"
> (Get-FileHash "$env:TEMP\uploaded.zip" -Algorithm SHA256).Hash.ToLower()
> ```

手动发布等价于：

```powershell
dotnet publish src\Music.Desktop -c Release -f net10.0-windows10.0.19041.0 `
    -r win-x64 --self-contained false -p:Version=1.1.0 -o publish
```

> **最容易踩的坑**：发布时必须让版本号进到程序集（脚本已用 `-p:Version` 处理好）。否则应用始终认为自己是 `1.0.0`，永远检测不到更新。

### 用 GitCode 做升级托管（本项目当前用法）

1. 跑脚本：`.\scripts\publish-release.ps1 -Version 1.1.0 -Notes "新增歌词广播"`
2. 在 GitCode 用 Tag `v1.1.0` 新建 Release，上传 `artifacts\Music-1.1.0.zip`
3. 提交并推送仓库根目录的 `latest.json`
4. 应用「设置 → 在线升级 → 更新地址」填：`https://raw.gitcode.com/evoq58/music/raw/master/latest.json`
5. **刚上传完要等一会儿**：GitCode 的 raw 与 Release 附件走 CDN，刚推送/刚上传时可能返回 403 / 404 或旧内容（实测踩过：清单已能推上去，但立刻检查更新会报 `403 (Forbidden)`，等几分钟就正常）。发布新版本后若客户端仍显示「已是最新版本」，多半也是缓存还没刷新。

注意 GitCode 的 raw 直链要走**独立域名 `raw.gitcode.com`**（`gitcode.com/<owner>/<repo>/raw/...` 对非浏览器客户端会返回网页外壳而不是文件内容），格式为：

```
https://raw.gitcode.com/{owner}/{repo}/raw/{branch}/{path}
```

### 上传到 Gitee / GitCode 等静态服务器

客户端只做「GET 一个 JSON + GET 一个 zip」，不需要 API、鉴权或服务端逻辑，所以任何能直链下载的地方都行（GitHub / Gitee / GitCode / 对象存储 / Cloudflare Pages…）。Gitee 与 GitCode 仓库里文件的 **Raw 直链**即可，不需要 Gitee Pages。

注意事项：

1. **单文件上限 100MB**：Gitee 免费仓库单文件上限 100MB，本项目的包约 67MB（已剔除 pdb 与多余架构）。再加平台会超限，建议 zip 放**发行版附件**或对象存储。
2. **先验证直链**：用无痕浏览器确认能直接下载（有些平台会跳转或限制未登录下载），再填进应用。清单要能返回 JSON 而不是 HTML。
3. **zip 必须把文件放在根目录**，不能多套一层——升级脚本是「解压到安装目录」，多一层就覆盖不到。脚本已自动校验。
4. **覆盖只覆盖同名文件，不删除旧文件**：新版删掉的 dll 会残留在安装目录（对 .NET 应用一般无害）。
5. **安装目录必须可写**：若装在 `C:\Program Files` 会因权限失败，建议装到 `%LOCALAPPDATA%\Programs\Music` 这类用户目录。
6. `scripts/publish-release.ps1` 保存为 **UTF-8 with BOM**（Windows PowerShell 5.1 需要 BOM 才能正确读中文），编辑时请勿改成 ANSI 或无 BOM 的 UTF-8。

## Android

Android 头已完成依赖注入接入与 libvlc 原生库引用（`VideoLAN.LibVLC.Android`），界面复用同一套 View 的自适应布局，**已实测可构建出可安装的 APK**。

### 前置条件

```powershell
# 1) Android 工作负载
dotnet workload install android

# 2) JDK 17+（必须是完整 JDK，仅 JRE 不行）
winget install Microsoft.OpenJDK.17
```

3) **Android SDK**：用 .NET 自带的安装目标下载，不要手动折腾命令行工具。注意 PowerShell 里 `-p:名字=值` **中间不能有空格**，值里含空格或变量时要把整个 `-p:...=...` 用双引号包起来：

```powershell
cd src\Music.Android
dotnet build -t:InstallAndroidDependencies -f net10.0-android `
    "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
    "-p:JavaSdkDirectory=C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot" `
    -p:AcceptAndroidSDKLicenses=True
```

装完把路径固化成用户级环境变量（原本为空时属纯新增），之后就不用再带参数：

```powershell
[Environment]::SetEnvironmentVariable('ANDROID_HOME', "$env:LOCALAPPDATA\Android\Sdk", 'User')
[Environment]::SetEnvironmentVariable('JAVA_HOME', 'C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot', 'User')
```

> 若安装目标报 `XA5300 找不到 Android SDK 目录` 或 `XARAT7001 NullReferenceException`，先确认 SDK 目录是否已经装好（它可能在报完这些警告之后才真正下载完）；目录里应有 `platforms\android-36`、`build-tools\36.0.0`、`platform-tools\adb.exe`、`licenses\android-sdk-license`。

### 构建与安装

```powershell
# Debug（自动用调试密钥签名，可直接装机）
# csproj 已默认按 arm64 真机构建（约 33MB，含 libvlc）
dotnet build src\Music.Android\Music.Android.csproj -c Debug -f net10.0-android
# 产物：src\Music.Android\bin\Debug\net10.0-android\<ApplicationId>-Signed.apk

# 装到手机（需开 USB 调试）
& "$env:ANDROID_HOME\platform-tools\adb.exe" install -r <apk路径>

# 模拟器（x86_64 AVD）构建时改架构：
dotnet build src\Music.Android\Music.Android.csproj -c Debug -f net10.0-android -p:RuntimeIdentifiers=android-x64
```

> ⚠️ **不要用单数 `-p:RuntimeIdentifier=...`**：`VideoLAN.LibVLC.Android` 的 targets 只认复数属性 `RuntimeIdentifiers`，用单数会构建出**缺 `libvlc.so`** 的 APK（能安装但无法播放）。csproj 里已固定 `<RuntimeIdentifiers>android-arm64</RuntimeIdentifiers>`，正常 `dotnet build` 即可。

**Release（分发给别人，必须自己签名）**：

```powershell
# 生成密钥库，一次即可，丢了就无法给同一个应用推更新
keytool -genkeypair -v -keystore music.keystore -alias music -keyalg RSA -keysize 2048 -validity 10000

dotnet publish src\Music.Android\Music.Android.csproj -c Release -f net10.0-android `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore=music.keystore `
    -p:AndroidSigningKeyAlias=music `
    -p:AndroidSigningKeyPass=你的密码 `
    -p:AndroidSigningStorePass=你的密码
```

**架构不匹配装不上？** 手机（如三星 W24 等 arm64 真机）装 x86_64 包会报「32 位应用不兼容」——那其实是在提示 APK 里的原生库与手机 CPU 架构不符。默认构建已是 arm64，无需额外参数；若曾用模拟器参数构建过，重装前先重新执行上面的默认构建。

### ApplicationId 怎么填

当前值：`zhusl.music`。

规则（Android 安装时会校验，不合法会报「解析软件包时出现问题」）：

- 至少两段，用点分隔；
- **每一段必须以字母开头**，只能含字母、数字、下划线；建议全小写（Java 包名约定）；
- 建议用「域名反转」：域名 `example.com` → `com.example.app`。

反例：`www.294713.xyz.music` 这种写法**构建能通过，但装到手机上会被系统拒绝**，因为 `294713` 这一段以数字开头。数字域名反转后如果出现数字开头的段，必须补一个字母，例如 `xyz.m294713.music`。

> ⚠️ 这个值一旦分发出去就不能再改：改了就是另一个应用，用户无法覆盖升级、数据也不通用。

### 签名是什么，为什么 Release 要自己签名

Android 要求**每个 APK 都必须带签名才能安装**。签名在这里的作用不是加密，而是两件事：

1. **完整性**：安装时系统用证书里的公钥校验 APK 有没有被改动过；
2. **身份连续性**：系统靠签名判断「这个新版本是不是同一个开发者发的」。**同一个应用升级必须用同一个密钥签名**，否则系统拒绝覆盖安装（只能卸载重装，用户数据会丢）。

因此：

| | 用的密钥 | 能否分发 |
|---|---|---|
| Debug 包（`dotnet build`） | Android 自动生成的 `debug.keystore`（在 `%USERPROFILE%\.android\debug.keystore`） | ❌ 任何人的电脑都能生成同签名的包 |
| Release 包（`dotnet publish`） | **你自己的 keystore** | ✅ |

「Release 要自己签名」就是指：你要生成并保管好一个属于自己的 keystore，发布时把路径与密码传给构建（就是上面那段 `-p:AndroidSigning*`）。

要点：

- **keystore 文件和密码务必备份**，丢了就再也无法给这个应用推更新；
- 不要把密码提交到仓库（上面示例是明文传参，正式项目应放 CI 的密钥里）；
- `ApplicationId` 和签名密钥一旦分发就都不能再变。

### 已知坑：Debug 包直接拷贝安装必闪退

症状：`adb install` 或拷贝 APK 安装后一打开就退，`logcat -b crash` 里能看到

```
Abort message: 'No assemblies found in ... .__override__ ... Assuming this is part of Fast Deployment. Exiting...'
```

根因：Debug 构建默认走 **Fast Deployment**——托管 DLL 不打进 APK，而是构建部署时由 MSBuild 通过 adb 推到设备的 `.__override__` 目录。脱离开发机的安装方式缺了这一步，启动时找不到程序集就自杀退出。**csproj 已用 `<EmbedAssembliesIntoApk>True</EmbedAssembliesIntoApk>` 强制把 DLL 打进 APK**，因此本项目构建出的 Debug 包可直接安装运行；若删除该属性，就必须用 `dotnet build -t:Install` 或 VS 部署到设备。

### 闪退了怎么排查

**方式一（不用数据线）**：Android 头已经内置崩溃日志，会写到

```
/sdcard/Android/data/<包名>/files/crash.log
```

即 `Android/data/zhusl.music/files/crash.log`。注意 Android 11 起多数系统（含三星）不允许普通文件管理器进入 `Android/data`，最可靠的取法还是数据线 + 方式二的 adb：

```powershell
& $adb pull /sdcard/Android/data/zhusl.music/files/crash.log D:\crash.log
```

复现一次闪退后把这个文件发出来，堆栈里会直接指出是哪一行抛的。另注意：**native 崩溃（段错误）不会写入此文件**，文件不存在或为空时改用方式二抓 `logcat -b crash`，那里的 abort message / backtrace 对 native 崩溃是一锤定音的。

**方式二（信息最全，需打开「开发者选项 → USB 调试」）**：

```powershell
$adb = "$env:ANDROID_HOME\platform-tools\adb.exe"
& $adb logcat -c                      # 清空旧日志
# 此时在手机上打开应用，等它闪退
& $adb logcat -d | Select-String -Pattern "AndroidRuntime|DOTNET|mono|FATAL" | Select-Object -Last 60
```

`FATAL EXCEPTION` 及其后面的 `Caused by` 就是根因；`DOTNET` / `mono` 关键字下是托管层未捕获异常的堆栈。

### 其它注意

- 构建时有 `XA0141` 警告：libvlc 3.6.5 的 `libvlc.so` 未按 16KB 页对齐，Android 16 起会要求，属于上游 NuGet 包的问题，目前可忽略。
- Android 上的 `UpdateService.CanSelfUpdate` 为 `false`（无法自我覆盖安装），升级走应用商店。
- `Music.Android` 已加入解决方案，因此 `dotnet build Music.slnx` 需要上述前置条件齐全；日常只构建桌面头即可。

## 已知限制

- **蓝牙歌词**：如上一节所述，协议本身不承载歌词，只能推歌名 / 封面；歌词请用内置的局域网广播网页。
- **Navidrome / FTP 需要真实服务器联调**：仓库里没有可用的测试服务器，这两条链路是按其协议规范实现并用本地假实现验证的，首次对接真实服务器时建议先用「测试连接」确认。
- **FTP 无标签元数据**：标题取文件名、专辑取目录名。
- **SMTC 可见性**：非打包应用在部分 Windows 版本上不保证系统媒体中心能显示会话，不可用时静默降级。
- **升级**：只覆盖同名文件（不删旧文件），没有增量更新、断点续传与自动回滚。
- **尚未实现**：歌单（Playlists 表已预留）、搜索联想、桌面歌词悬浮窗。
- **同一账号的密码目前以明文保存在 `settings.json` 中**，仅用于向对应服务器认证。
