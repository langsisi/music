using System;
using System.Net.Http;
using Music.Models;

namespace Music.Services.Remote;

/// <summary>按音源配置创建远程文件客户端。</summary>
public interface IRemoteFileClientFactory
{
    IRemoteFileClient Create(MusicSourceConfig config);
}

public sealed class RemoteFileClientFactory : IRemoteFileClientFactory
{
    private readonly HttpClient _httpClient;

    public RemoteFileClientFactory(HttpClient httpClient) => _httpClient = httpClient;

    public IRemoteFileClient Create(MusicSourceConfig config) => config switch
    {
        FtpSourceConfig ftp => new FtpRemoteFileClient(ftp),
        SmbSourceConfig smb => new SmbRemoteFileClient(smb),
        WebDavSourceConfig webdav => new WebDavRemoteFileClient(webdav, _httpClient),
        _ => throw new NotSupportedException($"该音源不支持文件读写：{config.Type}"),
    };
}
