using System.Text.Json.Serialization;
using Music.Models;
using Music.Services.Broadcast;
using Music.Services.Update;

namespace Music.Services;

/// <summary>
/// <c>settings.json</c> 的 System.Text.Json 源生成元数据，替代反射序列化以保证 AOT / 裁剪可用。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

/// <summary>在线升级（版本清单与内置更新配置）的源生成元数据。</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateConfig))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext
{
}

/// <summary>歌词广播 SSE 负载的源生成元数据；输出为 camelCase，与原匿名类型一致。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TrackEventPayload))]
[JsonSerializable(typeof(LineEventPayload))]
internal sealed partial class BroadcastJsonContext : JsonSerializerContext
{
}