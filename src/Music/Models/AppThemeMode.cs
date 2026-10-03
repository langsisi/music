namespace Music.Models;

/// <summary>界面主题模式。</summary>
public enum AppThemeMode
{
    /// <summary>深色（默认）。</summary>
    Dark,

    /// <summary>浅色。</summary>
    Light,

    /// <summary>跟随系统。</summary>
    System,
}

/// <summary>供设置页下拉框使用的主题选项。</summary>
public sealed record ThemeOption(AppThemeMode Mode, string Name);
