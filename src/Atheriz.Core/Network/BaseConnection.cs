using Atheriz.Core.Concurrency;
using Atheriz.Core.Objects; // Session now in Objects.Session (standalone)

namespace Atheriz.Core.Network;

// abstract interface for all network
// connections. Specific protocol implementations (WebSocket, Telnet, etc)
// inherit from this and implement SendCommand and Close.

/// <summary>
/// Abstract interface for all network connections.
/// Thread-safe FIFO input pipeline via _inputQueue, bounded by CONNECTION_INPUT_QUEUE_LIMIT (100).
/// </summary>
public abstract class BaseConnection : Atheriz.Core.Commands.IMessageTarget, Atheriz.Core.Commands.ISessionProvider, IDisposable, IAsyncDisposable
{
    public string? SessionId { get; }
    public Session Session { get; }
    public int ThreadId { get; }
    public readonly object Lock = new();
    public int FailedLoginAttempts;
    private bool _disposed;
    // Lifetime for the awaited retry/re-arm loops below: cancelled on
    // Dispose so a pending Task.Delay dies quietly instead of firing into a
    // cleared queue. Never disposed (Token must stay readable post-cancel).
    private readonly CancellationTokenSource _retryCts = new();
    internal CancellationToken RetryLifetimeToken => _retryCts.Token;

    /// <summary>Releases owned resources (queues/sessions; subclasses add
    /// sockets/semaphores).</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Async dispose: cancels pending retry loops, then releases.
    /// Subclasses with I/O joins override to await them first.</summary>
    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            try { _retryCts.Cancel(); } catch { }
            try { ClearPendingInput(); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed BaseConnection.Dispose: " + logEx.Message, "BaseConnection"); }
        }
    }

    protected bool IsDisposed => _disposed;

    // Per-connection input pipeline (issue #31)
    // Nominal input item (was a value tuple): the handler triple keeps its
    // Handler/Args/Kwargs member names, now on a compiler-checked type.
    private sealed record QueuedInput(Delegate Handler, List<object?> Args, Dictionary<string, object?> Kwargs);
    private readonly Queue<QueuedInput> _inputQueue = new();
    private bool _inputRunning;
    private double _lastInputBusy = double.NegativeInfinity; // 0.0 is a real timestamp, not "never".
    private bool _disconnected;
    public string ClientHost { get; set; } = "?"; // set by subclasses; defaults to "?".
    /// <summary>UTC creation time; drives the orphan-sweep for never-logged-in sockets.</summary>
    public DateTime ConnectedAtUtc { get; } = DateTime.UtcNow;

    // Async threadpool resolution via AsyncThreadPool; IsOnLoopThread checks ThreadId.

    protected BaseConnection(string? sessionId = null)
    {
        SessionId = sessionId;
        Session = new Session(connection: this);
        ThreadId = Environment.CurrentManagedThreadId;
    }

// faithful null outside async context
    public object? Loop => null;
    public bool IsOnLoopThread()
    {
        return Environment.CurrentManagedThreadId == ThreadId;
    }

    private static AtherizSettings DefaultSettings => AtherizSettings.Global;
    private static int ConnectionInputQueueLimit => DefaultSettings.ConnectionInputQueueLimit;
    private static readonly Lazy<AsyncThreadPool> _fallbackPool = new(() => new AsyncThreadPool());
    private static AsyncThreadPool FallbackPool => _fallbackPool.Value;

    // The fallback pool is process-lifetime; shut it down (non-blocking)
    // on exit so a leftover full queue cannot pin the process... (threads are
    // background anyway; this is belt-and-braces for hosted test runners).
    static BaseConnection()
    {
        try { AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { ShutdownFallbackPool(); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed BaseConnection.ProcessExit: " + logEx.Message, "BaseConnection"); } }; }
        catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed BaseConnection.cctor: " + logEx.Message, "BaseConnection"); }
    }

    public static void ShutdownFallbackPool()
    {
        try { if (_fallbackPool.IsValueCreated) _fallbackPool.Value.Stop(false); }
        catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[Network] fallback pool shutdown failed: {e}"); }
    }

    // Aggregate cap on RetryDrain chains. One chain per connection is
    // bounded (50ms cadence), but chains are unbounded ACROSS connections — a
    // stuck pool + connection churn would pile Task.Delay continuations forever.
    private static int _outstandingRetryDrains;
    private const int MaxOutstandingRetryDrains = 1024;
    // Named retry-drain windows/delays (values identical to the old literals;
    // each keeps its own semantic — no conflation).
    private const double RetryDrainDropWindowSeconds = 5.0;
    private static readonly TimeSpan RetryDrainDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RetryDrainRearmDelay = TimeSpan.FromMilliseconds(250);
    private const double InputBusyWindowSeconds = 1.0;
    // Per-connection state: a static holder would share the fixed-key
    // suppression across connections, so one connection's full backlog
    // silences every other's drop warning.
    private readonly ThrottledLog _retryDrainDropLog = new(RetryDrainDropWindowSeconds);

    private static bool TryScheduleRetryDrain(BaseConnection self)
    {
        if (Interlocked.Increment(ref _outstandingRetryDrains) > MaxOutstandingRetryDrains)
        {
            Interlocked.Decrement(ref _outstandingRetryDrains);
            if (self._retryDrainDropLog.ShouldLog("retry-drain"))
                Atheriz.Core.AtherizLogger.LogWarning("[Network] retry-drain backlog full; dropping retry");
            return false;
        }
        try
        {
            _ = RetryLoopAsync(self, RetryDrainDelay, self._retryCts.Token);
            return true;
        }
        catch (Exception logEx)
        {
            Interlocked.Decrement(ref _outstandingRetryDrains);
            Atheriz.Core.AtherizLogger.LogDebug("Suppressed BaseConnection.TryScheduleRetryDrain: " + logEx.Message, "BaseConnection");
            return false;
        }
    }

    // Awaited retry loop: replaces the Task.Delay(...).ContinueWith chain.
    // The delay honors the connection lifetime token, so shutdown/dispose
    // cancels a pending retry instead of firing into a cleared queue; the
    // gauge decrement rides the finally exactly as the continuation did.
    private static async Task RetryLoopAsync(BaseConnection self, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { Interlocked.Decrement(ref _outstandingRetryDrains); return; }
        try { self.RetryDrain(); }
        finally { Interlocked.Decrement(ref _outstandingRetryDrains); }
    }

    // Awaited re-arm chain: one pending re-arm timer per stalled connection
    // (chain, not fan-out), cancelled on dispose. The inner try/catch mirrors
    // the old ContinueWith's swallow.
    private static async Task RearmLoopAsync(BaseConnection self, CancellationToken ct)
    {
        try
        {
            await Task.Delay(RetryDrainRearmDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        try { ScheduleRetryDrain(self); } catch { }
    }

    // Retry scheduler with re-arm: when the capped scheduler is full the retry
    // is dropped, so re-arm through the cap after a delay instead of losing the
    // drain — otherwise queued input stalls until unrelated input arrives.
    // One pending re-arm timer per stalled connection (chain, not fan-out);
    // RetryDrain itself no-ops when there is nothing to do.
    private static void ScheduleRetryDrain(BaseConnection self)
    {
        if (TryScheduleRetryDrain(self)) return;
        lock (self.Lock)
        {
            if (self._disposed || self._disconnected || self._inputQueue.Count == 0) return;
        }
        _ = RearmLoopAsync(self, self._retryCts.Token);
    }

    private AsyncThreadPool ResolvePool()
    {
        // Prefer ConnectionManager singleton's pool if available
        try
        {
            var mgr = ConnectionManager.GlobalInstance;
            if (mgr?.Atp is not null) return mgr.Atp;
        }
        catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed BaseConnection.ResolvePool: " + logEx.Message, "BaseConnection"); }
        return FallbackPool;
    }

// throttling now via ThrottleWindow (1s window)
    // Queues one input handler for serialized execution on the game threadpool.
    // When queue >= CONNECTION_INPUT_QUEUE_LIMIT, newest message is dropped and
    // client gets throttled busy reply (1s window) — see #32. When the drain
    // task cannot be submitted the message stays queued with a retry armed, and
    // the client is told it is queued — never "dropped".
    public void EnqueueInput(Delegate handler, List<object?> args, Dictionary<string, object?> kwargs)
    {
        if (_disposed) return;
        bool notifyBusy = false;
        bool notifyRetry = false;
        bool needsDrain = false;
        int pendingCount = 0;
        lock (Lock)
        {
            if (_disconnected || _disposed) return;
            if (_inputQueue.Count >= ConnectionInputQueueLimit)
            {
                var now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
                if (!ThrottleWindow.ShouldLog(ref _lastInputBusy, InputBusyWindowSeconds, now)) return;
                // The busy log below reports this count: capture the full
                // queue size here, not just on the pool-failure path.
                pendingCount = _inputQueue.Count;
                notifyBusy = true;
            }
            else
            {
                _inputQueue.Enqueue(new QueuedInput(handler, args, kwargs));
                if (_inputRunning) return;
                _inputRunning = true;
                needsDrain = true;
            }
        }
        if (needsDrain)
        {
            if (TryAddDrainTask()) return;
            lock (Lock)
            {
                _inputRunning = false;
                pendingCount = _inputQueue.Count;
                var now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
                if (ThrottleWindow.ShouldLog(ref _lastInputBusy, InputBusyWindowSeconds, now))
                {
                    notifyRetry = true;
                }
            }
            try
            {
                ScheduleRetryDrain(this);
            }
            catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed BaseConnection.EnqueueInput: " + logEx.Message, "BaseConnection"); }
        }
        if (notifyBusy)
        {
            ConnectionManager.NetWarn($"[Network] Input queue full; input dropped; {pendingCount} message(s) pending");
            Msg("Server busy; input dropped.");
        }
        if (notifyRetry)
        {
            // Drain submission failed but the message is queued and a retry is
            // armed: report queued/retrying, not dropped.
            ConnectionManager.NetWarn($"[Network] Input queue submission rejected (pool full); {pendingCount} message(s) pending retry");
            Msg("Server busy; input queued; retrying.");
        }
    }

    private bool TryAddDrainTask()
    {
        var pool = ResolvePool();
        return pool.AddTask(DrainInput);
    }

    private void RetryDrain()
    {
        lock (Lock)
        {
            if (_disconnected) return;
            if (_inputQueue.Count == 0 || _inputRunning) return;
            _inputRunning = true;
        }
        if (TryAddDrainTask()) return;
        lock (Lock) { _inputRunning = false; }
        // Stopped pool: AddTask will never succeed again, so rescheduling
        // would spin a 50ms timer chain forever on a live connection with
        // queued input. Drop the retry (shutdown disconnects clear the
        // queue) and terminate the chain instead.
        try
        {
            if (ResolvePool().IsStopped)
            {
                Atheriz.Core.AtherizLogger.LogWarning("[Network] threadpool stopped; dropping input-drain retry");
                return;
            }
        }
        catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed BaseConnection.RetryDrain: " + logEx.Message, "BaseConnection"); }
        ScheduleRetryDrain(this);
    }

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
                if (_inputQueue.Count == 0) { _inputRunning = false; return; }
                if (_disconnected) { _inputQueue.Clear(); _inputRunning = false; return; }
                var item = _inputQueue.Dequeue();
                handler = item.Handler;
                args = item.Args;
                kwargs = item.Kwargs;
            }
            try
            {
                // Typed dispatch (F001): registered handlers are
                // Action<BaseConnection, List<object?>, Dictionary<string, object?>>.
                // Anything else is a shape error: log and continue draining
                // (same outcome as the old DynamicInvoke arity failure).
                if (handler is Action<BaseConnection, List<object?>, Dictionary<string, object?>> typed)
                    typed(this, args, kwargs);
                else
                    throw new InvalidOperationException($"Unsupported input handler shape {handler.Method.Name}; register Action<BaseConnection, List<object?>, Dictionary<string, object?>>.");
            }
            catch (Exception ex)
            {
                var name = handler.Method.Name;
                Atheriz.Core.AtherizLogger.LogError($"[Network] Input handler '{name}' failed: {ex}");
            }
        }
    }

    public void ClearPendingInput()
    {
        lock (Lock) { _inputQueue.Clear(); _inputRunning = false; }
    }

    internal void SetDisconnected(bool value)
    {
        lock (Lock) { _disconnected = value; }
    }

    internal bool IsDisconnected
    {
        get { lock (Lock) return _disconnected; }
    }

    // Snapshot of the host at RegisterConnection time: ClientHost is
    // mutable per message (tests/host changes), so counts/disconnect/overwrite
    // use the registration-time value.
    internal string? RegisteredHost { get; set; }

// must be implemented by child classes
    public abstract void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null);

    // Convenience overload for variadic args (used by Msg)
    public void SendCommand(string cmd, params object?[] args)
    {
        SendCommand(cmd, args?.ToList(), null);
    }

    public virtual void LaunchDraw()
    {
        SendCommand("launch_draw", [], []);
    }

    // Maps simple messages to the robust send_command interface.
    // Also handles trailing \r\n and screenreader ANSI stripping.
    public void Msg(string text)
    {
        // Single-arg text path — most common via broadcast
        MsgInternal(new List<object?> { text }, []);
    }
    // Faithful overloads for Python's flexible msg(*args, **kwargs)
    public void Msg() => MsgInternal([], []);
    public void Msg(object? arg) => MsgInternal(new List<object?>{arg}, []);
    public void Msg(object? arg1, object? arg2) => MsgInternal(new List<object?>{arg1, arg2}, []);
    public void Msg(Dictionary<string, object?> kwargs) => MsgInternal([], kwargs);
    public void Msg(List<object?> args, Dictionary<string, object?> kwargs) => MsgInternal(args, kwargs);
    // Expose internal for tests that need kwargs path like msg(text="hi") or msg(prompt=">")
    public void MsgKw(Dictionary<string, object?> kwargs, params object?[] args) => MsgInternal(args?.ToList() ?? [], kwargs ?? []);

    // Full msg handling with args/kwargs — mirrors Python's msg(*args, **kwargs)
    // For C# parity, we expose MsgInternal; callers needing kwargs can use SendCommand directly.
    private void MsgInternal(List<object?> args, Dictionary<string, object?> kwargs)
    {
        string cmd = "text";
        if ((args is null || args.Count == 0) && (kwargs is null || kwargs.Count == 0))
            return;

        // Copy the caller's list: the text path mutates args[0] below and the
        // kwargs path re-roots args — neither may alias caller state.
        args = args is not null ? new List<object?>(args) : [];
        // outgoing_kwargs = dict(kwargs) at connection.py:182
        var outgoingKwargs = kwargs is not null ? new Dictionary<string, object?>(kwargs) : [];

        if (outgoingKwargs.Count > 0)
        {
            if (outgoingKwargs.TryGetValue("text", out var textVal) && textVal is string t && !string.IsNullOrEmpty(t))
            {
                outgoingKwargs.Remove("text");
                args.Insert(0, t);
            }
            else if (outgoingKwargs.Count > 0)
            {
                // Python popitem() takes the LAST-inserted kwarg
                // (dict LIFO); First() took the first — same arbitrariness,
                // opposite end. Take the last key explicitly so the mapping
                // is specified and matches Python.
                var lastKey = outgoingKwargs.Keys.Last();
                var lastVal = outgoingKwargs[lastKey];
                outgoingKwargs.Remove(lastKey);
                cmd = lastKey;
                args.Insert(0, lastVal);
            }
        }

        if (cmd == "text" && args.Count > 0)
        {
            if (args[0] is not string)
                args[0] = args[0]?.ToString() ?? "";
            var s = (string)args[0]!;
            if (!s.EndsWith("\r\n", StringComparison.Ordinal) && !s.EndsWith("\n", StringComparison.Ordinal))
                s += "\r\n";
            if (Session.ScreenReader)
                s = GameUtils.StripAnsi(s);
            args[0] = s;
        }
        SendCommand(cmd, args, outgoingKwargs);
    }

    // IMessageTarget implementation (explicit)
    void Atheriz.Core.Commands.IMessageTarget.Msg(string text) => Msg(text);

// must be implemented by child classes
    public abstract void Close();
}
