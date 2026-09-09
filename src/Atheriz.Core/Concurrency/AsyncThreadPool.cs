using System.Collections.Concurrent;

namespace Atheriz.Core.Concurrency;

/// <summary>
/// Bounded worker pool mirroring <c>atheriz/globals/asyncthreadpool.py:69</c> AsyncThreadPool.
/// </summary>
public class AsyncThreadPool : IDisposable
{
    public const double ReliefSpawnCooldownSeconds = 1.0;

    private readonly int _maxThreads;
    private readonly int _reliefLimit;
    private readonly TimeSpan _watchdogThreshold;
    private readonly TimeSpan _watchdogInterval;

    // Manual bounded queue (replaces BlockingCollection) — allows capacity expansion on stop like Python.
    private readonly Queue<WorkItem?> _queue = new();
    private readonly object _queueLock = new();
    private int _queueLimit;

    private readonly List<Thread> _fixedThreads = new();
    private readonly List<Thread> _reliefThreads = new();
    private readonly object _lock = new();
    private int _busy;
    private int _reliefCount;
    private long _lastReliefSpawnTicks;
    private long _lastFullLogTicks;
    private bool _stopped;
    // Signalled by Stop so sleep loops exit promptly without Thread.Sleep polling.
    private readonly ManualResetEventSlim _stopEvent = new(false);
    private readonly Dictionary<long, (string Name, double StartedSeconds)> _currentTasks = new();
    private double? _saturatedSince;
    private double _lastStarvationLog;
    private Thread? _watchdogThread;
    private bool _disposed;

    private sealed record WorkItem(Func<Task> Runner, string Name);

    public AsyncThreadPool(
        int? maxThreads = null,
        int? queueLimit = null,
        int? reliefLimit = null,
        TimeSpan? watchdogSeconds = null,
        TimeSpan? watchdogInterval = null)
    {
        _maxThreads = maxThreads ?? (Environment.ProcessorCount);
        if (_maxThreads < 1) _maxThreads = 1;
        _queueLimit = queueLimit ?? 10000;
        _reliefLimit = reliefLimit ?? Environment.ProcessorCount;
        _watchdogThreshold = watchdogSeconds ?? TimeSpan.FromSeconds(30);
        _watchdogInterval = watchdogInterval ?? TimeSpan.FromSeconds(5);

        // Port of Python pool layout: threads[0] is the async thread and
        // threads[1:] are the fixed workers, so maxThreads counts the async
        // slot plus (maxThreads-1) workers. The Threads property exposes the
        // same alignment (dummy async placeholder + fixed workers).
        for (int i = 0; i < _maxThreads - 1; i++)
        {
            var t = new Thread(WorkLoop) { IsBackground = true, Name = $"AtherizWorker-{i}" };
            t.Start();
            _fixedThreads.Add(t);
        }
        if (_fixedThreads.Count == 0)
        {
            var t = new Thread(WorkLoop) { IsBackground = true, Name = "AtherizWorker-0" };
            t.Start();
            _fixedThreads.Add(t);
        }

        _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "AsyncThreadPoolWatchdog" };
        _watchdogThread.Start();
    }

    public int MaxThreads => _maxThreads;
    public int Busy { get { lock (_lock) return _busy; } }
    public int QueueCount
    {
        get
        {
            // Only real queued work is reported: the null control sentinels
            // Stop enqueues for worker shutdown are not user tasks.
            lock (_queueLock) return _queue.Count(i => i != null);
        }
    }
    public int QueueLimit
    {
        get { lock (_queueLock) return _queueLimit; }
        set { lock (_queueLock) { _queueLimit = value; } }
    }
    public int ReliefCount { get { lock (_lock) return _reliefCount; } }
    public IReadOnlyList<Thread> FixedThreads { get { lock (_lock) return _fixedThreads.ToList(); } }
    public IReadOnlyList<Thread> ReliefThreads { get { lock (_lock) return _reliefThreads.ToList(); } }
    public IReadOnlyList<Thread> Threads
    {
        get
        {
            lock (_lock)
            {
                // Mimic Python's threads[0]=AsyncThread, threads[1:]=fixed workers
                var list = new List<Thread>();
                // dummy async placeholder thread (not started) to keep index alignment for tests that check threads[1:]
                // We create a stub thread that is not alive; Python's AsyncThread stops on pool stop.
                var dummy = new Thread(() => {}) { IsBackground = true, Name = "AsyncThread0" };
                list.Add(dummy);
                list.AddRange(_fixedThreads);
                return list;
            }
        }
    }
    public bool IsStopped { get { lock (_lock) return _stopped; } }

    private void WorkLoop(object? arg)
    {
        bool relief = arg is bool b && b;
        while (true)
        {
            WorkItem? item = null;
            if (relief)
            {
                lock (_lock)
                {
                    if (_stopped)
                    {
                        lock (_queueLock) if (_queue.Count == 0)
                        {
                            _reliefCount--;
                            try { _reliefThreads.Remove(Thread.CurrentThread); } catch { }
                            return;
                        }
                    }
                }
                bool got = false;
                lock (_queueLock)
                {
                    if (_queue.Count > 0) { item = _queue.Dequeue(); got = true; }
                }
                if (!got)
                {
                    lock (_queueLock)
                    {
                        if (_queue.Count == 0)
                        {
                            Monitor.Wait(_queueLock, 500);
                        }
                        if (_queue.Count > 0) { item = _queue.Dequeue(); got = true; }
                    }
                    if (!got)
                    {
                        int cnt;
                        lock (_queueLock) cnt = _queue.Count;
                        if (cnt == 0)
                        {
                            lock (_lock)
                            {
                                _reliefCount--;
                                try { _reliefThreads.Remove(Thread.CurrentThread); } catch { }
                            }
                            return;
                        }
                        continue;
                    }
                }
            }
            else
            {
                lock (_queueLock)
                {
                    while (_queue.Count == 0)
                    {
                        Monitor.Wait(_queueLock);
                    }
                    item = _queue.Dequeue();
                }
            }

            if (item is null) // sentinel
            {
                if (relief)
                {
                    lock (_lock)
                    {
                        lock (_queueLock)
                        {
                            // Re-queue sentinel for fixed workers if possible
                            if (_queueLimit == 0 || _queue.Count < _queueLimit)
                            {
                                _queue.Enqueue(null);
                                Monitor.Pulse(_queueLock);
                            }
                            else
                            {
                                // Queue full: expand the limit — exit sentinels are
                                // control messages, not user tasks — so a queued
                                // user task is never silently discarded to make
                                // room for a sentinel.
                                _queueLimit++;
                                _queue.Enqueue(null);
                                Monitor.Pulse(_queueLock);
                            }
                            if (_stopped)
                            {
                                _reliefCount--;
                                try { _reliefThreads.Remove(Thread.CurrentThread); } catch { }
                                return;
                            }
                        }
                    }
                    // Brief yield for fixed workers to pick sentinel (relief thread, not pool worker)
                    _stopEvent.Wait(TimeSpan.FromMilliseconds(50)); lock (_lock) if (_stopped) return;
                    continue;
                }
                break;
            }

            string name = item.Name;
            long ident = Environment.CurrentManagedThreadId;
            double started = Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
            lock (_lock)
            {
                _busy++;
                _currentTasks[ident] = (name, started);
            }
            try
            {
                RunInternal(item);
            }
            finally
            {
                lock (_lock)
                {
                    _busy--;
                    _currentTasks.Remove(ident);
                }
            }
        }
    }

    // Bound on the inline wait for an incomplete async operation .
    // A hung task must not pin a fixed worker forever; on timeout the slot
    // is released and the operation continues detached on the CLR pool with
    // a fault-logging continuation attached.
    private const int WorkItemInlineWaitSeconds = 30;

    private static void RunInternal(WorkItem item)
    {
        // F009: faults go through AtherizLogger (server.log) instead of bare Console.Error.
        // AtherizLogger still echoes to Console.Error, so test log-capture keeps working.
        // the worker slot (Busy count + watchdog entry) is held for the
        // whole async operation, not just the sync prefix: an incomplete task is
        // waited on inline (matching Python holding the worker for the coro
        // lifetime), so maxThreads/queue-limit/relief/watchdog observe real async
        // load instead of seeing Busy==0 while CLR-pool threads do the work.
        try
        {
            var task = item.Runner();
            if (!task.IsCompleted)
            {
                bool done;
                try { done = task.Wait(TimeSpan.FromSeconds(WorkItemInlineWaitSeconds)); }
                catch { done = true; }
                if (!done)
                {
                    // hung async op — release the worker slot instead
                    // of pinning it unboundedly; keep fault visibility via a
                    // detached continuation.
                    try
                    {
                        task.ContinueWith(t =>
                        {
                            if (t.IsFaulted && t.Exception != null) try { AtherizLogger.LogError(t.Exception.ToString()); } catch { Console.Error.WriteLine(t.Exception.ToString()); }
                        }, TaskScheduler.Default);
                    }
                    catch { }
                    try { AtherizLogger.LogError($"Work item exceeded {WorkItemInlineWaitSeconds}s without completing; worker slot released."); } catch { }
                    return;
                }
                if (task.IsFaulted && task.Exception != null) try { AtherizLogger.LogError(task.Exception.ToString()); } catch { Console.Error.WriteLine(task.Exception.ToString()); }
            }
            else if (task.IsFaulted && task.Exception != null)
            {
                try { AtherizLogger.LogError(task.Exception.ToString()); } catch { Console.Error.WriteLine(task.Exception.ToString()); }
            }
        }
        catch (Exception ex) { try { AtherizLogger.LogError(ex.ToString()); } catch { Console.Error.WriteLine(ex.ToString()); } }
    }

    /// <summary>Spawns a relief worker if the pool is saturated and cooldown elapsed. Public so hosts can prod relief.</summary>
    public void MaybeSpawnReliefWorker()
    {
        bool spawn = false;
        int seq = 0;
        lock (_lock)
        {
            if (_stopped) return;
            if (_reliefLimit <= 0) return;
            if (_reliefCount >= _reliefLimit) return;
            if (_busy < _maxThreads - 1) return;
            int qcnt;
            lock (_queueLock) qcnt = _queue.Count;
            if (qcnt == 0) return;
            long now = DateTime.UtcNow.Ticks;
            long cooldownTicks = TimeSpan.FromSeconds(ReliefSpawnCooldownSeconds).Ticks;
            if (now - _lastReliefSpawnTicks < cooldownTicks) return;
            _reliefCount++;
            _lastReliefSpawnTicks = now;
            seq = _reliefCount;
            spawn = true;
        }
        if (spawn)
        {
            var t = new Thread(WorkLoop) { IsBackground = true, Name = $"AtherizRelief-{seq}" };
            // Start before list-add under one lock: a Start throw must neither
            // leak the count nor leave a phantom entry. Relief is
            // opportunistic — spawn failure degrades to unrelieved, so the
            // count is restored and the failure stays local.
            lock (_lock)
            {
                try { t.Start(true); }
                catch { _reliefCount--; return; }
                _reliefThreads.Add(t);
            }
        }
    }

    private void WatchdogLoop()
    {
        while (true)
        {
            // Sleep in slices so Stop can exit quickly instead of blocking up to 5s in one sleep
            // Use 50ms slice to respect small watchdog intervals in tests (e.g. 0.1s)
            var slice = TimeSpan.FromMilliseconds(50);
            var total = TimeSpan.Zero;
            while (total < _watchdogInterval)
            {
                // Event wait instead of Thread.Sleep so Stop wakes the watchdog immediately.
                _stopEvent.Wait(slice);
                total += slice;
                lock (_lock) { if (_stopped) return; }
            }
            bool stopped;
            int busy;
            lock (_lock) { stopped = _stopped; busy = _busy; }
            if (stopped) return;
            int qsize;
            lock (_queueLock) qsize = _queue.Count;
            // use actual queue limit for saturated check, not capped view
            int limit;
            lock (_queueLock) limit = _queueLimit;
            bool saturated = qsize > 0 && (busy >= _maxThreads - 1 || (limit != 0 && qsize >= limit));
            double now = Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
            if (saturated)
            {
                // snapshot under the lock, log after releasing it —
                // LogStarvation does file I/O and must not stall AddInternal/Stop.
                Dictionary<long, (string, double)>? snapshot = null;
                double saturatedFor = 0;
                lock (_lock)
                {
                    _saturatedSince ??= now;
                    if (now - _saturatedSince.Value >= _watchdogThreshold.TotalSeconds &&
                        now - _lastStarvationLog >= _watchdogThreshold.TotalSeconds)
                    {
                        _lastStarvationLog = now;
                        saturatedFor = now - _saturatedSince.Value;
                        snapshot = new Dictionary<long, (string, double)>(_currentTasks);
                    }
                }
                if (snapshot != null)
                    LogStarvation(qsize, busy, saturatedFor, snapshot);
            }
            else
            {
                lock (_lock) _saturatedSince = null;
            }
        }
    }

    private void LogStarvation(int qsize, int busy, double duration, Dictionary<long, (string Name, double Started)> tasks)
    {
        var now = Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        var detail = string.Join(", ", tasks.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value.Name} running {now - kv.Value.Started:F1}s"));
        var msg = $"[AsyncThreadPool] starvation suspected: {qsize} task(s) queued, {busy}/{_maxThreads - 1} workers busy for {duration:F1}s; running: [{detail}]";
        // Log via AtherizLogger which also echoes to Console.Error for CaptureAtherizLog (see Logger.Write).
        // Logger is the last resort here: if it throws there is nowhere left to report.
        try { AtherizLogger.LogError(msg); } catch (Exception) { }
    }

    public virtual bool AddTask(Action action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        string name = action.Method.Name ?? "action";
        return AddInternal(() => { action(); return Task.CompletedTask; }, name);
    }
    public virtual bool AddTask(Func<Task> asyncFunc)
    {
        if (asyncFunc is null) throw new ArgumentNullException(nameof(asyncFunc));
        string name = asyncFunc.Method.Name ?? "asyncFunc";
        return AddInternal(asyncFunc, name);
    }
    public virtual bool AddTask(Action action, string name) => AddInternal(() => { action(); return Task.CompletedTask; }, name);
    public virtual bool AddTask(Func<Task> asyncFunc, string name) => AddInternal(asyncFunc, name);

    // For python test compat: bool AddTask(Delegate) etc.
    // Typed delegate binding (replaces DynamicInvoke): closed Action/Func shapes
    // only. Callers with other shapes must bind arguments into a closure and use
    // the Action/Func<Task> overloads (narrowing of Python's arbitrary callables).
    private static Func<object?> BindDelegate(Delegate del, object?[] args)
    {
        args ??= [];
        if (args.Length == 0)
        {
            if (del is Action a) return () => { a(); return null; };
            if (del is Func<Task> ft) return () => ft();
            throw new ArgumentException($"Unsupported 0-arg delegate shape {del.Method.Name}; use an Action/Func<Task> overload.", nameof(del));
        }
        if (args.Length == 1)
        {
            var a0 = args[0];
            // Reference-type parameters via contravariance (covers Action<string> etc.).
            // Null arg passes through as null, matching the old DynamicInvoke behavior.
            if (del is Action<object> ao) return () => { ao(a0!); return null; };
            if (del is Func<object, Task> fo) return () => fo(a0!);
            if (a0 is int i)
            {
                if (del is Action<int> ai) return () => { ai(i); return null; };
                if (del is Func<int, Task> fi) return () => fi(i);
            }
            if (a0 is bool b)
            {
                if (del is Action<bool> ab) return () => { ab(b); return null; };
                if (del is Func<bool, Task> fb) return () => fb(b);
            }
            throw new ArgumentException($"Unsupported 1-arg delegate shape {del.Method.Name}; bind arguments into an Action/Func<Task> closure.", nameof(del));
        }
        throw new ArgumentException($"AddTask/Run accept at most 1 delegate argument; bind arguments into an Action/Func<Task> closure.", nameof(args));
    }
    public virtual bool AddTask(Delegate del, params object?[] args)
    {
        var bound = BindDelegate(del, args);
        // Fallback: wrap delegate invoke (returned Task intentionally unobserved,
        // matching the historical DynamicInvoke fallback).
        string name = del.Method.Name ?? "delegate";
        return AddInternal(() => { bound(); return Task.CompletedTask; }, name);
    }

    // Port of asyncthreadpool.py: run() executes sync inline and logs exceptions without raising
    public virtual void Run(Delegate del, params object?[] args)
    {
        try
        {
            var r = BindDelegate(del, args)();
            // A returned Task is not a loop coroutine (no loop exists here),
            // but it must not go unobserved: log faults like _do_async does.
            if (r is Task t) _ = t.ContinueWith(ct => { if (ct.IsFaulted && ct.Exception != null) try { AtherizLogger.LogError(ct.Exception.ToString()); } catch (Exception) { } }, TaskScheduler.Default);
        }
        catch (Exception ex) { try { AtherizLogger.LogError(ex.ToString()); } catch (Exception) { } }
    }
    public virtual void Run(Action action)
    {
        try { action(); } catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); }
    }

    private bool AddInternal(Func<Task> runner, string name)
    {
        // Keep busy lock semantics: check _stopped and enqueue atomically
        lock (_lock)
        {
            if (_stopped)
            {
                long now = DateTime.UtcNow.Ticks;
                if (now - _lastFullLogTicks > TimeSpan.FromSeconds(10).Ticks)
                {
                    _lastFullLogTicks = now;
                    Console.Error.WriteLine("[AsyncThreadPool] task submitted after stop; discarded");
                }
                return false;
            }
            lock (_queueLock)
            {
                if (_queueLimit != 0 && _queue.Count >= _queueLimit)
                {
                    long now = DateTime.UtcNow.Ticks;
                    if (now - _lastFullLogTicks > TimeSpan.FromSeconds(10).Ticks)
                    {
                        _lastFullLogTicks = now;
                        Console.Error.WriteLine($"[AsyncThreadPool] task queue full ({_queueLimit}); dropping task");
                    }
                    return false;
                }
                _queue.Enqueue(new WorkItem(runner, name));
                Monitor.Pulse(_queueLock);
            }
        }
        MaybeSpawnReliefWorker();
        return true;
    }

    // Port of asyncthreadpool.py delay stale-pool guard: a reload that
    // swapped the global pool drops this pool's delayed tasks (a cleared
    // global — standalone/test pools — still fires).
    private bool IsStalePool()
    {
        try
        {
            var cur = Globals.GlobalServices.TryGetPool();
            return cur != null && !ReferenceEquals(cur, this);
        }
        catch { return false; }
    }

    public void Delay(TimeSpan delay, Action action)
    {
        if (action is null) return;
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            lock (_lock) if (_stopped) return;
            if (IsStalePool()) return;
            // A full queue at fire time must not silently drop the delayed
            // callback: run it inline on the timer thread instead.
            if (!AddTask(action)) Run(action);
        }, TaskScheduler.Default);
    }
    public void Delay(TimeSpan delay, Func<Task> asyncFunc)
    {
        if (asyncFunc is null) return;
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            lock (_lock) if (_stopped) return;
            if (IsStalePool()) return;
            if (!AddTask(asyncFunc)) Run(asyncFunc);
        }, TaskScheduler.Default);
    }
    public void Delay(double seconds, Action action) => Delay(TimeSpan.FromSeconds(seconds), action);
    public void Delay(double seconds, Func<Task> asyncFunc) => Delay(TimeSpan.FromSeconds(seconds), asyncFunc);

    public void Stop(bool wait = true, TimeSpan? timeout = null)
    {
        var to = timeout ?? TimeSpan.FromSeconds(10);
        lock (_lock)
        {
            if (_stopped) return;
            _stopped = true;
            _stopEvent.Set();
        }
        AtherizLogger.LogInformation("at AsyncThreadPool.stop() ..."); // info upstream (asyncthreadpool.py:307), not an error

        // Drain preserving non-null tasks, similar to Python logic, while holding both locks
        List<WorkItem?> preserved = new();
        lock (_lock)
        {
            lock (_queueLock)
            {
                while (_queue.Count > 0)
                {
                    var it = _queue.Dequeue();
                    if (it != null) preserved.Add(it);
                }
                int needed = preserved.Count + Math.Max(1, _fixedThreads.Count);
                if (_queueLimit != 0 && needed > _queueLimit)
                {
                    _queueLimit = needed;
                }
                foreach (var p in preserved)
                {
                    _queue.Enqueue(p);
                }
                for (int i = 0; i < Math.Max(1, _fixedThreads.Count); i++)
                {
                    _queue.Enqueue(null);
                }
                Monitor.PulseAll(_queueLock);
            }
        }

        if (wait)
        {
            // One deadline shared by all fixed workers: sequential Join(to)
            // calls would stall shutdown N×to in the worst case.
            var deadline = DateTime.UtcNow + to;
            foreach (var t in _fixedThreads)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) remaining = TimeSpan.FromMilliseconds(50);
                if (!t.Join(remaining))
                    Console.Error.WriteLine($"Thread {t.Name} did not stop within {to.TotalSeconds}s");
            }
            List<Thread> reliefSnap;
            lock (_lock) reliefSnap = new List<Thread>(_reliefThreads);
            foreach (var t in reliefSnap) t.Join(TimeSpan.FromSeconds(1));
            lock (_lock) _reliefThreads.RemoveAll(t => !t.IsAlive);

            if (_watchdogThread != null && _watchdogThread.IsAlive)
            {
                _watchdogThread.Join(TimeSpan.FromSeconds(1));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop(wait: true);
        _stopEvent.Dispose();
        _disposed = true;
    }
}
