using Music.Models;

namespace Music.Services.Ftp;

/// <summary>按音源配置创建 FTP 客户端。</summary>
public interface IFtpFileClientFactory
{
    IFtpFileClient Create(FtpSourceConfig config);
}

public sealed class FluentFtpFileClientFactory : IFtpFileClientFactory
{
    public IFtpFileClient Create(FtpSourceConfig config) => new FluentFtpFileClient(config);
}
