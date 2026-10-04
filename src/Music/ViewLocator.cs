using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Music.ViewModels;
using Music.ViewModels.Pages;
using Music.Views.Pages;

namespace Music;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
/// <remarks>
/// 使用显式 <c>switch</c> 映射而非反射（<c>Type.GetType</c> + <c>Activator.CreateInstance</c>），
/// 以保证 AOT / 裁剪后仍能正确解析页面视图。
/// </remarks>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            HomeViewModel => new HomeView(),
            LibraryViewModel => new LibraryView(),
            DiscoverViewModel => new DiscoverView(),
            SettingsViewModel => new SettingsView(),
            _ => new TextBlock
            {
                Text = "Not Found: " + param.GetType().FullName?.Replace("ViewModel", "View", StringComparison.Ordinal),
            },
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}