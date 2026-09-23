using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atheriz.Core.Network;

// WebSocket-specific implementation of BaseConnection.
// Line numbers referenced in comments.

public sealed class WebSocketConnection : BaseConnection
{
    public System.Net.WebSockets.WebSocket WebSocket { get; }
    private Task? _closeTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly PendingLimiter _limiter; // sole accounting (single source of truth)
    // Close flag (with _limiter.IsClosing forms IsClosing).
    private bool _closing;
    private int _disposeManaged;

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
        ClientHost = clientHost ?? "?";
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
            DisposeManaged();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        // Async join: awaits in-flight sends with WaitAsync instead of parking
        // the caller. A timeout proceeds past the bound, mirroring the sync
        // Wait above (which ignores the return and proceeds).
        try
        {
            var pending = _limiter.SnapshotTasks().ToArray();
            if (pending.Length != 0) await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (TimeoutException) { }
        catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.DisposeAsync: " + logEx.Message, "WebSocketConnection"); }
        DisposeManaged();
        base.Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Socket/semaphore teardown shared by both dispose paths (idempotent:
    // async and sync dispose may race, and Dispose may run twice).
    private void DisposeManaged()
    {
        if (Interlocked.Exchange(ref _disposeManaged, 1) != 0) return;
        try { WebSocket.Abort(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.Dispose: " + logEx.Message, "WebSocketConnection"); }
        try { WebSocket.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.Dispose: " + logEx.Message, "WebSocketConnection"); }
        try { _sendLock.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed WebSocketConnection.Dispose: " + logEx.Message, "WebSocketConnection"); }
    }

// now via PendingLimiter sole accounting
    // (SendCommand reserves/tracks inline below; this helper was dead code.)
// now via PendingLimiter sole accounting
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

// bounded: a hung peer must not
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

// now via PendingLimiter sole accounting
    public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
    {
        if (cmd == "echo_on") return;
        if (cmd == "prompt_masked") cmd = "prompt";
        args ??= [];
        kwargs ??= [];
        // Single UTF8 pass: serialize straight to bytes and reuse the length
        // for the reservation (was GetByteCount + GetBytes). Wire bytes are
        // identical — same payload shape, same options.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new object[] { cmd, args, kwargs }, SendJsonOptions);
        var nb = bytes.Length;
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
// reserve -> schedule -> Track in one
            // guarded span: if Track itself throws, the reservation is released
            // and the task observed (the old split leaked the limiter slot).
            task = Task.Run(() => LockedSendAsync(bytes));
            _limiter.Track(task, nb);
            // the completion callback rides the owning try — if the
            // attach itself throws, the catch below releases the reservation
            _ = task.ContinueWith(t => TaskDone(t), TaskScheduler.Default);
        }
        catch (Exception e)
        {
            // Single release — the reservation is either tracked to task
            // or untracked (Task.Run/Track threw first). The old ReleaseSync
            // + re-attached TaskDone subtracted twice, and Release's clamp
            // then wiped unrelated legitimate debt. No re-attach: nothing
            // will complete this task through the limiter again.
            _limiter.ReleaseAttachFailure(task, nb);
            Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Error sending command: {e}");
            return;
        }
    }

// now via limiter snapshot
    private async Task CloseWebSocketAsync()
    {
        List<Task> pending = _limiter.SnapshotTasks();
        if (pending.Count > 0)
        {
            try
            {
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
        catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.CloseWebSocketAsync: " + logEx.Message, "WebSocket"); }
    }

// now via limiter sole accounting
    public override void Close()
    {
        _closing = true;
        if (!_limiter.TryMarkClosing()) return;
        try
        {
// scheduled via Task.Run.
            // Observe the task: an unobserved close fault must reach the log, not the finalizer.
            _closeTask = Task.Run(() => CloseWebSocketAsync());
            _ = _closeTask.ContinueWith(t => TaskDone(t), TaskScheduler.Default);
        }
        catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Error closing connection: {e}"); }
    }

    internal PendingLimiter Limiter => _limiter;
}

// Typed contracts replacing the Python @app.websocket decorator duck-typing
// production never routes real sockets through WebSocketProtocol.Setup
// (Server's Program.cs maps /ws explicitly).
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
public interface IWebSocketDisconnect { }

public sealed class WebSocketProtocol : BaseProtocol
{
    // Per-protocol state: a static holder would share per-host suppression
    // across test and game worlds, so a burst in one silences another.
    private readonly ThrottledLog _oversizeLog = new(OversizeWindow);
    private const double OversizeWindow = 5.0;

    private bool ShouldLogOversize(string host)
        => _oversizeLog.ShouldLog(host);

    // Test doubles implement IWebSocketApp/IWebSocketPeer ( FakeApp /
    // MockWsEndpoint in PortedWebSocketTests); the typed
    // System.Net.WebSockets.WebSocket path lives in WebSocketConnection and
    // the real server registers /ws explicitly in Server's Program.cs.
    public override void Setup(object app)
    {
        AtherizSettings settings = AtherizSettings.Global;
        if (app is IHost host)
        {
            try { settings = host.Services.GetRequiredService<AtherizSettings>(); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed WebSocketConnection.Setup: " + logEx.Message, "WebSocket"); }
        }

        if (!settings.WebsocketEnabled) return;
        // Real WebApplication is handled directly in Server's Program.cs
        // (explicit Map), not here: only IWebSocketApp doubles register.
        if (app is not IWebSocketApp wapp) return;

        // Create the endpoint delegate that mirrors websocket.py:163-199
        Func<IWebSocketPeer, System.Threading.Tasks.Task> endpoint = async (IWebSocketPeer peer) =>
        {
            string clientHost = (peer.Client as IWebSocketClientInfo)?.Host ?? "?";
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
// throttled via ThrottleWindow (5s per host)
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
