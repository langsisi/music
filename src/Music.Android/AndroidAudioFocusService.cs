using System;
using System.Runtime.Versioning;
using Android.Media;
using Android.OS;
using Music.Services.Audio;

namespace Music.Android;

/// <summary>
/// Android 音频焦点：申请媒体焦点后，系统会在导航播报 / 来电等场景回调本服务。
/// <see cref="AudioFocus.LossTransientCanDuck"/> 交由上层压低音量（闪避），
/// <see cref="AudioFocus.LossTransient"/> 交由上层暂停播放，播报 / 通话结束后恢复。
/// </summary>
/// <remarks>
/// 与 <see cref="AndroidSystemMediaService"/> 一致：任何平台异常都静默降级，不影响播放。
/// </remarks>
public sealed class AndroidAudioFocusService
    : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener, IAudioFocusService
{
    private readonly AudioManager? _audioManager;
    private readonly AudioAttributes? _attributes;
    private readonly Handler _handler = new(Looper.MainLooper!);

    /// <summary>API 26+ 的焦点请求对象；低版本走旧接口，保持为 null。</summary>
    private AudioFocusRequestClass? _request;

    public AndroidAudioFocusService()
    {
        _audioManager = global::Android.App.Application.Context?
            .GetSystemService(global::Android.Content.Context.AudioService) as AudioManager;

        var attributes = new AudioAttributes.Builder();
        attributes.SetUsage(AudioUsageKind.Media);
        attributes.SetContentType(AudioContentType.Music);
        _attributes = attributes.Build();
    }

    public event EventHandler<AudioFocusChange>? FocusChanged;

    public bool RequestFocus()
    {
        if (_audioManager is null)
        {
            return true;
        }

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                _request ??= BuildRequest();
                return _audioManager.RequestAudioFocus(_request!) == AudioFocusRequest.Granted;
            }

            return RequestLegacyFocus();
        }
        catch (Exception)
        {
            // 个别 ROM 的 AudioManager 实现会抛异常，静默降级为「无焦点协商」。
            return false;
        }
    }

    public void AbandonFocus()
    {
        if (_audioManager is null)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                if (_request is not null)
                {
                    _audioManager.AbandonAudioFocusRequest(_request);
                }

                return;
            }

            AbandonLegacyFocus();
        }
        catch (Exception)
        {
            // 忽略。
        }
    }

    public void OnAudioFocusChange(AudioFocus focusChange)
    {
        var change = focusChange switch
        {
            AudioFocus.Gain => AudioFocusChange.Gain,
            AudioFocus.Loss => AudioFocusChange.Loss,
            AudioFocus.LossTransient => AudioFocusChange.LossTransient,
            AudioFocus.LossTransientCanDuck => AudioFocusChange.LossTransientCanDuck,
            _ => (AudioFocusChange?)null,
        };

        if (change is null)
        {
            return;
        }

        // 回调来自系统线程，统一切回主线程再抛给上层。
        _handler.Post(() => FocusChanged?.Invoke(this, change.Value));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AbandonFocus();
            _request?.Dispose();
            _request = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// <c>SetWillPauseWhenDucked(false)</c> 是关键：告诉系统「被闪避时我会自己降音量、不必暂停」，
    /// 于是导航播报这类 <c>AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK</c> 请求会下发
    /// LossTransientCanDuck（可闪避）而不是 LossTransient（必须暂停）。
    /// </summary>
    [SupportedOSPlatform("android26.0")]
    private AudioFocusRequestClass BuildRequest()
    {
        var builder = new AudioFocusRequestClass.Builder(AudioFocus.Gain);
        builder.SetAudioAttributes(_attributes!);
        builder.SetWillPauseWhenDucked(false);
        builder.SetOnAudioFocusChangeListener(this, _handler);
        return builder.Build()!;
    }

    /// <summary>API 26 起已废弃的旧式焦点申请，仅低版本设备走这里。</summary>
    [UnsupportedOSPlatform("android26.0")]
    private bool RequestLegacyFocus()
        => _audioManager!.RequestAudioFocus(this, Stream.Music, AudioFocus.Gain)
            == AudioFocusRequest.Granted;

    /// <summary>API 26 起已废弃的旧式焦点释放，仅低版本设备走这里。</summary>
    [UnsupportedOSPlatform("android26.0")]
    private void AbandonLegacyFocus() => _audioManager!.AbandonAudioFocus(this);
}