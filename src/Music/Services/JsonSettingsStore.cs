using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;

namespace Music.Services;

/// <summary>把 <see cref="AppSettings"/> 以 JSON 持久化到 <see cref="AppPaths.SettingsFile"/>。</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    // 序列化选项（WriteIndented / IgnoreReadOnlyProperties）由 SettingsJsonContext 的
    // [JsonSourceGenerationOptions] 表达；只保留真正可写的状态，DisplayName / Summary / Type
    // 这类只读计算属性不会被写进设置文件。
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return;
            }

            await using var stream = File.OpenRead(AppPaths.SettingsFile);
            var loaded = await JsonSerializer
                .DeserializeAsync(stream, SettingsJsonContext.Default.AppSettings, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is not null)
            {
                Current = loaded;
            }
        }
        catch (Exception)
        {
            // 设置文件损坏时退回默认值，不影响启动。
            Current = new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppPaths.EnsureCreated();

            // 先写临时文件再替换，避免写入中断导致设置文件损坏。
            var tempFile = AppPaths.SettingsFile + ".tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer
                    .SerializeAsync(stream, Current, SettingsJsonContext.Default.AppSettings, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempFile, AppPaths.SettingsFile, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
