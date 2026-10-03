using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using Music.Services.Sources;

namespace Music.Services.Lyrics;

/// <summary>
/// Navidrome 曲目的歌词：走 OpenSubsonic 的 <c>getLyricsBySongId</c>。
/// 老版本服务器不支持该扩展时静默返回 null，不影响播放。
/// </summary>
public sealed class NavidromeLyricsProvider : ILyricsProvider
{
    private readonly ISettingsStore _settings;
    private readonly HttpClient _httpClient;

    public NavidromeLyricsProvider(ISettingsStore settings, HttpClient httpClient)
    {
        _settings = settings;
        _httpClient = httpClient;
    }

    public bool CanHandle(Track track)
        => track.SourceType == MusicSourceType.Navidrome
           && !string.IsNullOrEmpty(track.RemoteId);

    public async Task<string?> GetRawLyricsAsync(Track track, CancellationToken cancellationToken = default)
    {
        // 认证信息来自音源配置，因此需要按 SourceId 反查。
        var config = _settings.Current.Sources
            .OfType<NavidromeSourceConfig>()
            .FirstOrDefault(source => source.Id == track.SourceId);

        if (config is null)
        {
            return null;
        }

        try
        {
            var client = new NavidromeClient(config, _httpClient);
            return await client.GetLyricsAsync(track.RemoteId!, cancellationToken).ConfigureAwait(false);
        }
        catch (NavidromeException)
        {
            return null;
        }
    }
}
