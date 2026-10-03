using Avalonia;
using Avalonia.Styling;
using Music.Models;

namespace Music.Services;

/// <summary>应用主题切换。</summary>
public static class ThemeService
{
    public static void Apply(AppThemeMode mode)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
