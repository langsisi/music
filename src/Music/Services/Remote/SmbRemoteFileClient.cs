using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Music.Models;
using SMBLibrary;
using SMBLibrary.Client;
using SMBLibrary.SMB2;
using SmbFileAttributes = SMBLibrary.FileAttributes;

namespace Music.Services.Remote;

/// <summary>
/// 基于 SMBLibrary 的 SMB2/3 客户端。SMBLibrary 全部是同步阻塞 API，
/// 因此每个方法都包在 <see cref="Task.Run(Action, CancellationToken)"/> 里，避免占用 UI 线程。
/// 仅支持 SMB2 及以上；端口只能走 445（直连）或 139（NetBIOS）。
/// </summary>
public sealed class SmbRemoteFileClient : IRemoteFileClient
{
    /// <summary>单次读写的最大分块（1 MB），SMBLibrary 建议不要超过 MaxReadSize / MaxWriteSize。</summary>
    private const int MaxChunkSize = 1 << 20;

    private readonly SmbSourceConfig _config;
    private SMB2Client? _client;
    private ISMBFileStore? _store;

    public SmbRemoteFileClient(SmbSourceConfig config) => _config = config;

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (_store is not null)
        {
            return;
        }

        var client = new SMB2Client();
        var transport = _config.Port == 139
            ? SMBTransportType.NetBiosOverTCP
            : SMBTransportType.DirectTCPTransport;

        if (!client.Connect(_config.Host, transport))
        {
            client.Disconnect();
            throw new IOException($"无法连接 SMB 服务器 {_config.Host}。");
        }

        var status = client.Login(_config.Domain ?? string.Empty, _config.UserName, _config.Password);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            client.Logoff();
            client.Disconnect();
            throw new IOException($"SMB 登录失败：{Describe(status)}");
        }

        var store = client.TreeConnect(_config.ShareName, out status);
        if (store is null || status != NTStatus.STATUS_SUCCESS)
        {
            client.Logoff();
            client.Disconnect();
            throw new IOException($"无法打开共享「{_config.ShareName}」：{Describe(status)}");
        }

        _client = client;
        _store = store;
    }, cancellationToken);

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        var entries = new List<RemoteEntry>();
        await Task.Run(
                () => ListRecursive(ToSmbPath(rootPath), entries, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new ScanProgress("SMB 目录已列出", entries.Count, entries.Count));
        return entries;
    }

    public async Task DownloadAsync(
        string remotePath,
        Stream destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await Task.Run(() =>
        {
            var store = _store!;
            var path = ToSmbPath(remotePath);

            var status = store.CreateFile(
                out var handle,
                out _,
                path,
                AccessMask.GENERIC_READ,
                SmbFileAttributes.Normal,
                ShareAccess.Read,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE,
                null);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new IOException($"打开远端文件失败：{Describe(status)}");
            }

            try
            {
                var bufferSize = (int)Math.Min(store.MaxReadSize, MaxChunkSize);
                long offset = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    status = store.ReadFile(out var data, handle, offset, bufferSize);
                    if (status == NTStatus.STATUS_END_OF_FILE)
                    {
                        break;
                    }

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        throw new IOException($"读取远端文件失败：{Describe(status)}");
                    }

                    if (data.Length == 0)
                    {
                        break;
                    }

                    destination.Write(data, 0, data.Length);
                    offset += data.Length;
                }
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(1);
    }

    public async Task UploadAsync(
        string remotePath,
        Stream content,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await Task.Run(() =>
        {
            var store = _store!;
            var path = ToSmbPath(remotePath);

            EnsureDirectory(store, path);

            var status = store.CreateFile(
                out var handle,
                out _,
                path,
                AccessMask.GENERIC_WRITE,
                SmbFileAttributes.Normal,
                ShareAccess.None,
                CreateDisposition.FILE_OVERWRITE_IF,
                CreateOptions.FILE_NON_DIRECTORY_FILE,
                null);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new IOException($"创建远端文件失败：{Describe(status)}");
            }

            try
            {
                var buffer = new byte[(int)Math.Min(store.MaxWriteSize, MaxChunkSize)];
                long offset = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var read = content.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    var chunk = read == buffer.Length ? buffer : buffer[..read];
                    status = store.WriteFile(out _, handle, offset, chunk);
                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        throw new IOException($"写入远端文件失败：{Describe(status)}");
                    }

                    offset += read;
                }
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(1);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _store?.Disconnect();
        }
        catch (Exception)
        {
            // 断开失败忽略。
        }

        try
        {
            _client?.Logoff();
        }
        catch (Exception)
        {
            // 注销失败忽略。
        }

        _client?.Disconnect();
        _store = null;
        _client = null;
        return ValueTask.CompletedTask;
    }

    private void ListRecursive(
        string directory,
        List<RemoteEntry> entries,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = _store!;
        var status = store.CreateFile(
            out var handle,
            out _,
            directory,
            AccessMask.GENERIC_READ,
            SmbFileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            // 无权限或已删除的子目录直接跳过。
            return;
        }

        List<QueryDirectoryFileInformation> items;
        try
        {
            status = store.QueryDirectory(
                out items,
                handle,
                "*",
                FileInformationClass.FileDirectoryInformation);
        }
        finally
        {
            store.CloseFile(handle);
        }

        if (status != NTStatus.STATUS_SUCCESS)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item is not FileDirectoryInformation info)
            {
                continue;
            }

            if (info.FileName is "." or ".." || string.IsNullOrEmpty(info.FileName))
            {
                continue;
            }

            var isDirectory = (info.FileAttributes & SmbFileAttributes.Directory) != 0;
            var child = CombineSmbPath(directory, info.FileName);

            entries.Add(new RemoteEntry(ToDisplayPath(child), info.EndOfFile, isDirectory));

            if (isDirectory)
            {
                ListRecursive(child, entries, progress, cancellationToken);
            }
        }

        progress?.Report(new ScanProgress("正在列出 SMB 目录", entries.Count, entries.Count));
    }

    /// <summary>逐级创建远端目录；已存在视为成功。</summary>
    private static void EnsureDirectory(ISMBFileStore store, string filePath)
    {
        var separator = filePath.LastIndexOf('\\');
        if (separator <= 0)
        {
            return;
        }

        var directory = filePath[..separator];
        var segments = directory.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;

        foreach (var segment in segments)
        {
            current = current.Length == 0 ? segment : current + "\\" + segment;

            var status = store.CreateFile(
                out var handle,
                out _,
                current,
                AccessMask.GENERIC_WRITE,
                SmbFileAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_CREATE,
                CreateOptions.FILE_DIRECTORY_FILE,
                null);

            if (status == NTStatus.STATUS_SUCCESS)
            {
                store.CloseFile(handle);
                continue;
            }

            // 目录已存在时不再报错。
            if (status != NTStatus.STATUS_OBJECT_NAME_COLLISION)
            {
                throw new IOException($"创建远端目录失败：{Describe(status)}");
            }
        }
    }

    /// <summary>接口层路径统一 '/' 分隔；SMB 用 '\' 且相对共享根。</summary>
    private static string ToSmbPath(string path) => path.Replace('/', '\\').TrimStart('\\');

    private static string ToDisplayPath(string path) => path.Replace('\\', '/');

    private static string CombineSmbPath(string directory, string name)
        => string.IsNullOrEmpty(directory) ? name : directory + "\\" + name;

    private static string Describe(NTStatus status) => status.ToString();
}
