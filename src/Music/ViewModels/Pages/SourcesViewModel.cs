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

    public override string Description => "添加与管理本地文件夹、FTP 与 Navidrome 音源。";

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

    // ---------------- 远程音源编辑（Navidrome / FTP 共用一套表单） ----------------

    [ObservableProperty]
    private bool _isEditorOpen;

    [ObservableProperty]
    private MusicSourceType _editorType;

    partial void OnEditorTypeChanged(MusicSourceType value)
    {
        OnPropertyChanged(nameof(IsFtpEditor));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(AddressLabel));
        OnPropertyChanged(nameof(AddressPlaceholder));
    }

    public bool IsFtpEditor => EditorType == MusicSourceType.Ftp;

    public string EditorTitle => IsFtpEditor ? "FTP 服务器" : "Navidrome 服务器";

    public string AddressLabel => IsFtpEditor ? "主机地址" : "服务器地址";

    public string AddressPlaceholder => IsFtpEditor ? "ftp.example.com" : "https://music.example.com";

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

    [ObservableProperty]
    private string _editorStatusText = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    [RelayCommand]
    private void AddNavidrome() => OpenEditor(MusicSourceType.Navidrome, null);

    [RelayCommand]
    private void AddFtp() => OpenEditor(MusicSourceType.Ftp, null);

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

        EditorName = source?.Name ?? string.Empty;
        EditorAddress = navidrome?.BaseUrl ?? ftp?.Host ?? string.Empty;
        EditorUser = navidrome?.UserName ?? ftp?.UserName ?? string.Empty;
        EditorPassword = navidrome?.Password ?? ftp?.Password ?? string.Empty;
        EditorPort = ftp?.Port ?? (int)DefaultFtpPort;
        EditorRootPath = string.IsNullOrWhiteSpace(ftp?.RootPath) ? "/" : ftp.RootPath;
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
            error = IsFtpEditor ? "请填写主机地址。" : "请填写服务器地址。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(EditorUser))
        {
            error = "请填写用户名。";
            return false;
        }

        return IsFtpEditor
            ? TryBuildFtp(address, out config, out error)
            : TryBuildNavidrome(address, out config, out error);
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

        // 允许直接粘贴 ftp://host:port 形式。
        var host = address;
        if (host.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            {
                error = "主机地址格式不正确，例如 ftp.example.com。";
                return false;
            }

            host = uri.Host;
        }

        var root = string.IsNullOrWhiteSpace(EditorRootPath) ? "/" : EditorRootPath.Trim();
        if (!root.StartsWith('/'))
        {
            root = "/" + root;
        }

        config = new FtpSourceConfig
        {
            Name = string.IsNullOrWhiteSpace(EditorName) ? "FTP 服务器" : EditorName.Trim(),
            Host = host,
            Port = EditorPort is null ? (int)DefaultFtpPort : (int)decimal.Round(EditorPort.Value),
            UserName = EditorUser.Trim(),
            Password = EditorPassword ?? string.Empty,
            RootPath = root,
        };

        return true;
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

            var count = await _sync.SyncAsync(config, progress);
            StatusText = $"{config.DisplayName}：已导入 {count} 首曲目";
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
