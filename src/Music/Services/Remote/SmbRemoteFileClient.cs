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
            // 共享名写错时，把服务器上真实存在的共享列出来，省得用户猜。
            var available = TryListShares(client);

            client.Logoff();
            client.Disconnect();

            var hint = available.Count > 0
                ? $"该服务器上的共享有：{string.Join("、", available)}。"
                : "请在服务器的共享设置里确认共享名（共享名不等于文件夹名）。";

            throw new IOException($"无法打开共享「{_config.ShareName}」：{Describe(status)}。{hint}");
        }

        // 共享能打开不代表起始目录存在，先在连接阶段校验一次，
        // 避免「测试连接通过、同步却 0 首」的困惑。
        var rootPath = ToSmbPath(_config.RootPath ?? "/");
        var rootStatus = OpenDirectory(store, rootPath);
        if (rootStatus != NTStatus.STATUS_SUCCESS)
        {
            store.Disconnect();
            client.Logoff();
            client.Disconnect();
            throw new IOException(
                $"已连接到共享「{_config.ShareName}」，但起始目录「{_config.RootPath}」打不开：{Describe(rootStatus)}。");
        }

        _client = client;
        _store = store;
    }, cancellationToken);

    /// <summary>打开一个目录做探活，成功则立即关闭。用于校验起始目录是否可访问。</summary>
    private static NTStatus OpenDirectory(ISMBFileStore store, string path)
    {
        var status = store.CreateFile(
            out var handle,
            out _,
            path,
            AccessMask.GENERIC_READ,
            SmbFileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status == NTStatus.STATUS_SUCCESS)
        {
            store.CloseFile(handle);
        }

        return status;
    }

    /// <summary>查询已打开句柄的文件大小；失败返回 0，调用方据此退化成不报中间进度。</summary>
    private static long GetFileSize(ISMBFileStore store, object handle)
    {
        var status = store.GetFileInformation(
            out var information,
            handle,
            FileInformationClass.FileStandardInformation);

        return status == NTStatus.STATUS_SUCCESS && information is FileStandardInformation standard
            ? standard.EndOfFile
            : 0;
    }

    /// <summary>尽力枚举服务器共享名；失败就返回空列表，不影响主流程的报错。</summary>
    private static List<string> TryListShares(SMB2Client client)
    {
        try
        {
            var shares = client.ListShares(out var status);
            return status == NTStatus.STATUS_SUCCESS && shares is not null
                ? shares
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        var entries = new List<RemoteEntry>();
        await Task.Run(
                () => ListRecursive(ToSmbPath(rootPath), entries, progress, cancellationToken, isRoot: true),
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

            // 先取文件总大小，循环里才能回报 0~1 进度；取不到就退化成不报中间进度。
            var totalSize = GetFileSize(store, handle);

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

                    if (totalSize > 0)
                    {
                        progress?.Report((double)offset / totalSize);
                    }
                }
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(1);
    }

    public async Task<Stream> OpenReadAsync(
        string remotePath,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        // SMBLibrary 只有同步分块读取，包一层顺序 Stream 供代理边读边发。
        return await Task.Run(
                () => (Stream)new SmbReadStream(_store!, ToSmbPath(remotePath), offset),
                cancellationToken)
            .ConfigureAwait(false);
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

            // 上传源通常是本地文件流，长度已知；不可 seek 时不报中间进度。
            var totalSize = content.CanSeek ? content.Length : 0;

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

                    if (totalSize > 0)
                    {
                        progress?.Report((double)offset / totalSize);
                    }
                }
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(1);
    }

    public async Task DeleteAsync(string remotePath, CancellationToken cancellationToken)
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
                AccessMask.GENERIC_WRITE | AccessMask.DELETE,
                SmbFileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE,
                null);

            // 文件不存在时视为已删除。
            if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND)
            {
                return;
            }

            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new IOException($"打开远端文件失败：{Describe(status)}");
            }

            try
            {
                // SMB 没有独立的删除命令：标记为「关闭时删除」，关闭句柄即完成删除。
                status = store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"删除远端文件失败：{Describe(status)}");
                }
            }
            finally
            {
                store.CloseFile(handle);
            }
        }, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        bool isRoot = false)
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
            // 起始目录打不开必须报出来，否则用户只会看到「已导入 0 首曲目」而无从排查；
            // 子目录可能因权限或竞态消失，跳过即可。
            if (isRoot)
            {
                throw new IOException($"无法打开 SMB 目录「{directory}」：{Describe(status)}");
            }

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

        // SMBLibrary 列完目录时可能返回 STATUS_NO_MORE_FILES（而非 STATUS_SUCCESS），
        // 它同样表示列表已取全，不能当作失败丢弃结果。
        if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
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

    /// <summary>
    /// 把 SMBLibrary 的分块读取包装成只读顺序流：内部预取一块，按需交付给调用方。
    /// 打开后即固定 offset，不支持 Seek，供本地 HTTP 代理按 Range 顺序发送。
    /// </summary>
    private sealed class SmbReadStream : Stream
    {
        private readonly ISMBFileStore _store;
        private readonly object _handle;
        private readonly int _chunkSize;

        private byte[] _chunk = [];
        private int _chunkOffset;
        private int _chunkCount;
        private long _position;
        private bool _disposed;

        public SmbReadStream(ISMBFileStore store, string path, long offset)
        {
            _store = store;
            _chunkSize = (int)Math.Min(store.MaxReadSize, MaxChunkSize);

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

            _handle = handle;
            _position = offset;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (count <= 0)
            {
                return 0;
            }

            if (_chunkCount == 0 && !Fill())
            {
                return 0;
            }

            var take = Math.Min(count, _chunkCount);
            Buffer.BlockCopy(_chunk, _chunkOffset, buffer, offset, take);

            _chunkOffset += take;
            _chunkCount -= take;
            _position += take;
            return take;
        }

        /// <summary>拉取下一块；已在文件末尾时返回 false。</summary>
        private bool Fill()
        {
            var status = _store.ReadFile(out var data, _handle, _position, _chunkSize);
            if (status == NTStatus.STATUS_END_OF_FILE || data.Length == 0)
            {
                return false;
            }

            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new IOException($"读取远端文件失败：{Describe(status)}");
            }

            _chunk = data;
            _chunkOffset = 0;
            _chunkCount = data.Length;
            return true;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try
                {
                    _store.CloseFile(_handle);
                }
                catch (Exception)
                {
                    // 连接可能已被回收，关闭句柄失败可忽略。
                }
            }

            base.Dispose(disposing);
        }
    }
}
