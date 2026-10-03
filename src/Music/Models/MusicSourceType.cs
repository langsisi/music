namespace Music.Models;

/// <summary>音源类型。</summary>
public enum MusicSourceType
{
    /// <summary>本地文件夹。</summary>
    Local = 0,

    /// <summary>FTP 服务器。</summary>
    Ftp = 1,

    /// <summary>Navidrome / Subsonic 服务器。</summary>
    Navidrome = 2,
}

/// <summary>音源扫描进度。<see cref="Total"/> 为 0 表示总数未知。</summary>
public readonly record struct ScanProgress(string Stage, int Current, int Total)
{
    public double Percent => Total > 0 ? Current * 100.0 / Total : 0;
}
