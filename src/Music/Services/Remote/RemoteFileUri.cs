using Music.Models;

namespace Music.Services.Remote;

/// <summary>远端路径的展示地址拼装（FTP / SMB / WebDAV 共用）。</summary>
public static class RemoteFileUri
{
    /// <summary>用于界面展示的地址，取流时不使用该地址。路径以 '/' 分隔、相对音源根目录。</summary>
    public static string BuildDisplayUri(MusicSourceConfig config, string remotePath)
    {
        var suffix = remotePath.StartsWith('/') ? remotePath : "/" + remotePath;

        return config switch
        {
            FtpSourceConfig ftp => $"ftp://{ftp.Host}:{ftp.Port}{suffix}",
            SmbSourceConfig smb => $"smb://{smb.Host}/{smb.ShareName}{suffix}",
            WebDavSourceConfig webDav => $"{webDav.BaseUrl.TrimEnd('/')}{NormalizeRoot(webDav.RootPath)}{suffix}",
            _ => suffix,
        };
    }

    private static string NormalizeRoot(string rootPath) =>
        string.IsNullOrWhiteSpace(rootPath) || rootPath == "/" ? string.Empty : "/" + rootPath.Trim('/');
}
