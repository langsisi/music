using System;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace Music.Android;

[Activity(
    Label = "ZMusic",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>读取音乐文件运行时权限的请求码。</summary>
    private const int StoragePermissionRequestCode = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        RequestStoragePermission();
    }

    /// <summary>
    /// 申请读取音乐文件的权限，用于扫描本机音乐目录。
    /// Android 13+ 用细分后的 READ_MEDIA_AUDIO，低版本用 READ_EXTERNAL_STORAGE。
    /// </summary>
    private void RequestStoragePermission()
    {
        var permission = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;

        if (CheckSelfPermission(permission) == Permission.Granted)
        {
            return;
        }

        RequestPermissions([permission], StoragePermissionRequestCode);
    }
}
