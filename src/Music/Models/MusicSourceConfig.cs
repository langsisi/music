using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Music.Models;

/// <summary>
/// 音源配置基类。使用 System.Text.Json 多态序列化，<c>type</c> 字段区分具体类型。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LocalSourceConfig), "local")]
[JsonDerivedType(typeof(FtpSourceConfig), "ftp")]
[JsonDerivedType(typeof(NavidromeSourceConfig), "navidrome")]
[JsonDerivedType(typeof(SmbSourceConfig), "smb")]
[JsonDerivedType(typeof(WebDavSourceConfig), "webdav")]
public abstract class MusicSourceConfig
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public abstract MusicSourceType Type { get; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name;

    /// <summary>界面上展示的副标题（地址 / 目录等）。</summary>
    [JsonIgnore]
    public abstract string Summary { get; }

    /// <summary>是否支持在界面上编辑连接参数（本地文件夹直接增删目录，不需要）。</summary>
    [JsonIgnore]
    public virtual bool CanEdit => false;

    [JsonIgnore]
    protected abstract string DefaultName { get; }
}

/// <summary>本地文件夹音源。</summary>
public sealed class LocalSourceConfig : MusicSourceConfig
{
    public List<string> Folders { get; set; } = [];

    public override MusicSourceType Type => MusicSourceType.Local;

    protected override string DefaultName => "本地音乐";

    public override string Summary => Folders.Count switch
    {
        0 => "尚未添加文件夹",
        1 => Folders[0],
        _ => $"{Folders[0]} 等 {Folders.Count} 个文件夹",
    };
}

/// <summary>FTP 音源。</summary>
public sealed class FtpSourceConfig : MusicSourceConfig
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 21;

    public string UserName { get; set; } = string.Empty;

    /// <summary>注意：目前以明文保存在本地设置文件中。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>起始目录，默认为根目录。</summary>
    public string RootPath { get; set; } = "/";

    public override MusicSourceType Type => MusicSourceType.Ftp;

    public override bool CanEdit => true;

    protected override string DefaultName => "FTP 服务器";

    public override string Summary => string.IsNullOrWhiteSpace(Host)
        ? "尚未配置服务器"
        : $"ftp://{Host}:{Port}{RootPath}";
}

/// <summary>Navidrome（Subsonic API）音源。</summary>
public sealed class NavidromeSourceConfig : MusicSourceConfig
{
    /// <summary>形如 https://music.example.com 。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    /// <summary>注意：目前以明文保存在本地设置文件中。</summary>
    public string Password { get; set; } = string.Empty;

    public override MusicSourceType Type => MusicSourceType.Navidrome;

    public override bool CanEdit => true;

    protected override string DefaultName => "Navidrome";

    public override string Summary => string.IsNullOrWhiteSpace(BaseUrl)
        ? "尚未配置服务器"
        : BaseUrl;
}

/// <summary>SMB / CIFS 共享音源。</summary>
public sealed class SmbSourceConfig : MusicSourceConfig
{
    public string Host { get; set; } = string.Empty;

    /// <summary>SMB 只支持 445（直连 TCP）与 139（NetBIOS），其他值不生效。</summary>
    public int Port { get; set; } = 445;

    /// <summary>共享名，如 <c>music</c>。</summary>
    public string ShareName { get; set; } = string.Empty;

    /// <summary>共享内的起始目录，默认为根目录。</summary>
    public string RootPath { get; set; } = "/";

    public string UserName { get; set; } = string.Empty;

    /// <summary>注意：目前以明文保存在本地设置文件中。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>域 / 工作组，本地账号留空。</summary>
    public string Domain { get; set; } = string.Empty;

    public override MusicSourceType Type => MusicSourceType.Smb;

    public override bool CanEdit => true;

    protected override string DefaultName => "SMB 共享";

    public override string Summary => string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(ShareName)
        ? "尚未配置服务器"
        : $@"\\{Host}\{ShareName}{RootPath}";
}

/// <summary>WebDAV 音源。</summary>
public sealed class WebDavSourceConfig : MusicSourceConfig
{
    /// <summary>形如 https://dav.example.com/dav 。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>BaseUrl 下的起始目录，默认为根目录。</summary>
    public string RootPath { get; set; } = "/";

    public string UserName { get; set; } = string.Empty;

    /// <summary>注意：目前以明文保存在本地设置文件中。</summary>
    public string Password { get; set; } = string.Empty;

    public override MusicSourceType Type => MusicSourceType.WebDav;

    public override bool CanEdit => true;

    protected override string DefaultName => "WebDAV";

    public override string Summary => string.IsNullOrWhiteSpace(BaseUrl)
        ? "尚未配置服务器"
        : BaseUrl;
}
