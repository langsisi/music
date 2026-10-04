using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Music.Models;
using Music.Services;
using Music.Services.Dialogs;
using Music.Services.Library;
using Music.Services.Sources;

namespace Music.ViewModels.Pages;

public partial class SourcesViewModel : PageViewModel
{
    /// <summary>FTP 默认端口。</summary>
    private const decimal DefaultFtpPort = 21;

    /// <summary>SMB 默认端口（仅 445 直连 / 139 NetBIOS 有效）。</summary>
    private const int DefaultSmbPort = 445;

    private readonly ISettingsStore _settings;
    private readonly IFilePickerService _picker;
    private readonly LibrarySyncService _sync;
    private readonly ILibraryStore _libraryStore;
    private readonly IMusicSourceFactory _sourceFactory;

    /// <summary>正在编辑的音源；为 null 表示当前是「新增」。</summary>
    private MusicSourceConfig? _editing;

    public SourcesViewModel(
        ISettingsStore settings,
        IFilePickerService picker,
        LibrarySyncService sync,
        ILibraryStore libraryStore,
        IMusicSourceFactory sourceFactory)
    {
        _settings = settings;
        _picker = picker;
        _sync = sync;
        _libraryStore = libraryStore;
        _sourceFactory = sourceFactory;

        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSources));
    }

    public override string Title => "音源";

    public override string Description => "添加与管理本地文件夹、FTP、SMB、WebDAV 与 Navidrome 音源。";

    public ObservableCollection<MusicSourceConfig> Sources => _settings.Current.Sources;

    public bool HasSources => Sources.Count > 0;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    /// <summary>选择文件夹并加入本地音源，随后立即扫描入库。</summary>
    [RelayCommand]
    private async Task AddLocalFolderAsync()
    {
        var folders = await _picker.PickFoldersAsync("选择音乐文件夹");
        if (folders.Count == 0)
        {
            return;
        }

        var config = Sources.OfType<LocalSourceConfig>().FirstOrDefault();
        if (config is null)
        {
            config = new LocalSourceConfig();
            Sources.Add(config);
        }

        foreach (var folder in folders)
        {
            if (!config.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                config.Folders.Add(folder);
            }
        }

        // DisplayName / Summary 是计算属性，添加文件夹后需要让列表刷新。
        await _settings.SaveAsync();
        RefreshSourceList();

        await Sync(config);
    }

    // ---------------- 远程音源编辑（FTP / SMB / WebDAV / Navidrome 共用一套表单） ----------------

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private MusicSourceType _editorType;

    partial void OnEditorTypeChanged(MusicSourceType value)
    {
        OnPropertyChanged(nameof(ShowPort));
        OnPropertyChanged(nameof(ShowRootPath));
        OnPropertyChanged(nameof(ShowShareName));
        OnPropertyChanged(nameof(ShowDomain));
        OnPropertyChanged(nameof(ShowSmbPortHint));
        OnPropertyChanged(nameof(ShowWebDavRootHint));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(AddressLabel));
        OnPropertyChanged(nameof(AddressPlaceholder));
    }

    /// <summary>端口：FTP（21）与 SMB（445 / 139）才有意义。</summary>
    public bool ShowPort => EditorType is MusicSourceType.Ftp or MusicSourceType.Smb;

    /// <summary>起始目录：三种文件协议都有。</summary>
    public bool ShowRootPath =>
        EditorType is MusicSourceType.Ftp or MusicSourceType.Smb or MusicSourceType.WebDav;

    public bool ShowShareName => EditorType == MusicSourceType.Smb;

    public bool ShowDomain => EditorType == MusicSourceType.Smb;

    /// <summary>SMB 只支持 445 / 139，给出提示避免填写无效端口。</summary>
    public bool ShowSmbPortHint => EditorType == MusicSourceType.Smb;

    /// <summary>WebDAV 起始目录是 URL 路径而非 NAS 文件系统路径，给出提示。</summary>
    public bool ShowWebDavRootHint => EditorType == MusicSourceType.WebDav;

    public string EditorTitle => EditorType switch
    {
        MusicSourceType.Ftp => "FTP 服务器",
        MusicSourceType.Smb => "SMB 共享",
        MusicSourceType.WebDav => "WebDAV 服务器",
        _ => "Navidrome 服务器",
    };

    public string AddressLabel =>
        EditorType is MusicSourceType.Ftp or MusicSourceType.Smb ? "主机地址" : "服务器地址";

    public string AddressPlaceholder => EditorType switch
    {
        MusicSourceType.Ftp => "ftp.example.com",
        MusicSourceType.Smb => "192.168.1.10",
        MusicSourceType.WebDav => "https://dav.example.com/dav",
        _ => "https://music.example.com",
    };

    [ObservableProperty]
    private string _editorName = string.Empty;

    [ObservableProperty]
    private string _editorAddress = string.Empty;

    [ObservableProperty]
    private decimal? _editorPort = DefaultFtpPort;

    [ObservableProperty]
    private string _editorUser = string.Empty;

    [ObservableProperty]
    private string _editorPassword = string.Empty;

    [ObservableProperty]
    private string _editorRootPath = "/";

    /// <summary>SMB 共享名。</summary>
    [ObservableProperty]
    private string _editorShareName = string.Empty;

    /// <summary>SMB 域 / 工作组，本地账号留空。</summary>
    [ObservableProperty]
    private string _editorDomain = string.Empty;

    [ObservableProperty]
    private string _editorStatusText = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    [RelayCommand]
    private void AddNavidrome() => OpenEditor(MusicSourceType.Navidrome, null);

    [RelayCommand]
    private void AddFtp() => OpenEditor(MusicSourceType.Ftp, null);

    [RelayCommand]
    private void AddSmb() => OpenEditor(MusicSourceType.Smb, null);

    [RelayCommand]
    private void AddWebDav() => OpenEditor(MusicSourceType.WebDav, null);

    [RelayCommand]
    private void EditSource(MusicSourceConfig source)
    {
        switch (source)
        {
            case NavidromeSourceConfig:
                OpenEditor(MusicSourceType.Navidrome, source);
                break;
            case FtpSourceConfig:
                OpenEditor(MusicSourceType.Ftp, source);
                break;
            case SmbSourceConfig:
                OpenEditor(MusicSourceType.Smb, source);
                break;
            case WebDavSourceConfig:
                OpenEditor(MusicSourceType.WebDav, source);
                break;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editing = null;
        IsEditorOpen = false;
        EditorStatusText = string.Empty;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!TryBuildSource(out var built, out var error))
        {
            EditorStatusText = error;
            return;
        }

        IsTesting = true;
        EditorStatusText = "正在连接服务器…";
        try
        {
            if (_sourceFactory.Create(built) is not IConnectionTestableMusicSource source)
            {
                EditorStatusText = "该音源不支持连接测试。";
                return;
            }

            await source.TestConnectionAsync();
            EditorStatusText = "连接成功，可以保存了。";
        }
        catch (Exception ex)
        {
            EditorStatusText = ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveSourceAsync()
    {
        if (!TryBuildSource(out var built, out var error))
        {
            EditorStatusText = error;
            return;
        }

        MusicSourceConfig config;
        if (_editing is null)
        {
            config = built;
            Sources.Add(config);
        }
        else
        {
            // 复用原 Id，已收藏曲目的关联不会断。
            config = _editing;
            CopyInto(_editing, built);
            RefreshSourceList();
        }

        _editing = null;
        IsEditorOpen = false;
        EditorStatusText = string.Empty;

        await _settings.SaveAsync();
        await Sync(config);
    }

    private void OpenEditor(MusicSourceType type, MusicSourceConfig? source)
    {
        _editing = source;
        EditorType = type;

        var navidrome = source as NavidromeSourceConfig;
        var ftp = source as FtpSourceConfig;
        var smb = source as SmbSourceConfig;
        var webDav = source as WebDavSourceConfig;

        EditorName = source?.Name ?? string.Empty;
        EditorAddress = navidrome?.BaseUrl ?? ftp?.Host ?? smb?.Host ?? webDav?.BaseUrl ?? string.Empty;
        EditorUser = navidrome?.UserName ?? ftp?.UserName ?? smb?.UserName ?? webDav?.UserName ?? string.Empty;
        EditorPassword = navidrome?.Password ?? ftp?.Password ?? smb?.Password ?? webDav?.Password ?? string.Empty;
        EditorPort = type switch
        {
            MusicSourceType.Ftp => ftp?.Port ?? (int)DefaultFtpPort,
            MusicSourceType.Smb => smb?.Port ?? DefaultSmbPort,
            _ => null,
        };

        var root = ftp?.RootPath ?? smb?.RootPath ?? webDav?.RootPath;
        EditorRootPath = string.IsNullOrWhiteSpace(root) ? "/" : root;
        EditorShareName = smb?.ShareName ?? string.Empty;
        EditorDomain = smb?.Domain ?? string.Empty;
        EditorStatusText = string.Empty;
        IsEditorOpen = true;
    }

    /// <summary>把编辑结果写回已有配置对象，保持对象实例与 Id 不变。</summary>
    private static void CopyInto(MusicSourceConfig target, MusicSourceConfig source)
    {
        target.Name = source.Name;

        switch (target)
        {
            case NavidromeSourceConfig navidrome when source is NavidromeSourceConfig edited:
                navidrome.BaseUrl = edited.BaseUrl;
                navidrome.UserName = edited.UserName;
                navidrome.Password = edited.Password;
                break;

            case FtpSourceConfig ftp when source is FtpSourceConfig editedFtp:
                ftp.Host = editedFtp.Host;
                ftp.Port = editedFtp.Port;
                ftp.UserName = editedFtp.UserName;
                ftp.Password = editedFtp.Password;
                ftp.RootPath = editedFtp.RootPath;
                break;

            case SmbSourceConfig smb when source is SmbSourceConfig editedSmb:
                smb.Host = editedSmb.Host;
                smb.Port = editedSmb.Port;
                smb.ShareName = editedSmb.ShareName;
                smb.RootPath = editedSmb.RootPath;
                smb.UserName = editedSmb.UserName;
                smb.Password = editedSmb.Password;
                smb.Domain = editedSmb.Domain;
                break;

            case WebDavSourceConfig webDav when source is WebDavSourceConfig editedWebDav:
                webDav.BaseUrl = editedWebDav.BaseUrl;
                webDav.RootPath = editedWebDav.RootPath;
                webDav.UserName = editedWebDav.UserName;
                webDav.Password = editedWebDav.Password;
                break;
        }
    }

    /// <summary>把表单内容整理成音源配置，并做最基本的校验。</summary>
    private bool TryBuildSource(out MusicSourceConfig config, out string error)
    {
        config = null!;
        error = string.Empty;

        var address = EditorAddress?.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            error = EditorType is MusicSourceType.Ftp or MusicSourceType.Smb
                ? "请填写主机地址。"
                : "请填写服务器地址。";
            return false;
        }

        // SMB 允许空用户名以支持来宾共享 / 匿名访问。
        if (EditorType != MusicSourceType.Smb && string.IsNullOrWhiteSpace(EditorUser))
        {
            error = "请填写用户名。";
            return false;
        }

        return EditorType switch
        {
            MusicSourceType.Ftp => TryBuildFtp(address, out config, out error),
            MusicSourceType.Smb => TryBuildSmb(address, out config, out error),
            MusicSourceType.WebDav => TryBuildWebDav(address, out config, out error),
            _ => TryBuildNavidrome(address, out config, out error),
        };
    }

    private bool TryBuildNavidrome(string address, out MusicSourceConfig config, out string error)
    {
        config = null!;
        error = string.Empty;

        if (!address.Contains("://", StringComparison.Ordinal))
        {
            address = "http://" + address;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "服务器地址格式不正确，例如 https://music.example.com。";
            return false;
        }

        config = new NavidromeSourceConfig
        {
            Name = string.IsNullOrWhiteSpace(EditorName) ? "Navidrome" : EditorName.Trim(),
            BaseUrl = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}",
            UserName = EditorUser.Trim(),
            Password = EditorPassword ?? string.Empty,
        };

        return true;
    }

    private bool TryBuildFtp(string address, out MusicSourceConfig config, out string error)
    {
        config = null!;
        error = string.Empty;

        if (!TryNormalizeHost(address, "ftp.example.com", out var host, out error))
        {
            return false;
        }

        config = new FtpSourceConfig
        {
            Name = string.IsNullOrWhiteSpace(EditorName) ? "FTP 服务器" : EditorName.Trim(),
            Host = host,
            Port = EditorPort is null ? (int)DefaultFtpPort : (int)decimal.Round(EditorPort.Value),
            UserName = EditorUser.Trim(),
            Password = EditorPassword ?? string.Empty,
            RootPath = NormalizeRoot(EditorRootPath),
        };

        return true;
    }

    private bool TryBuildSmb(string address, out MusicSourceConfig config, out string error)
    {
        config = null!;
        error = string.Empty;

        if (!TryNormalizeHost(address, "192.168.1.10", out var host, out error))
        {
            return false;
        }

        var share = EditorShareName?.Trim() ?? string.Empty;
        if (share.Length == 0)
        {
            error = "请填写共享名，例如 music。";
            return false;
        }

        // SMB 只支持 445（直连 TCP）与 139（NetBIOS），其余值归一化为 445。
        var port = EditorPort is null ? DefaultSmbPort : (int)decimal.Round(EditorPort.Value);
        if (port != 139)
        {
            port = DefaultSmbPort;
        }

        config = new SmbSourceConfig
        {
            Name = string.IsNullOrWhiteSpace(EditorName) ? "SMB 共享" : EditorName.Trim(),
            Host = host,
            Port = port,
            ShareName = share,
            RootPath = NormalizeRoot(EditorRootPath),
            UserName = EditorUser?.Trim() ?? string.Empty,
            Password = EditorPassword ?? string.Empty,
            Domain = EditorDomain?.Trim() ?? string.Empty,
        };

        return true;
    }

    private bool TryBuildWebDav(string address, out MusicSourceConfig config, out string error)
    {
        config = null!;
        error = string.Empty;

        if (!address.Contains("://", StringComparison.Ordinal))
        {
            address = "https://" + address;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "服务器地址格式不正确，例如 https://dav.example.com/dav。";
            return false;
        }

        config = new WebDavSourceConfig
        {
            Name = string.IsNullOrWhiteSpace(EditorName) ? "WebDAV" : EditorName.Trim(),
            BaseUrl = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}",
            RootPath = NormalizeRoot(EditorRootPath),
            UserName = EditorUser.Trim(),
            Password = EditorPassword ?? string.Empty,
        };

        return true;
    }

    /// <summary>把用户输入的主机地址归一化成纯主机名（允许粘贴带 scheme 的完整地址）。</summary>
    private static bool TryNormalizeHost(string address, string example, out string host, out string error)
    {
        error = string.Empty;
        host = address;

        if (!address.Contains("://", StringComparison.Ordinal))
        {
            return true;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            error = $"主机地址格式不正确，例如 {example}。";
            return false;
        }

        host = uri.Host;
        return true;
    }

    /// <summary>起始目录统一成以 '/' 开头。</summary>
    private static string NormalizeRoot(string? root)
    {
        var value = string.IsNullOrWhiteSpace(root) ? "/" : root.Trim();
        return value.StartsWith('/') ? value : "/" + value;
    }

    // ---------------- 同步与移除 ----------------

    [RelayCommand]
    private async Task Sync(MusicSourceConfig config)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        try
        {
            // Progress<T> 在构造时捕获 UI 同步上下文，因此回调会自动回到 UI 线程。
            var progress = new Progress<ScanProgress>(report =>
            {
                StatusText = report.Total > 0
                    ? $"{report.Stage} {report.Current}/{report.Total}"
                    : $"{report.Stage} {report.Current}";
                ProgressValue = report.Percent;
            });

            var result = await _sync.SyncAsync(config, progress);
            StatusText = result.CoverCount > 0
                ? $"{config.DisplayName}：已导入 {result.TrackCount} 首曲目，封面 {result.CoverCount} 张"
                : $"{config.DisplayName}：已导入 {result.TrackCount} 首曲目（未取到封面）";
            ProgressValue = 100;
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Remove(MusicSourceConfig config)
    {
        Sources.Remove(config);
        await _libraryStore.RemoveSourceTracksAsync(config.Id);
        await _settings.SaveAsync();
        StatusText = $"已移除 {config.DisplayName}";
    }

    /// <summary>重建集合以刷新 DisplayName / Summary 等计算属性。</summary>
    private void RefreshSourceList()
    {
        var snapshot = Sources.ToList();
        Sources.Clear();
        foreach (var source in snapshot)
        {
            Sources.Add(source);
        }
    }
}
