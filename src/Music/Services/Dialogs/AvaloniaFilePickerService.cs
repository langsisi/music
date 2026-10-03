using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Music.Services.Dialogs;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickFoldersAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return [];
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
        });

        return folders
            .Select(folder => folder.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }

    private static TopLevel? GetTopLevel()
    {
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                return desktop.MainWindow;

            case ISingleViewApplicationLifetime singleView when singleView.MainView is not null:
                return TopLevel.GetTopLevel(singleView.MainView);

            default:
                return null;
        }
    }
}
