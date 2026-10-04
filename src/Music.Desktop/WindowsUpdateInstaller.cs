#if WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Music.Services.Update;

namespace Music.Desktop;

/// <summary>
/// Windows 自更新：起一个独立的 PowerShell 进程，等本进程退出后解压覆盖安装目录并重新启动，
/// 随后关闭当前实例，把文件锁让给升级脚本。
/// </summary>
public sealed class WindowsUpdateInstaller : IUpdateInstaller
{
    public bool CanSelfUpdate => true;

    public void Install(string packagePath)
    {
        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var executable = Environment.ProcessPath
            ?? Path.Combine(installDirectory, "Music.Desktop.exe");

        // 必须是独立进程：本进程退出后才能覆盖自己正在使用的文件。
        var command = string.Join(
            "; ",
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue",
            "Start-Sleep -Milliseconds 800",
            $"Expand-Archive -LiteralPath '{packagePath}' -DestinationPath '{installDirectory}' -Force",
            $"Start-Process -FilePath '{executable}'");

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        Process.Start(startInfo);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }
}
#endif