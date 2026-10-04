# 音源新增 SMB / WebDAV + 刮削写回音源

## Context（为什么做）

现状：音源只有 本地文件夹 / FTP / Navidrome 三种；在线元数据刮削（选网易云/QQ 等候选后应用）**只把封面写进 `AppPaths.CoversDir`、歌词写进 `AppPaths.LyricsDir` 缓存，明确不改动音乐文件**。

需求：
1. 音源增加 **SMB** 与 **WebDAV**（支持扫描入库 + 播放取流，与 FTP 同级）。
2. 除 Navidrome 外，刮削时把**封面与歌词直接写回音源本身**——用户选择「写入音频文件标签」（参照 MusicTagWeb 的做法：把封面/歌词内嵌进音频文件标签），慢可接受，但必须异步、不阻塞 UI；并加一个设置开关，默认开启。

MusicTagWeb 的做法（已核实）：它把封面、歌词等直接写入音频文件标签（内嵌），作业在服务端批量执行；它本身不通过 SMB/WebDAV 协议访问文件，而是让用户把 NAS 目录挂载进容器。本方案取「内嵌写标签」这一点，SMB/WebDAV 则做成真正的协议音源。

## 关键决策（已确认）

- **SMB 库**：`SMBLibrary` **1.5.8.1**（纯托管、netstandard2.0、无原生依赖，跨桌面/Android/iOS 可用）。仅支持 SMB2/2.1/3.0，不支持 SMB1 老设备；**无自定义端口重载**，只能 445（DirectTCP）或 139（NetBIOS）。
- **WebDAV**：不引第三方库，用 `HttpClient` 实现（PROPFIND / GET / PUT / MKCOL + Basic）。
- **取流**：FTP/SMB/WebDAV 一律「整文件下载到缓存再本地播放」（SMB 客户端同步且会话进程内；WebDAV 凭据无法安全交给 VLC）。WebDAV 不走直连。
- **写回内容**：只写 **封面 + 歌词**（不改标题/歌手/专辑，避免与「先 ApplyAsync 再 SaveMetadataAsync」的顺序耦合）。本地/远端都内嵌进音频标签，并额外写/传一个同名 `.lrc`（与 `LocalLrcProvider` 的命名规则一致）。
- **开关**：`AppSettings.ScrapeWriteBack`，默认 `true`；老 settings.json 无反序列化为默认值即开启。
- **抽象**：把 FTP 专用抽象一次性泛化为协议无关的 `Remote` 抽象（调用面仅 ~8 个文件，机械改动），后续维护成本更低。

## 实施步骤

### 1. 包 / 枚举 / 配置 / JSON
- [Directory.Packages.props](file:///e:/Code/Avalonia/Music/Directory.Packages.props)：加 `SMBLibrary 1.5.8.1`。
- [Music.csproj](file:///e:/Code/Avalonia/Music/src/Music/Music.csproj)：加 `<PackageReference Include="SMBLibrary" />`。
- [MusicSourceType.cs](file:///e:/Code/Avalonia/Music/src/Music/Models/MusicSourceType.cs)：追加 `Smb = 4`、`WebDav = 5`（**勿改 0–3，值会落库**）。
- [MusicSourceConfig.cs](file:///e:/Code/Avalonia/Music/src/Music/Models/MusicSourceConfig.cs)：加两个 `[JsonDerivedType]`（"smb" / "webdav"）与派生类：
  - `SmbSourceConfig`：Host / Port(445) / ShareName / RootPath("/") / UserName / Password / Domain；`Summary` = `\\host\share\RootPath`。
  - `WebDavSourceConfig`：BaseUrl / RootPath("/") / UserName / Password；`Summary` = BaseUrl。
  - 二者 `CanEdit => true`。
- [AppSettings.cs](file:///e:/Code/Avalonia/Music/src/Music/Models/AppSettings.cs)：加 `public bool ScrapeWriteBack { get; set; } = true;`。
- [AppJsonContext.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/AppJsonContext.cs)：**无需显式登记**——现有 FTP/Navidrome 也仅靠基类 `[JsonDerivedType]` 可达且工作正常，源生成器会自动纳入派生类型。作为运行时验证点之一。

### 2. 远程文件客户端泛化（`Music.Services.Ftp` → `Music.Services.Remote`）
- 新增 `Remote/IRemoteFileClient.cs`：`RemoteEntry(Path,Size,IsDirectory)` + `IRemoteFileClient`（ConnectAsync / ListAsync / DownloadAsync / UploadAsync），形状与现有 `IFtpFileClient` 完全一致。
- 新增 `Remote/IRemoteFileClientFactory.cs`：`Create(MusicSourceConfig)`，实现 `RemoteFileClientFactory`（switch 分派，注入独立 HttpClient）。
- `FluentFtpFileClient.cs` → 迁为 `Remote/FtpRemoteFileClient.cs`（逻辑不变，仅改命名空间/接口名）。
- 删除 `Ftp/IFtpFileClient.cs`、`Ftp/IFtpFileClientFactory.cs`、`Ftp/FluentFtpFileClient.cs`。
- 同步替换引用：`SqliteAudioCache`、`OnlineDownloadService`、`FtpMusicSource`、`IMusicSourceFactory`、`ServiceCollectionExtensions`（注册 `IRemoteFileClientFactory`）。

### 3. SMB / WebDAV 实现
- 新增 `Remote/SmbRemoteFileClient.cs`（SMBLibrary）：`SMB2Client.Connect(host, DirectTCPTransport)` → `Login(domain,user,pass)` → `TreeConnect(share, out ISMBFileStore)`；列目录用递归 `CreateFile(FILE_DIRECTORY_FILE)` + `QueryDirectory(FileDirectoryInformation)`；读 `ReadFile` 分块（≤ `MaxReadSize`）→ 目标流；写 `CreateFile(FILE_OVERWRITE_IF)` + `WriteFile` 分块（≤ `MaxWriteSize`）；上传前逐级建目录；`DisposeAsync` 做 `CloseFile`/`Disconnect`/`Logoff`。
  - SMBLibrary 全同步 → 所有调用包 `Task.Run`，`SMB2Client` 非线程安全，一个实例一个客户端，用后释放。
  - 注意 `SMBLibrary.FileAttributes` 与 `System.IO.FileAttributes` 同名，需 `using SmbFileAttributes = SMBLibrary.FileAttributes;`。
  - 路径：接口层统一用 `/`，实现内转 `\` 并去前导 `\`（相对共享根）。
- 新增 `Remote/WebDavRemoteFileClient.cs`（纯 HttpClient）：
  - `ConnectAsync` = `PROPFIND Depth:0` 探活。
  - `ListAsync` = 逐层 `PROPFIND Depth:1`（**不用 infinity**）+ 207 XML 解析（`XmlReader` 按 `LocalName` 匹配，`href` 用 `new Uri(dirUri, href)` 解析）。
  - `DownloadAsync` = GET 流式；`UploadAsync` = 逐级 `MKCOL`（201/405 视为成功）+ `PUT`（200/201/204 成功）。
  - 每请求带 `Authorization: Basic`；`BaseUrl` 校验 scheme ∈ http/https。
  - 「连接测试」复用 `ConnectAsync`。
- SMB/WebDAV 的 HttpClient 建议用**独立、无 5 分钟超时**的实例，避免大文件上传超时。

### 4. 音源扫描 + 取流 + 下载目标
- 新增 `Sources/RemoteFileMusicSource.cs`：把 [FtpMusicSource.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Sources/FtpMusicSource.cs) 泛化为「递归列目录 + 按扩展名登记曲目」（标题=文件名、专辑=父目录名、`Id=TrackKey.Create(configId, remotePath)`、`RemoteId=远端路径`、`Path=展示 URI`）。FTP/SMB/WebDAV 共用，`Path` 分别生成 `ftp://`、`smb://host/share/...`、`{BaseUrl}{RootPath}/...`。
- [IMusicSourceFactory.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Sources/IMusicSourceFactory.cs)：switch 增加 SMB/WebDAV 分支（走 `RemoteFileMusicSource`），FTP 也改走它。
- [CachedMediaResolver.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Media/CachedMediaResolver.cs)：把「FTP 走整文件缓存」扩展为 `Ftp or Smb or WebDav`。
- [SqliteAudioCache.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Cache/SqliteAudioCache.cs)：`DownloadFtpAsync` 改名 `DownloadRemoteAsync`，配置查找改按 `Id` 找 `MusicSourceConfig`，switch 覆盖 Ftp/Smb/WebDav。
- [OnlineDownloadService.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Online/OnlineDownloadService.cs)：`GetTargets` 纳入已配置的 SMB/WebDAV；`UploadToFtpAsync` 泛化为 `UploadToRemoteAsync(MusicSourceConfig)`，返回对应展示地址；Navidrome 分支保持抛「不支持上传」。

### 5. 写回服务 + 刮削集成
- 新增 `Metadata/AudioTagWriter.cs`：`Write(path, coverBytes, lyrics, title?, artist?, album?)`（null=不改该字段），TagLib 写内嵌封面（FrontCover，含 MIME 探测）+ 内嵌歌词；失败静默返回 false。
  - [OnlineDownloadService.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Online/OnlineDownloadService.cs) 的私有 `ApplyTags`/`DetectImageMime` 改为调用它，去重。
- 新增 `Metadata/SourceWriteBackService.cs`：`WriteBackAsync(Track, coverBytes, lyrics, ct)`：
  - 内部先读 `_settings.Current.ScrapeWriteBack`，关闭则跳过。
  - **Local**：`Task.Run(() => AudioTagWriter.Write(track.Path, ...))` + 写同目录同名 `.lrc`（`Path.ChangeExtension(track.Path, ".lrc")`）。
  - **FTP/SMB/WebDAV**：下载原文件到 `AppPaths.CacheDir` 临时文件 → `Task.Run(TagLib 改写)` → 覆盖上传 → 上传同名 `.lrc`；`try/finally` 清理临时文件。
  - **Navidrome/Online**：跳过。
  - 全程 `catch` 降级为 `Failed` 文案，不抛出；加 ~120s 超时兜底，避免 UI 永久停在「正在应用…」。
- [MetadataScrapeService.cs](file:///e:/Code/Avalonia/Music/src/Music/Services/Metadata/MetadataScrapeService.cs)：构造注入 `SourceWriteBackService`；`ApplyAsync` 在封面/歌词保存并落库后调用写回，把结果并入返回文案（如「已应用网易云的封面和歌词。已写回音源。」/「…（已缓存到本地）。写回音源失败：…」）；更新类注释（不再是「不改动音乐文件」）。
  - 不阻塞 UI：TagLib 走 `Task.Run`，网络为异步；`PlayerViewModel.ApplySearchResult` 本就 await，UI 线程不阻塞。

### 6. UI
- [SettingsView.axaml](file:///e:/Code/Avalonia/Music/src/Music/Views/Pages/SettingsView.axaml)：
  - 音源「添加」菜单加「SMB 共享」「WebDAV」两项。
  - 编辑表单把 `IsFtpEditor` 硬编码改为 `Sources.ShowPort` / `ShowRootPath` / `ShowShareName` / `ShowDomain`；新增共享名、域两个输入行（仅 SMB）。
  - 新增设置行「刮削时写回音源」（ToggleSwitch 绑 `SettingsViewModel.ScrapeWriteBack`）。
- [SourcesViewModel.cs](file:///e:/Code/Avalonia/Music/src/Music/ViewModels/Pages/SourcesViewModel.cs)：删除 `IsFtpEditor`，改为 4 个可见性计算属性；`EditorTitle/AddressLabel/AddressPlaceholder` 支持 4 种类型；新增 `EditorShareName/EditorDomain` 字段与 `AddSmb`/`AddWebDav` 命令；`EditSource`/`OpenEditor`/`CopyInto`/`TryBuildSource` 增加 SMB/WebDAV 分支与 `TryBuildSmb`/`TryBuildWebDav`（SMB 允许空用户名以支持来宾共享；WebDAV 校验 http/https）。
- [SettingsViewModel.cs](file:///e:/Code/Avalonia/Music/src/Music/ViewModels/Pages/SettingsViewModel.cs)：照 `BroadcastEnabled` 模式加 `ScrapeWriteBack` 属性 + `_loading` 守卫 + `Save()`。

## 涉及的既有文件（改动清单）
`Directory.Packages.props`、`Music.csproj`、`MusicSourceType.cs`、`MusicSourceConfig.cs`、`AppSettings.cs`、`SqliteAudioCache.cs`、`CachedMediaResolver.cs`、`OnlineDownloadService.cs`、`FtpMusicSource.cs`（→`RemoteFileMusicSource.cs`）、`IMusicSourceFactory.cs`、`ServiceCollectionExtensions.cs`、`MetadataScrapeService.cs`、`SourcesViewModel.cs`、`SettingsViewModel.cs`、`SettingsView.axaml`；新增 `Services/Remote/*`、`Services/Metadata/AudioTagWriter.cs`、`Services/Metadata/SourceWriteBackService.cs`；删除 `Services/Ftp/*`。

## 验证
1. **编译（硬门槛）**：`dotnet build src/Music/Music.csproj -c Debug` 与 `dotnet build src/Music.Desktop/Music.Desktop.csproj -c Debug` 均 0 错误（后者同时校验 XAML 编译绑定：可见性属性/开关名拼错会直接编译失败）。
2. **多态 JSON**：手工在 `settings.json` 加一条 `{"type":"smb",...}` 与一条 `{"type":"webdav",...}`，重启确认不崩、`DisplayName`/`Summary` 正确。
3. **开关**：关闭 `ScrapeWriteBack` 后应用候选，确认本地封面/歌词缓存仍生效、不写回。
4. **真机（桌面）**：SMB→Windows 共享或 NAS（本地账号 Domain 留空）：添加→测试连接→同步→播放（确认 `AppPaths.CacheDir` 生成文件）→刮削后远端音频标签含封面+歌词且同目录出现 `.lrc`。WebDAV→Nextcloud / IIS：验证列目录、MKCOL、PUT，以及 `href` 相对/绝对两种形式。
5. **边界**：SMB 密码/共享名错误、WebDAV 401/403/405/409 时 UI 只显示可读错误，本地缓存不受影响。

## 风险
- SMB 仅 SMB2+，无自定义端口（UI 提示，其余值归一化为 445）。
- SMB 默认响应超时 5s，大文件分块读写；写回给 120s 总超时。
- WebDAV 服务器差异大（禁 infinity、IIS 30MB 上传限制、仅 Basic），失败静默降级并提示用应用密码 + HTTPS。
- 远端写回需「下载→改写→上传」，流量/耗时放大（用户已接受）。
- AOT/裁剪：当前桌面 `PublishAot=false`、iOS `TrimMode=partial`，不阻断；将来若开全量 AOT 需为 SMBLibrary 加 trimmer 配置。
