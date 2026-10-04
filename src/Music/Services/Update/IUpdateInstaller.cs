namespace Music.Services.Update;

/// <summary>
/// 把下载好的升级包落地安装。各平台实现不同：
/// Windows 解压覆盖安装目录后重启；Android 拉起系统安装器让用户确认安装。
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>当前平台是否支持应用内自更新。</summary>
    bool CanSelfUpdate { get; }

    /// <summary>安装已下载的升级包。安装动作发起后即可返回（应用可能随之退出）。</summary>
    void Install(string packagePath);
}