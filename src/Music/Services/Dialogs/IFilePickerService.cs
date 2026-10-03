using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Dialogs;

/// <summary>平台选择器抽象（桌面为系统文件夹对话框，移动端后续接入 SAF）。</summary>
public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickFoldersAsync(string title, CancellationToken cancellationToken = default);
}
