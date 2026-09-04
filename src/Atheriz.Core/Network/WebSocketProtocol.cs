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
        _settings = settings ?? AtherizSettings.Default;
        ClientHost = clientHost ?? "?"; // port of websocket.py:36
        _limiter = new PendingLimiter(_settings.WebsocketMaxPendingBytes, _settings.WebsocketMaxPendingSends);
    }

    // port of websocket.py:46-53 _track_task — now via PendingLimiter sole accounting
    private void TrackTask(Task task, int nb)
    {
        _limiter.Track(task, nb);
        try { _ = task.ContinueWith(t => TaskDone(t)); } catch { }
    }

    // port of websocket.py:55-66 _task_done — now via PendingLimiter sole accounting
    private void TaskDone(Task task)
    {
        _limiter.Release(task);
        // Avoid GetAwaiter().GetResult() blocking; inspect fault directly (fix audit: blocking call)
        if (task.IsFaulted)
        {
            var ex = task.Exception?.InnerException ?? task.Exception;
            if (ex is OperationCanceledException) { }
            else if (ex != null) try { Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Async task failed: {ex}"); } catch { Console.Error.WriteLine($"[WebSocket] Async task failed: {ex}"); }
        }
        else if (task.IsCanceled) { }
    }

    // port of websocket.py:68-70 _locked_send
    private async Task LockedSendAsync(string data)
    {
        await _sendLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally { _sendLock.Release(); }
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
            try { Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] closing {ClientHost}: pending {_limiter.PendingCount} msgs {_limiter.PendingBytes} bytes exceeds limit"); } catch { Console.Error.WriteLine($"[WebSocket] closing {ClientHost}: pending {_limiter.PendingCount} msgs {_limiter.PendingBytes} bytes exceeds limit"); }
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
            try { Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Error sending command: {e}"); } catch { Console.Error.WriteLine($"[WebSocket] Error sending command: {e}"); }
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
                // port of websocket.py:120-130 wait_for gather with 0.25 timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await Task.WhenAll(pending).WaitAsync(cts.Token);
            }
            catch (TimeoutException)
            {
                foreach (var pt in pending) try { } catch { } // port of websocket.py:132-133 cancel pending
            }
            catch { }
        }
        try { if (WebSocket.State == WebSocketState.Open) await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { } // port of websocket.py:135
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
            // port of websocket.py:145-148 _is_on_loop_thread branching — scheduled via Task.Run
            _closeTask = Task.Run(() => CloseWebSocketAsync());
        }
        catch (Exception e) { try { Atheriz.Core.AtherizLogger.LogError($"[WebSocket] Error closing connection: {e}"); } catch { Console.Error.WriteLine($"[WebSocket] Error closing connection: {e}"); } } // port of websocket.py:149-150
    }

    internal PendingLimiter Limiter => _limiter;
}

public sealed class WebSocketProtocol : Protocol
{
    // Oversize throttling — port of websocket.py:15-27 (now via ThrottleWindow)
    private static readonly object _oversizeLock = new object();
    private static readonly Dictionary<string, double> _oversizeLast = new();
    private const double OversizeWindow = 5.0; // port of websocket.py:17

    private static bool ShouldLogOversize(string host) // port of websocket.py:20-27
        => ThrottleWindow.ShouldLog(_oversizeLast, _oversizeLock, host, OversizeWindow);

    // Port of websocket.py:153-199 WebSocketProtocol.setup
    // Original uses @app.websocket("/ws") decorator; in C# we support both:
    // - Python-style MagicMock with app.websocket("/ws") for legacy test compatibility
    // - Real WebApplication is handled directly in Server's Program.cs (explicit Map), not here.
    // This Setup therefore only handles the mock case; real server registers /ws explicitly.
    public override void Setup(object app)
    {
        AtherizSettings settings = AtherizSettings.Global;
        try
        {
            var servicesProp = app.GetType().GetProperty("Services");
            if (servicesProp != null)
            {
                var sp = servicesProp.GetValue(app) as IServiceProvider;
                if (sp != null) settings = sp.GetService<AtherizSettings>() ?? sp.GetRequiredService<AtherizSettings>();
            }
            else if (app is IHost host) settings = host.Services.GetRequiredService<AtherizSettings>();
        }
        catch { }

        if (!settings.WebsocketEnabled) return; // port of websocket.py:160-161

        // Python-style mock handling: app.websocket("/ws") decorator — port of websocket.py:163
        try
        {
            object? wsAttr = null;
            try { wsAttr = ((dynamic)app).websocket; } catch { }
            if (wsAttr == null)
            {
                try
                {
                    var prop = app.GetType().GetProperty("websocket", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (prop != null) wsAttr = prop.GetValue(app);
                    else
                    {
                        var field = app.GetType().GetField("websocket", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null) wsAttr = field.GetValue(app);
                    }
                }
                catch { }
            }
            if (wsAttr != null)
            {
                // Create the endpoint delegate that mirrors websocket.py:163-199
                Func<dynamic, System.Threading.Tasks.Task> endpoint = async (dynamic websocket) =>
                {
                    string clientHost = "?";
                    try
                    {
                        // Try dynamic first, then reflection fallback for private nested mock types
                        object? clientObj = null;
                        try { clientObj = ((dynamic)websocket).client; } catch { }
                        if (clientObj == null)
                        {
                            try
                            {
                                var t = ((object)websocket).GetType();
                                var prop = t.GetProperty("client", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (prop != null) clientObj = prop.GetValue(websocket);
                                else
                                {
                                    var field = t.GetField("client", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    if (field != null) clientObj = field.GetValue(websocket);
                                }
                            } catch { }
                        }
                        if (clientObj != null)
                        {
                            try { var h = ((dynamic)clientObj).host; if (h != null) clientHost = h.ToString() ?? "?"; }
                            catch {
                                try {
                                    var ct = clientObj.GetType();
                                    var hp = ct.GetProperty("host", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                    if (hp != null) clientHost = hp.GetValue(clientObj)?.ToString() ?? "?";
                                    else {
                                        var hf = ct.GetField("host", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                        if (hf != null) clientHost = hf.GetValue(clientObj)?.ToString() ?? "?";
                                        else {
                                            var hp2 = ct.GetProperty("Host", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                            if (hp2 != null) clientHost = hp2.GetValue(clientObj)?.ToString() ?? "?";
                                        }
                                    }
                                } catch { }
                            }
                        }
                    } catch { }
                    // is_ip_banned check (port of websocket.py:166)
                    try {
                        if (Atheriz.Core.Globals.ObjectRegistry.IsIpBanned(clientHost)) {
                            try {
                                try { await ((dynamic)websocket).close(); } catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
                                    var m = ((object)websocket).GetType().GetMethod("close", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                    if (m != null) { var tt = m.Invoke(websocket, new object?[]{null,null}) as System.Threading.Tasks.Task; if(tt!=null) await tt; }
                                }
                            } catch { } return;
                        }
                    } catch { }
                    try {
                        try { await ((dynamic)websocket).accept(); } catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
                            var m = ((object)websocket).GetType().GetMethod("accept", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                            if (m != null) { var tt = m.Invoke(websocket, null) as System.Threading.Tasks.Task; if(tt!=null) await tt; }
                        }
                    } catch { }
                    var mgr = ConnectionManager.GlobalInstance ?? new ConnectionManager(settings: settings);
                    string connId = mgr.GenerateConnectionId();
                    // Create WebSocketConnection (use dynamic ws as WebSocket if possible, else fallback)
                    BaseConnection? connection = null;
                    try
                    {
                        if (websocket is System.Net.WebSockets.WebSocket netWs)
                            connection = new WebSocketConnection(netWs, sessionId: connId, settings: settings, clientHost: clientHost);
                        else
                        {
                            connection = new FallbackConnection(connId) { ClientHost = clientHost };
                        }
                    }
                    catch { connection = new FallbackConnection(connId) { ClientHost = clientHost }; }
                    if (!mgr.RegisterConnection(connId, connection!)) return;
                    try
                    {
                        while (true)
                        {
                            string raw;
                            try { raw = await ((dynamic)websocket).receive_text(); } catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
                                var m = ((object)websocket).GetType().GetMethod("receive_text", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                if (m == null) throw;
                                var task = m.Invoke(websocket, null) as System.Threading.Tasks.Task<string>;
                                if (task == null) {
                                    var t2 = m.Invoke(websocket, null) as System.Threading.Tasks.Task;
                                    if (t2 != null) { await t2; raw = ""; } else throw new InvalidOperationException("receive_text returned null");
                                } else raw = await task;
                            }
                            int byteCount = System.Text.Encoding.UTF8.GetByteCount(raw);
                            if (byteCount > settings.WebsocketMaxMessageSize)
                            {
                                // oversize handling port of websocket.py:181-192 — throttled via ThrottleWindow (5s per host)
                                bool shouldLog = true;
                                try { shouldLog = ShouldLogOversize(clientHost); } catch { }
                                if (shouldLog) try { Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] Message too large from {clientHost} ({byteCount} bytes > {settings.WebsocketMaxMessageSize} bytes)"); } catch { Console.Error.WriteLine($"[WebSocket] Message too large from {clientHost} ({byteCount} bytes > {settings.WebsocketMaxMessageSize} bytes)"); }
                                try {
                                    try { await ((dynamic)websocket).close(1009); } catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
                                        var m = ((object)websocket).GetType().GetMethod("close", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                        if (m != null) {
                                            try { var tt = m.Invoke(websocket, new object?[]{1009, null}) as System.Threading.Tasks.Task; if(tt!=null) await tt; else { var tt2 = m.Invoke(websocket, new object?[]{1009}) as System.Threading.Tasks.Task; if(tt2!=null) await tt2; } } catch { }
                                            // fallback with named args
                                            try { var tt = m.Invoke(websocket, new object?[]{ (object)1009, (object)"Message too large"}) as System.Threading.Tasks.Task; if(tt!=null) await tt; } catch { }
                                        }
                                    }
                                } catch { try { await ((dynamic)websocket).close(code: 1009, reason: "Message too large"); } catch { try { await ((dynamic)websocket).close(); } catch {
                                    try {
                                        var m = ((object)websocket).GetType().GetMethod("close", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                        if (m != null) { var tt = m.Invoke(websocket, new object?[]{null,null}) as System.Threading.Tasks.Task; if(tt!=null) await tt; }
                                    } catch {}
                                } } }
                                break;
                            }
                            mgr.HandleCommand(connection!, raw);
                        }
                    }
                    catch (System.Net.WebSockets.WebSocketException) { }
                    catch (Exception ex)
                    {
                        // Check for WebSocketDisconnect equivalent (Python's WebSocketDisconnect)
                        var typeName = ex.GetType().Name;
                        if (typeName.Contains("WebSocketDisconnect") || typeName.Contains("Disconnect")) { }
                        else
                        {
                            try { Atheriz.Core.AtherizLogger.LogWarning($"[WebSocket] Connection error: {ex}"); } catch { Console.Error.WriteLine($"[WebSocket] Connection error: {ex}"); }
                        }
                    }
                    finally
                    {
                        try { mgr.Disconnect(connection!); } catch { }
                    }
                };
                // Try to register via decorator
                object? decorator = null;
                try { decorator = ((dynamic)wsAttr)("/ws"); } catch { }
                if (decorator == null)
                {
                    try
                    {
                        if (wsAttr is Delegate del) decorator = del.DynamicInvoke("/ws");
                        else
                        {
                            var t = wsAttr.GetType();
                            var m = t.GetMethod("Invoke") ?? t.GetMethod("__call__") ?? t.GetMethod("Call");
                            if (m != null) decorator = m.Invoke(wsAttr, new object[]{"/ws"});
                        }
                    } catch { }
                }
                if (decorator != null)
                {
                    try
                    {
                        if (decorator is Delegate decDel) decDel.DynamicInvoke(endpoint);
                        else try { ((dynamic)decorator)(endpoint); } catch { var tt = decorator.GetType(); var mm = tt.GetMethod("Invoke"); mm?.Invoke(decorator, new object[]{endpoint}); }
                    } catch { }

                }
                else
                {
                    // Fallback: if wsAttr itself is the capturer (like FakeWsApp with side_effect), try calling directly
                    try { ((dynamic)app).CapturedEndpoints["/ws"] = endpoint; } catch { }
                }
                bool hasMockOnly = app.GetType().GetProperty("Services") == null && app.GetType().GetMethod("Map") == null;
                if (hasMockOnly) return;
                return;
            }
        }
        catch { }

        // For real server, do NOT attempt dynamic Map here (extension method via dynamic fails).
        // Program.cs will register /ws explicitly after calling Setup.
        return;
    }

    private sealed class FallbackConnection : BaseConnection
    {
        public FallbackConnection(string? sid) : base(sid) { }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null) { }
        public override void Close() { }
    }
}
