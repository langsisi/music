using System;
using System.IO;
using Android.Content;
using AndroidX.Core.Content;
using Music.Services.Update;

namespace Music.Android;

/// <summary>
/// Android 自更新：把下载好的 apk 复制到 FileProvider 可共享的目录，
/// 再以 content:// 形式拉起系统安装器，由用户在系统界面确认安装。
/// </summary>
public sealed class AndroidUpdateInstaller : IUpdateInstaller
{
    private const string MimeTypeApk = "application/vnd.android.package-archive";

    public bool CanSelfUpdate => true;

    public void Install(string packagePath)
    {
        var context = global::Android.App.Application.Context!;
        var shareable = CopyToShareableDirectory(context, packagePath);

        var authority = $"{context.PackageName}.fileprovider";
        var uri = FileProvider.GetUriForFile(context, authority, new Java.IO.File(shareable))
            ?? throw new InvalidOperationException("无法生成安装包的共享地址。");

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, MimeTypeApk);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);

        context.StartActivity(intent);
    }

    /// <summary>把包复制到应用私有 files/updates 下（file_paths.xml 已声明该目录可共享）。</summary>
    private static string CopyToShareableDirectory(Context context, string packagePath)
    {
        var directory = Path.Combine(context.FilesDir!.AbsolutePath, "updates");
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, Path.GetFileName(packagePath));
        if (string.Equals(Path.GetFullPath(packagePath), Path.GetFullPath(target), StringComparison.Ordinal))
        {
            return target;
        }

        File.Copy(packagePath, target, overwrite: true);
        return target;
    }
}