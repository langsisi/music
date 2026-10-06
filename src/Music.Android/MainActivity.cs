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

    /// <summary>通知权限（Android 13+）的请求码。</summary>
    private const int NotificationPermissionRequestCode = 1002;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        RequestStoragePermission();
        RequestNotificationPermission();
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

        RequestPermissionIfNeeded(permission, StoragePermissionRequestCode);
    }

    /// <summary>
    /// Android 13+ 发通知需要 POST_NOTIFICATIONS 授权，用于通知栏 / 锁屏的「正在播放」控制条。
    /// 未授权时仅不显示控制条，播放本身不受影响。
    /// </summary>
    private void RequestNotificationPermission()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        RequestPermissionIfNeeded(Manifest.Permission.PostNotifications, NotificationPermissionRequestCode);
    }

    private void RequestPermissionIfNeeded(string permission, int requestCode)
    {
        if (CheckSelfPermission(permission) == Permission.Granted)
        {
            return;
        }

        RequestPermissions([permission], requestCode);
    }
}
