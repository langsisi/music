using System;
using System.Net.Http;
using Music.Models;
using Music.Services.Ftp;

namespace Music.Services.Sources;

public interface IMusicSourceFactory
{
    IMusicSource Create(MusicSourceConfig config);
}

public sealed class MusicSourceFactory : IMusicSourceFactory
{
    private readonly HttpClient _httpClient;
    private readonly IFtpFileClientFactory _ftpClientFactory;

    public MusicSourceFactory(HttpClient httpClient, IFtpFileClientFactory ftpClientFactory)
    {
        _httpClient = httpClient;
        _ftpClientFactory = ftpClientFactory;
    }

    public IMusicSource Create(MusicSourceConfig config) => config switch
    {
        LocalSourceConfig local => new LocalMusicSource(local),
        FtpSourceConfig ftp => new FtpMusicSource(ftp, _ftpClientFactory),
        NavidromeSourceConfig navidrome => new NavidromeMusicSource(navidrome, _httpClient),
        _ => throw new NotSupportedException($"暂不支持的音源类型：{config.Type}"),
    };
}
