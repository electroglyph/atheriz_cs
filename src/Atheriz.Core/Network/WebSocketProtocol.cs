using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atheriz.Core.Network;

// Port of atheriz/network/websocket.py:1-199
// WebSocket-specific implementation of BaseConnection.
// Line numbers referenced in comments.

public sealed class WebSocketConnection : BaseConnection
{
    // port of websocket.py:33-45 WebSocketConnection.__init__
    public System.Net.WebSockets.WebSocket WebSocket { get; }
    private Task? _closeTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1); // port of websocket.py:44 _send_lock = asyncio.Lock()
    private readonly PendingLimiter _limiter; // sole accounting (single source of truth)
    // Close flag (with _limiter.IsClosing forms IsClosing).
    private bool _closing;

    private readonly AtherizSettings _settings;

    // Named send/close deadlines (values identical to the old literals).
    private static readonly TimeSpan SendLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseDrainTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);
    // Hoisted serializer options (default settings, matching the previous
    // per-send implicit defaults) so the hot path allocates no options.
    private static readonly JsonSerializerOptions SendJsonOptions = JsonSerializerOptions.Default;

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
            // join in-flight sends (bounded by the longest write/TLS
            // timeouts) before Abort/Dispose instead of a 250ms spin that
            // proceeds mid-write and hides the loss in ObjectDisposedException.
            try
            {
                var pending = _limiter.SnapshotTasks().ToArray();
                if (pending.Length != 0) Task.WhenAll(pending).Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Dispose: " + logEx.Message, "WebSocketConnection"); }
            try { WebSocket.Abort(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.Dispose: " + logEx.Message, "WebSocketConnection"); }
            try { WebSocket.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.Dispose: " + logEx.Message, "WebSocketConnection"); }
            try { _sendLock.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.Dispose: " + logEx.Message, "WebSocketConnection"); }
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
            // OperationCanceledException and ObjectDisposedException stay silent
            // (post-dispose race: socket already gone); anything else is logged.
            if (ex is not (OperationCanceledException or ObjectDisposedException) and not null) try { Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Async task failed: {ex}"); } catch { Console.Error.WriteLine($"[WebSocket] Async task failed: {ex}"); }
        }
    }

    // port of websocket.py:68-70 _locked_send — bounded: a hung peer must not
    // pin _sendLock (and stall all later sends) forever. Lock-wait and send
    // use separate deadlines: a lock timeout only skips this message (the
    // holder still owns a live send), while a send timeout Aborts.
    private async Task LockedSendAsync(byte[] bytes)
    {
        // Lock-wait uses WaitAsync(TimeSpan): no CTS alloc for this phase.
        // A lock timeout only skips this message (the holder still owns a
        // live send), while a send timeout Aborts — the two failure modes
        // stay distinct. The OCE throw keeps the old lock-timeout silence
        // (TaskDone swallows OperationCanceledException).
        if (!await _sendLock.WaitAsync(SendLockTimeout).ConfigureAwait(false))
            throw new OperationCanceledException("WebSocket send lock wait timed out.");
        try
        {
            using var sendCts = new CancellationTokenSource(SendTimeout);
            await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, sendCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { WebSocket.Abort(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.LockedSendAsync: " + logEx.Message, "WebSocketConnection"); }
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
        args ??= [];
        kwargs ??= [];
        // Single UTF8 pass: serialize straight to bytes and reuse the length
        // for the reservation (was GetByteCount + GetBytes). Wire bytes are
        // identical — same payload shape, same options.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new object[] { cmd, args, kwargs }, SendJsonOptions); // port of websocket.py:81
        var nb = bytes.Length; // port of websocket.py:82
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
            // port of websocket.py:93-97 — reserve -> schedule -> Track in one
            // guarded span: if Track itself throws, the reservation is released
            // and the task observed (the old split leaked the limiter slot).
            task = Task.Run(() => LockedSendAsync(bytes));
            _limiter.Track(task, nb);
            // the completion callback rides the owning try — if the
            // attach itself throws, the catch below releases the reservation
            // instead of leaking the limiter slot (port of websocket.py:106-109).
            _ = task.ContinueWith(t => TaskDone(t), TaskScheduler.Default);
        }
        catch (Exception e) // port of websocket.py:99-103
        {
            _limiter.ReleaseSync(nb);
            if (task is not null) try { task.ContinueWith(t => TaskDone(t), TaskScheduler.Default); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.SendCommand: " + logEx.Message, "WebSocketConnection"); }
            Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Error sending command: {e}");
            return;
        }
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
                using var cts = new CancellationTokenSource(CloseDrainTimeout);
                await Task.WhenAll(pending).WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { } // deadline elapsed; pendings release via TaskDone on completion
            catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.CloseWebSocketAsync: " + logEx.Message, "WebSocket"); }
        }
        try
        {
            // Bounded close handshake: a peer that never answers
            // must not hang Close forever. Abort past the deadline.
            if (WebSocket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(CloseHandshakeTimeout);
                try { await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { try { WebSocket.Abort(); } catch { } }
            }
        }
        catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.CloseWebSocketAsync: " + logEx.Message, "WebSocket"); } // port of websocket.py:135
    }

    // port of websocket.py:139-150 close — now via limiter sole accounting
    public override void Close()
    {
        _closing = true;
        if (!_limiter.TryMarkClosing()) return;
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

public sealed class WebSocketProtocol : BaseProtocol
{
    // Oversize throttling — port of websocket.py:15-27 (now via ThrottleWindow)
    private static readonly ThrottledLog _oversizeLog = new(OversizeWindow);
    private const double OversizeWindow = 5.0; // port of websocket.py:17

    private static bool ShouldLogOversize(string host) // port of websocket.py:20-27
        => _oversizeLog.ShouldLog(host);

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
            try { settings = host.Services.GetRequiredService<AtherizSettings>(); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
        }

        if (!settings.WebsocketEnabled) return; // port of websocket.py:160-161
        // Real WebApplication is handled directly in Server's Program.cs
        // (explicit Map), not here: only IWebSocketApp doubles register.
        if (app is not IWebSocketApp wapp) return;

        // Create the endpoint delegate that mirrors websocket.py:163-199
        Func<IWebSocketPeer, System.Threading.Tasks.Task> endpoint = async (IWebSocketPeer peer) =>
        {
            string clientHost = (peer.Client as IWebSocketClientInfo)?.Host ?? "?";
            // is_ip_banned check (port of websocket.py:166)
            try {
                if (Atheriz.Core.Globals.ObjectRegistry.IsIpBanned(clientHost)) {
                    try { await peer.CloseAsync(0, null).ConfigureAwait(false); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
                    return;
                }
            } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
            try { await peer.AcceptAsync().ConfigureAwait(false); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
            var mgr = ConnectionManager.GlobalInstance ?? new ConnectionManager(settings: settings);
            string connId = mgr.GenerateConnectionId();
            // Peer doubles have no real socket: the fallback connection drops
            // sends loudly (the old dynamic branch resolved the same way).
            BaseConnection connection = new FallbackConnection(connId) { ClientHost = clientHost };
                    if (!mgr.RegisterConnection(connId, connection!))
                    {
                        // Refused (ban/total-cap): close the peer instead of
                        // returning to a Close() that only logs — otherwise the
                        // socket dangles to client timeout, unregistered and
                        // unswept. Nothing was registered, so nothing to sweep.
                        try { await peer.CloseAsync(1013, "Server unavailable").ConfigureAwait(false); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
                        return;
                    }
            try
            {
                while (true)
                {
                    string raw = await peer.ReceiveTextAsync().ConfigureAwait(false);
                    int byteCount = System.Text.Encoding.UTF8.GetByteCount(raw);
                    if (byteCount > settings.WebsocketMaxMessageSize)
                    {
                        // oversize handling port of websocket.py:181-192 — throttled via ThrottleWindow (5s per host)
                        bool shouldLog = true;
                        try { shouldLog = ShouldLogOversize(clientHost); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
                        if (shouldLog) Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] Message too large from {clientHost} ({byteCount} bytes > {settings.WebsocketMaxMessageSize} bytes)");
                        try { await peer.CloseAsync(1009, "Message too large").ConfigureAwait(false); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
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
                try { mgr.Disconnect(connection!); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
            }
        };
        // Register the endpoint against the typed app contract.
        wapp.WebSocket("/ws", endpoint);
        return;
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
