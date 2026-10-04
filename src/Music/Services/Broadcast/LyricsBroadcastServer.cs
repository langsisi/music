using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Music.Services.Broadcast;

/// <summary>对外广播的播放状态快照。</summary>
public sealed record BroadcastState(
    string Title,
    string Artist,
    string Album,
    double PositionSeconds,
    double DurationSeconds,
    bool IsPlaying,
    IReadOnlyList<string> Lines,
    int CurrentLineIndex)
{
    public static readonly BroadcastState Idle = new("未在播放", string.Empty, string.Empty, 0, 0, false, [], -1);
}

/// <summary>SSE <c>track</c> 事件的负载（字段名由 camelCase 策略输出为小写）。</summary>
internal sealed record TrackEventPayload(
    string Title,
    string Artist,
    string Album,
    double Duration,
    IReadOnlyList<string> Lines);

/// <summary>SSE <c>line</c> 事件的负载（字段名由 camelCase 策略输出为小写）。</summary>
internal sealed record LineEventPayload(int Index, double Position, bool Playing);

/// <summary>
/// 内置歌词广播服务：直接绑定 Socket 的极简 HTTP 服务，
/// <c>GET /</c> 返回滚动歌词网页，<c>GET /events</c> 用 SSE 推送当前行。
/// </summary>
/// <remarks>
/// 之所以不用 HttpListener：它绑定 <c>http://+:port/</c> 需要管理员或 URL ACL；
/// 也不需要 WebSocket —— SSE 由浏览器自动重连，实现量小得多。
/// </remarks>
public sealed class LyricsBroadcastServer : IDisposable
{
    private readonly ConcurrentDictionary<TcpClient, ClientSink> _clients = new();
    private readonly object _startStopLock = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private BroadcastState _state = BroadcastState.Idle;
    private string _lastTrackSignature = string.Empty;

    /// <summary>服务状态变化（供设置页显示）。</summary>
    public event EventHandler<string>? StatusChanged;

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    /// <summary>本机可访问的地址列表。</summary>
    public IReadOnlyList<string> Urls
    {
        get
        {
            if (!IsRunning)
            {
                return [];
            }

            var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => $"http://{address}:{Port}/")
                .ToList();

            addresses.Insert(0, $"http://127.0.0.1:{Port}/");
            return addresses;
        }
    }

    public void Start(int port)
    {
        lock (_startStopLock)
        {
            if (IsRunning)
            {
                if (Port == port)
                {
                    return;
                }

                StopCore();
            }

            try
            {
                Port = port;
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                IsRunning = true;

                _ = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
                StatusChanged?.Invoke(this, $"歌词广播已开启：{Urls.FirstOrDefault()}");
            }
            catch (Exception ex)
            {
                IsRunning = false;
                _listener = null;
                StatusChanged?.Invoke(this, $"歌词广播启动失败：{ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_startStopLock)
        {
            if (!IsRunning)
            {
                return;
            }

            StopCore();
            StatusChanged?.Invoke(this, "歌词广播已关闭");
        }
    }

    /// <summary>推送新的播放状态；内容有变化时才向已连接客户端写数据。</summary>
    public void Update(BroadcastState state)
    {
        _state = state;

        if (!IsRunning || _clients.IsEmpty)
        {
            return;
        }

        var signature = $"{state.Title}|{state.Lines.Count}";
        var trackChanged = signature != _lastTrackSignature;

        if (trackChanged)
        {
            _lastTrackSignature = signature;
        }

        var payload = trackChanged ? BuildTrackEvent(state) : BuildLineEvent(state);

        foreach (var (client, _) in _clients)
        {
            _ = SendToAsync(client, payload);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private void StopCore()
    {
        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
            // 忽略关闭异常。
        }

        _listener = null;

        foreach (var (client, sink) in _clients)
        {
            sink.Dispose();
            client.Dispose();
        }

        _clients.Clear();
        _lastTrackSignature = string.Empty;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // listener 被关闭或已取消，退出循环。
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var stream = client.GetStream();
            var path = await ReadRequestPathAsync(stream, cancellationToken).ConfigureAwait(false);

            if (path.StartsWith("/events", StringComparison.OrdinalIgnoreCase))
            {
                await ServeEventStreamAsync(client, stream, cancellationToken).ConfigureAwait(false);
                return;
            }

            var html = Encoding.UTF8.GetBytes(LyricsPageHtml);
            var header = Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {html.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n");

            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(html, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 客户端提前断开等情况直接忽略。
        }
        finally
        {
            _clients.TryRemove(client, out var sink);
            sink?.Dispose();
            client.Dispose();
        }
    }

    private static async Task<string> ReadRequestPathAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        var headerEnd = false;

        while (!headerEnd && builder.Length < 16384)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            headerEnd = builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal);
        }

        var firstLine = builder.ToString().Split("\r\n")[0];
        var parts = firstLine.Split(' ');
        return parts.Length >= 2 ? parts[1] : "/";
    }

    private async Task ServeEventStreamAsync(
        TcpClient client,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = Encoding.UTF8.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/event-stream; charset=utf-8\r\n" +
            "Cache-Control: no-cache\r\n" +
            "Connection: keep-alive\r\n" +
            "Access-Control-Allow-Origin: *\r\n\r\n");

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var sink = new ClientSink(stream);
        _clients[client] = sink;

        // 新客户端立即拿到完整状态。
        _lastTrackSignature = string.Empty;
        await SendToAsync(client, BuildTrackEvent(_state)).ConfigureAwait(false);
        await SendToAsync(client, BuildLineEvent(_state)).ConfigureAwait(false);
    }

    /// <summary>向单个客户端发送；失败即判定为断开并清理。</summary>
    private async Task SendToAsync(TcpClient client, string payload)
    {
        if (!_clients.TryGetValue(client, out var sink))
        {
            return;
        }

        var ok = await sink.SendAsync(payload).ConfigureAwait(false);
        if (ok)
        {
            return;
        }

        _clients.TryRemove(client, out _);
        sink.Dispose();
        client.Dispose();
    }

    private static string BuildTrackEvent(BroadcastState state)
    {
        var payload = JsonSerializer.Serialize(
            new TrackEventPayload(state.Title, state.Artist, state.Album, state.DurationSeconds, state.Lines),
            BroadcastJsonContext.Default.TrackEventPayload);

        return $"event: track\ndata: {payload}\n\n";
    }

    private static string BuildLineEvent(BroadcastState state)
    {
        var payload = JsonSerializer.Serialize(
            new LineEventPayload(state.CurrentLineIndex, Math.Round(state.PositionSeconds, 2), state.IsPlaying),
            BroadcastJsonContext.Default.LineEventPayload);

        return $"event: line\ndata: {payload}\n\n";
    }

    private sealed class ClientSink : IDisposable
    {
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public ClientSink(NetworkStream stream) => _stream = stream;

        public async Task<bool> SendAsync(string message)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await _stream.WriteAsync(bytes).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                // 写失败说明客户端已断开，由调用方清理。
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            _writeLock.Dispose();
            _stream.Dispose();
        }
    }

    private const string LyricsPageHtml = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1" />
        <title>Music · 歌词</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
          body { margin:0; background:#0b0d12; color:#f3f5fa;
                 font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Microsoft YaHei",sans-serif;
                 height:100vh; display:flex; flex-direction:column; overflow:hidden; }
          header { padding:18px 20px 12px; border-bottom:1px solid #232a3d; }
          #title { font-size:17px; font-weight:600; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
          #artist { font-size:12px; color:#a6aec4; margin-top:4px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
          #lyrics { flex:1; overflow-y:auto; padding:24vh 20px; scroll-behavior:smooth; }
          .line { padding:10px 0; font-size:19px; line-height:1.5; color:#6b748e;
                  transition:color .25s, transform .25s; transform-origin:left center; }
          .line.active { color:#f3f5fa; font-weight:600; transform:scale(1.04); }
          .line.near { color:#a6aec4; }
          #status { padding:10px 20px; font-size:11px; color:#6b748e; border-top:1px solid #232a3d; }
          #progress { height:3px; background:#232a3d; }
          #progress > div { height:100%; width:0; background:linear-gradient(90deg,#7c6bff,#e15bff); }
        </style>
        </head>
        <body>
        <header>
          <div id="title">未在播放</div>
          <div id="artist">&nbsp;</div>
        </header>
        <div id="lyrics"><div class="line">等待连接…</div></div>
        <div id="progress"><div></div></div>
        <div id="status">已连接</div>
        <script>
          const titleEl = document.getElementById('title');
          const artistEl = document.getElementById('artist');
          const lyricsEl = document.getElementById('lyrics');
          const barEl = document.querySelector('#progress > div');
          const statusEl = document.getElementById('status');
          let lines = [], duration = 0, activeIndex = -1;

          function renderLines() {
            lyricsEl.innerHTML = '';
            if (!lines.length) {
              const d = document.createElement('div');
              d.className = 'line'; d.textContent = '暂无歌词';
              lyricsEl.appendChild(d);
              return;
            }
            lines.forEach((text, i) => {
              const d = document.createElement('div');
              d.className = 'line';
              d.dataset.i = i;
              d.textContent = text || '♪';
              lyricsEl.appendChild(d);
            });
          }

          function highlight(index) {
            if (index === activeIndex) return;
            activeIndex = index;
            [...lyricsEl.children].forEach((el, i) => {
              el.classList.toggle('active', i === index);
              el.classList.toggle('near', Math.abs(i - index) === 1);
            });
            const el = lyricsEl.querySelector('.line.active');
            if (el) {
              const target = el.offsetTop - lyricsEl.clientHeight / 2 + el.clientHeight / 2;
              lyricsEl.scrollTo({ top: Math.max(0, target), behavior: 'smooth' });
            }
          }

          const source = new EventSource('/events');

          source.addEventListener('track', e => {
            const d = JSON.parse(e.data);
            titleEl.textContent = d.title || '未在播放';
            artistEl.textContent = d.artist || ' ';
            lines = d.lines || [];
            duration = d.duration || 0;
            activeIndex = -1;
            renderLines();
            statusEl.textContent = '曲目已同步';
          });

          source.addEventListener('line', e => {
            const d = JSON.parse(e.data);
            highlight(d.index);
            if (duration > 0) barEl.style.width = Math.min(100, d.position / duration * 100) + '%';
          });

          source.onopen = () => statusEl.textContent = '已连接';
          source.onerror = () => statusEl.textContent = '连接断开，正在重连…';
        </script>
        </body>
        </html>
        """;
}
