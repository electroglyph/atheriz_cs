using Atheriz.Core.Concurrency;
using Atheriz.Core.Objects; // Port of atheriz/objects/session.py:202 — Session now in Objects.Session (standalone)
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Network;

// Port of atheriz/network/connection.py:16-207
// Faithful C# port of BaseConnection — abstract interface for all network
// connections. Specific protocol implementations (WebSocket, Telnet, etc)
// inherit from this and implement SendCommand and Close.
// See connection.py:16-207 for original semantics.
// Session is now standalone in Objects.Session — Port of atheriz/objects/session.py:202 (see Objects/Session.cs)

/// <summary>
/// Abstract interface for all network connections. Mirrors <c>atheriz/network/connection.py:BaseConnection</c> (207 LOC).
/// Thread-safe FIFO input pipeline via _inputQueue, bounded by CONNECTION_INPUT_QUEUE_LIMIT (100).
/// </summary>
public abstract class BaseConnection : Atheriz.Core.Commands.IMessageTarget
{
    // port of connection.py:23-40 __init__
    public string? SessionId { get; }
    public Session Session { get; }
    public int ThreadId { get; } // port of connection.py:31 threading.get_ident()
    public readonly object Lock = new object(); // port of connection.py:32 RLock
    public int FailedLoginAttempts; // port of connection.py:33

    // Per-connection input pipeline (issue #31) — connection.py:34-40
    private readonly Queue<(Delegate Handler, List<object?> Args, Dictionary<string, object?> Kwargs)> _inputQueue = new();
    private bool _inputRunning; // port of connection.py:38
    private double _lastInputBusy; // port of connection.py:39
    private bool _disconnected; // port of connection.py:40
    public string ClientHost { get; set; } = "?"; // set by subclasses; mirrors Python's client_host fallback "?"

    // Async threadpool resolution — mirrors connection.py:42-65 _resolve_loop / _is_on_loop_thread
    // In C# we use AsyncThreadPool instead of asyncio loop; IsOnLoopThread checks ThreadId.

    protected BaseConnection(string? sessionId = null)
    {
        SessionId = sessionId;
        Session = new Session(connection: this);
        ThreadId = Environment.CurrentManagedThreadId;
        // Try capture running loop if any — not applicable in C#, keep for parity comment
        // port of connection.py:27-30 loop capture omitted; ThreadId used instead
    }

    // Port of connection.py:28-30 loop capture — faithful null outside async context
    public object? Loop => null;
    // Port of connection.py:53-65 _is_on_loop_thread
    public bool IsOnLoopThread()
    {
        return Environment.CurrentManagedThreadId == ThreadId;
    }

    // Settings helper — mirrors settings.CONNECTION_INPUT_QUEUE_LIMIT at settings.py:81
    private static AtherizSettings DefaultSettings => AtherizSettings.Global;
    private static int ConnectionInputQueueLimit => DefaultSettings.ConnectionInputQueueLimit;
    private static AsyncThreadPool? _fallbackPool;
    private static AsyncThreadPool FallbackPool => _fallbackPool ??= new AsyncThreadPool();

    private AsyncThreadPool ResolvePool()
    {
        // mirrors get_async_threadpool() import inside method at connection.py:80
        // Prefer ConnectionManager singleton's pool if available
        try
        {
            var mgr = ConnectionManager.GlobalInstance;
            if (mgr?.Atp != null) return mgr.Atp;
        }
        catch { }
        return FallbackPool;
    }

    [Obsolete("Use TimeProvider.MonotonicSeconds()")]
    private static double MonotonicSeconds()
    {
        // mirrors time.monotonic() at connection.py:87,103 — now via TimeProvider
        return global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
    }

    // Port of connection.py:67-117 enqueue_input — throttling now via ThrottleWindow (1s window)
    // Queues one input handler for serialized execution on the game threadpool.
    // When queue >= CONNECTION_INPUT_QUEUE_LIMIT, newest message is dropped and
    // client gets throttled busy reply (1s window) — see #32.
    public void EnqueueInput(Delegate handler, List<object?> args, Dictionary<string, object?> kwargs)
    {
        bool notifyBusy = false;
        bool needsDrain = false;
        lock (Lock)
        {
            if (_disconnected) return; // port of connection.py:84-85
            if (_inputQueue.Count >= ConnectionInputQueueLimit) // port of connection.py:86
            {
                var now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds(); // port of connection.py:87
                if (!ThrottleWindow.ShouldLog(ref _lastInputBusy, 1.0, now)) return; // port of connection.py:88-90 via ThrottleWindow
                notifyBusy = true; // port of connection.py:91
            }
            else
            {
                _inputQueue.Enqueue((handler, args, kwargs)); // port of connection.py:93
                if (_inputRunning) return; // port of connection.py:94-95
                _inputRunning = true; // port of connection.py:96
                needsDrain = true; // port of connection.py:97
            }
        }
        if (needsDrain) // port of connection.py:98-110
        {
            if (TryAddDrainTask()) return; // port of connection.py:99-100
            lock (Lock) // port of connection.py:101-106
            {
                _inputRunning = false;
                var now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
                if (ThrottleWindow.ShouldLog(ref _lastInputBusy, 1.0, now))
                {
                    notifyBusy = true;
                }
            }
            try
            {
                // port of connection.py:108 threading.Timer(0.05, self._retry_drain).start()
                _ = Task.Delay(TimeSpan.FromMilliseconds(50)).ContinueWith(_ => RetryDrain());
            }
            catch { }
        }
        if (notifyBusy) // port of connection.py:111-116
        {
            try { Atheriz.Core.AtherizLogger.LogWarning($"[Network] Input queue submission rejected (pool full); {_inputQueue.Count} message(s) pending retry"); } catch { Console.Error.WriteLine($"[Network] Input queue submission rejected (pool full); {_inputQueue.Count} message(s) pending retry"); }
            Msg("Server busy; input dropped.");
        }
    }

    private bool TryAddDrainTask()
    {
        var pool = ResolvePool();
        // port of connection.py:99 get_async_threadpool().add_task(self._drain_input)
        return pool.AddTask(DrainInput);
    }

    // Port of connection.py:118-133 _retry_drain
    private void RetryDrain()
    {
        lock (Lock)
        {
            if (_disconnected) return; // port of connection.py:121
            if (_inputQueue.Count == 0 || _inputRunning) return; // port of connection.py:123
            _inputRunning = true; // port of connection.py:125
        }
        if (TryAddDrainTask()) return; // port of connection.py:126-127
        lock (Lock) { _inputRunning = false; } // port of connection.py:128-129
        try
        {
            _ = Task.Delay(TimeSpan.FromMilliseconds(50)).ContinueWith(_ => RetryDrain()); // port of connection.py:131
        }
        catch { }
    }

    // Port of connection.py:135-153 _drain_input
    // Worker-side: run queued input handlers FIFO until queue empties.
    private void DrainInput()
    {
        while (true)
        {
            Delegate handler;
            List<object?> args;
            Dictionary<string, object?> kwargs;
            lock (Lock)
            {
                if (_inputQueue.Count == 0) { _inputRunning = false; return; } // port of connection.py:139-141
                if (_disconnected) { _inputQueue.Clear(); _inputRunning = false; return; } // port of connection.py:142-145
                var item = _inputQueue.Dequeue(); // port of connection.py:146
                if (_disconnected) continue; // port of connection.py:147-148
                handler = item.Handler;
                args = item.Args;
                kwargs = item.Kwargs;
            }
            try
            {
                handler.DynamicInvoke(this, args, kwargs); // port of connection.py:150
            }
            catch (Exception ex)
            {
                var name = handler.Method.Name ?? handler.ToString();
                try { Atheriz.Core.AtherizLogger.LogError($"[Network] Input handler '{name}' failed: {ex}"); } catch { Console.Error.WriteLine($"[Network] Input handler '{name}' failed: {ex}"); } // port of connection.py:152-153
            }
        }
    }

    // Port of connection.py:155-159 clear_pending_input
    public void ClearPendingInput()
    {
        lock (Lock) { _inputQueue.Clear(); _inputRunning = false; }
    }

    // Port of connection.py:139-141 internal helper for Disconnect to set _disconnected
    internal void SetDisconnected(bool value)
    {
        lock (Lock) { _disconnected = value; }
    }

    internal bool IsDisconnected
    {
        get { lock (Lock) return _disconnected; }
    }

    // Port of connection.py:162-167 send_command — must be implemented by child classes
    public abstract void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null);

    // Convenience overload for variadic args (used by Msg)
    public void SendCommand(string cmd, params object?[] args)
    {
        SendCommand(cmd, args?.ToList(), null);
    }

    // Port of connection.py:169-171 launch_draw
    public virtual void LaunchDraw()
    {
        SendCommand("launch_draw", new List<object?>(), new Dictionary<string, object?>());
    }

    // Port of connection.py:173-199 msg
    // Maps simple messages to the robust send_command interface.
    // Also handles trailing \r\n and screenreader ANSI stripping.
    public void Msg(string text)
    {
        // Single-arg text path — most common via broadcast
        MsgInternal(new List<object?> { text }, new Dictionary<string, object?>());
    }
    // Faithful overloads for Python's flexible msg(*args, **kwargs)
    public void Msg() => MsgInternal(new List<object?>(), new Dictionary<string, object?>());
    public void Msg(object? arg) => MsgInternal(new List<object?>{arg}, new Dictionary<string, object?>());
    public void Msg(object? arg1, object? arg2) => MsgInternal(new List<object?>{arg1, arg2}, new Dictionary<string, object?>());
    public void Msg(Dictionary<string, object?> kwargs) => MsgInternal(new List<object?>(), kwargs);
    public void Msg(List<object?> args, Dictionary<string, object?> kwargs) => MsgInternal(args, kwargs);
    // Expose internal for tests that need kwargs path like msg(text="hi") or msg(prompt=">")
    public void MsgKw(Dictionary<string, object?> kwargs, params object?[] args) => MsgInternal(args?.ToList() ?? new List<object?>(), kwargs ?? new Dictionary<string, object?>());

    // Full msg handling with args/kwargs — mirrors Python's msg(*args, **kwargs)
    // For C# parity, we expose MsgInternal; callers needing kwargs can use SendCommand directly.
    private void MsgInternal(List<object?> args, Dictionary<string, object?> kwargs)
    {
        // port of connection.py:173-199
        string cmd = "text";
        if ((args == null || args.Count == 0) && (kwargs == null || kwargs.Count == 0))
            return; // port of connection.py:179-180

        args ??= new List<object?>();
        // outgoing_kwargs = dict(kwargs) at connection.py:182
        var outgoingKwargs = kwargs != null ? new Dictionary<string, object?>(kwargs) : new Dictionary<string, object?>();

        if (outgoingKwargs.Count > 0) // port of connection.py:183
        {
            if (outgoingKwargs.TryGetValue("text", out var textVal) && textVal is string t && !string.IsNullOrEmpty(t)) // port of connection.py:184-186
            {
                outgoingKwargs.Remove("text");
                args.Insert(0, t);
            }
            else if (outgoingKwargs.Count > 0) // port of connection.py:187-190
            {
                var kv = outgoingKwargs.First();
                outgoingKwargs.Remove(kv.Key);
                cmd = kv.Key;
                args = new List<object?> { kv.Value }.Concat(args).ToList();
            }
        }

        if (cmd == "text" && args.Count > 0) // port of connection.py:192-198
        {
            if (args[0] is not string)
                args[0] = args[0]?.ToString() ?? "";
            var s = (string)args[0]!;
            if (!s.EndsWith("\r\n") && !s.EndsWith("\n"))
                s += "\r\n";
            if (Session.ScreenReader) // port of connection.py:197-198
                s = GameUtils.StripAnsi(s);
            args[0] = s;
        }
        SendCommand(cmd, args, outgoingKwargs);
    }

    // IMessageTarget implementation (explicit)
    void Atheriz.Core.Commands.IMessageTarget.Msg(string text) => Msg(text);

    // Port of connection.py:201-207 close — must be implemented by child classes
    public abstract void Close();
}
