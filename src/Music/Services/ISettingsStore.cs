using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services;

public interface ISettingsStore
{
    /// <summary>当前生效的设置（内存中的唯一实例）。</summary>
    AppSettings Current { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
