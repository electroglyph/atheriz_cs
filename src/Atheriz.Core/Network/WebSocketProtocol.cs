using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Network;

// Port of atheriz/network/websocket.py:1-199
// WebSocket-specific implementation of BaseConnection.
// Line numbers referenced in comments.

public sealed class WebSocketConnection : BaseConnection
{
    // port of websocket.py:33-45 WebSocketConnection.__init__
    public System.Net.WebSockets.WebSocket WebSocket { get; }
    private Task? _closeTask;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1); // port of websocket.py:44 _send_lock = asyncio.Lock()
    private readonly PendingLimiter _limiter; // sole accounting (P1.6 single source of truth)
    // Legacy reflection shims retained for compat (not authoritative, no drift)
#pragma warning disable CS0169
    private readonly object _pendingLock = new object();
    private readonly HashSet<Task> _pendingTasks = new();
    private int _pendingCount;
    private int _pendingBytes;
    private readonly Dictionary<Task, int> _pendingBytesByTask = new();
    private bool _closing;
#pragma warning restore CS0169

    private readonly AtherizSettings _settings;

    public WebSocketConnection(System.Net.WebSockets.WebSocket websocket, string? sessionId = null, AtherizSettings? settings = null, string? clientHost = null) : base(sessionId)
    {
        WebSocket = websocket;
        _settings = settings ?? AtherizSettings.Global;
        ClientHost = clientHost ?? "?"; // port of websocket.py:36
        _limiter = new PendingLimiter(_settings.WebsocketMaxPendingBytes, _settings.WebsocketMaxPendingSends);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { WebSocket.Abort(); } catch { }
            try { WebSocket.Dispose(); } catch { }
            try { _sendLock.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }

    // port of websocket.py:46-53 _track_task — now via PendingLimiter sole accounting
    // (SendCommand reserves/tracks inline below; this helper was dead code.)
    // port of websocket.py:55-66 _task_done — now via PendingLimiter sole accounting
    // (see TaskDone below).
    private void TaskDone(Task task)
    {
        _limiter.Release(task);
        // Avoid GetAwaiter().GetResult() blocking; inspect fault directly
        if (task.IsFaulted)
        {
            var ex = task.Exception?.InnerException ?? task.Exception;
            if (ex is OperationCanceledException) { }
            else if (ex != null) try { Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Async task failed: {ex}"); } catch { Console.Error.WriteLine($"[WebSocket] Async task failed: {ex}"); }
        }
        else if (task.IsCanceled) { }
    }

    // port of websocket.py:68-70 _locked_send — bounded: a hung peer must not
    // pin _sendLock (and stall all later sends) forever.
    private async Task LockedSendAsync(string data)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sendLock.WaitAsync(cts.Token);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { WebSocket.Abort(); } catch { }
            throw;
        }
        finally { try { _sendLock.Release(); } catch { } }
    }

    public bool IsClosing => _limiter.IsClosing || _closing;
    public int PendingBytes => _limiter.PendingBytes;
    public int PendingCount => _limiter.PendingCount;

    // port of websocket.py:72-114 send_command — now via PendingLimiter sole accounting
    public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
    {
        if (cmd == "echo_on") return; // port of websocket.py:73-74
        if (cmd == "prompt_masked") cmd = "prompt"; // port of websocket.py:75-76
        args ??= new List<object?>();
        kwargs ??= new Dictionary<string, object?>();
        var data = JsonSerializer.Serialize(new object[] { cmd, args, kwargs }); // port of websocket.py:81
        var nb = Encoding.UTF8.GetByteCount(data); // port of websocket.py:82
        if (IsClosing) return;
        // TryReserve handles both bytes and count limits via PendingLimiter
        if (!_limiter.TryReserve(nb))
        {
            try { Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] closing {ClientHost}: pending {_limiter.PendingCount} msgs {_limiter.PendingBytes} bytes exceeds limit"); } catch (Exception) { }
            Close();
            return;
        }
        Task? task = null;
        try
        {
            // port of websocket.py:93-97
            task = Task.Run(() => LockedSendAsync(data));
        }
        catch (Exception e) // port of websocket.py:99-103
        {
            _limiter.ReleaseSync(nb);
            Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Error sending command: {e}");
            return;
        }
        // Track task for later Release via limiter
        _limiter.Track(task, nb);
        try { _ = task.ContinueWith(t => TaskDone(t)); } catch { } // port of websocket.py:106-109
    }

    // port of websocket.py:116-137 _close_websocket — now via limiter snapshot
    private async Task CloseWebSocketAsync()
    {
        List<Task> pending = _limiter.SnapshotTasks();
        if (pending.Count > 0)
        {
            try
            {
                // port of websocket.py:120-130 wait_for gather with 0.25 timeout.
                // WaitAsync(CancellationToken) raises OperationCanceledException
                // (not TimeoutException) on deadline — that is the expected path.
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await Task.WhenAll(pending).WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) { } // deadline elapsed; pendings release via TaskDone on completion
            catch { }
        }
        try
        {
            // Bounded close handshake: a peer that never answers
            // must not hang Close forever. Abort past the deadline.
            if (WebSocket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token); }
                catch (OperationCanceledException) { try { WebSocket.Abort(); } catch { } }
            }
        }
        catch { } // port of websocket.py:135
    }

    // port of websocket.py:139-150 close — now via limiter sole accounting
    public override void Close()
    {
        if (!_limiter.TryMarkClosing())
        {
            _closing = true;
            return;
        }
        _closing = true;
        try
        {
            // port of websocket.py:145-148 _is_on_loop_thread branching — scheduled via Task.Run.
            // Observe the task: an unobserved close fault must reach the log, not the finalizer.
            _closeTask = Task.Run(() => CloseWebSocketAsync());
            _ = _closeTask.ContinueWith(t => TaskDone(t), TaskScheduler.Default);
        }
        catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Error closing connection: {e}"); } // port of websocket.py:149-150
    }

    internal PendingLimiter Limiter => _limiter;
}

// Typed contracts replacing the Python @app.websocket decorator duck-typing
// (port of websocket.py:153-199 setup). Test doubles implement these;
// production never routes real sockets through WebSocketProtocol.Setup
// (Server's Program.cs maps /ws explicitly).
/// <summary>Peer info for host resolution (port of websocket.client.host).</summary>
public interface IWebSocketClientInfo { string? Host { get; } }
/// <summary>Abstraction over a connected websocket peer (mock or real).</summary>
public interface IWebSocketPeer
{
    object? Client { get; }
    Task AcceptAsync();
    Task<string> ReceiveTextAsync();
    Task CloseAsync(int code, string? reason);
}
/// <summary>App surface Setup registers the /ws endpoint against.</summary>
public interface IWebSocketApp
{
    void WebSocket(string path, Func<IWebSocketPeer, Task> endpoint);
}
/// <summary>Marker for clean peer disconnects (port of WebSocketDisconnect).</summary>
public interface IWebSocketDisconnect { }

public sealed class WebSocketProtocol : Protocol
{
    // Oversize throttling — port of websocket.py:15-27 (now via ThrottleWindow)
    private static readonly object _oversizeLock = new object();
    private static readonly Dictionary<string, double> _oversizeLast = new();
    private const double OversizeWindow = 5.0; // port of websocket.py:17

    private static bool ShouldLogOversize(string host) // port of websocket.py:20-27
        => ThrottleWindow.ShouldLog(_oversizeLast, _oversizeLock, host, OversizeWindow);

    // Port of websocket.py:153-199 WebSocketProtocol.setup.
    // Test doubles implement IWebSocketApp/IWebSocketPeer ( FakeApp /
    // MockWsEndpoint in PortedWebSocketTests); the typed
    // System.Net.WebSockets.WebSocket path lives in WebSocketConnection and
    // the real server registers /ws explicitly in Server's Program.cs.
    public override void Setup(object app)
    {
        // Port of websocket.py:153-199 WebSocketProtocol.setup.
        AtherizSettings settings = AtherizSettings.Global;
        if (app is IHost host)
        {
            try { settings = host.Services.GetRequiredService<AtherizSettings>(); } catch { }
        }

        if (!settings.WebsocketEnabled) return; // port of websocket.py:160-161
        // Real WebApplication is handled directly in Server's Program.cs
        // (explicit Map), not here: only IWebSocketApp doubles register.
        if (app is not IWebSocketApp wapp) return;

        {
            {
                // Create the endpoint delegate that mirrors websocket.py:163-199
                Func<IWebSocketPeer, System.Threading.Tasks.Task> endpoint = async (IWebSocketPeer peer) =>
                {
                    string clientHost = (peer.Client as IWebSocketClientInfo)?.Host ?? "?";
                    // is_ip_banned check (port of websocket.py:166)
                    try {
                        if (Atheriz.Core.Globals.ObjectRegistry.IsIpBanned(clientHost)) {
                            try { await peer.CloseAsync(0, null); } catch { }
                            return;
                        }
                    } catch { }
                    try { await peer.AcceptAsync(); } catch { }
                    var mgr = ConnectionManager.GlobalInstance ?? new ConnectionManager(settings: settings);
                    string connId = mgr.GenerateConnectionId();
                    // Peer doubles have no real socket: the fallback connection drops
                    // sends loudly (the old dynamic branch resolved the same way).
                    BaseConnection connection = new FallbackConnection(connId) { ClientHost = clientHost };
                    if (!mgr.RegisterConnection(connId, connection!)) return;
                    try
                    {
                        while (true)
                        {
                            string raw = await peer.ReceiveTextAsync();
                            int byteCount = System.Text.Encoding.UTF8.GetByteCount(raw);
                            if (byteCount > settings.WebsocketMaxMessageSize)
                            {
                                // oversize handling port of websocket.py:181-192 — throttled via ThrottleWindow (5s per host)
                                bool shouldLog = true;
                                try { shouldLog = ShouldLogOversize(clientHost); } catch { }
                                if (shouldLog) Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] Message too large from {clientHost} ({byteCount} bytes > {settings.WebsocketMaxMessageSize} bytes)");
                                try { await peer.CloseAsync(1009, "Message too large"); } catch { }
                                break;
                            }
                            mgr.HandleCommand(connection!, raw);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Port of websocket.py except WebSocketDisconnect: clean peer
                        // disconnects stay silent, everything else is logged.
                        if (ex is not IWebSocketDisconnect)
                        {
                            Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] Connection error: {ex}");
                        }
                    }
                    finally
                    {
                        try { mgr.Disconnect(connection!); } catch { }
                    }
                };
                // Register the endpoint against the typed app contract.
                wapp.WebSocket("/ws", endpoint);
                return;
            }
        }
    }

    private sealed class FallbackConnection : BaseConnection
    {
        public FallbackConnection(string? sid) : base(sid) { }
        // No real peer exists: dropping silently loses messages, so log loudly.
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
        {
            Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] dropping command '{cmd}': no real peer (fallback connection)");
        }
        public override void Close()
        {
            Atheriz.Core.AtherizLogger.LogWarning("[WebSocket] close on fallback connection (no real peer)");
        }
    }
}
