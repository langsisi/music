using System;
using System.Net.Http;
using Music.Models;
using Music.Services.Remote;

namespace Music.Services.Sources;

public interface IMusicSourceFactory
{
    IMusicSource Create(MusicSourceConfig config);
}

public sealed class MusicSourceFactory : IMusicSourceFactory
{
    private readonly HttpClient _httpClient;
    private readonly IRemoteFileClientFactory _remoteClientFactory;

    public MusicSourceFactory(HttpClient httpClient, IRemoteFileClientFactory remoteClientFactory)
    {
        _httpClient = httpClient;
        _remoteClientFactory = remoteClientFactory;
    }

    public IMusicSource Create(MusicSourceConfig config) => config switch
    {
        LocalSourceConfig local => new LocalMusicSource(local),
        FtpSourceConfig ftp => new RemoteFileMusicSource(ftp, _remoteClientFactory),
        SmbSourceConfig smb => new RemoteFileMusicSource(smb, _remoteClientFactory),
        WebDavSourceConfig webDav => new RemoteFileMusicSource(webDav, _remoteClientFactory),
        NavidromeSourceConfig navidrome => new NavidromeMusicSource(navidrome, _httpClient),
        _ => throw new NotSupportedException($"暂不支持的音源类型：{config.Type}"),
    };
}
