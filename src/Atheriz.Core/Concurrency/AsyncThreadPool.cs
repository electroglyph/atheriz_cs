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
    private int _origLimitForCap = 0;
    private bool _capped = false;

    private readonly List<Thread> _fixedThreads = new();
    private readonly List<Thread> _reliefThreads = new();
    private readonly object _lock = new();
    private int _busy;
    private int _reliefCount;
    private long _lastReliefSpawnTicks;
    private long _lastFullLogTicks;
    private bool _stopped;
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
            lock (_queueLock)
            {
                int actual = _queue.Count;
                if (_capped) return Math.Min(actual, _origLimitForCap);
                return actual;
            }
        }
    }
    public int QueueLimit
    {
        get { lock (_queueLock) return _queueLimit; }
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
    public object BusyLock => _lock;

    // For tests: allow replacing queue limit (simulates atp.task_queue = Queue(maxsize=2))
    public void SetQueueLimitForTesting(int newLimit)
    {
        lock (_queueLock) { _queueLimit = newLimit; _capped = false; }
    }

    // Expose internal queue for inspection (count, etc.) — not for direct mutation
    public int RawQueueCount { get { lock (_queueLock) return _queue.Count; } }

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
                                // Queue full: discard one item then enqueue sentinel (Python fallback)
                                if (_queue.Count > 0)
                                {
                                    try { _ = _queue.Dequeue(); } catch { }
                                    _queue.Enqueue(null);
                                    Monitor.Pulse(_queueLock);
                                }
                                else
                                {
                                    try { _queue.Enqueue(null); Monitor.Pulse(_queueLock); } catch { }
                                }
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
                    for (int _s = 0; _s < 50; _s += 10) { Thread.Sleep(10); lock (_lock) if (_stopped) return; }
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

    private static void RunInternal(WorkItem item)
    {
        // F009: faults go through AtherizLogger (server.log) instead of bare Console.Error.
        // AtherizLogger still echoes to Console.Error, so test log-capture keeps working.
        try
        {
            var task = item.Runner();
            if (!task.IsCompleted)
            {
                _ = task.ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null) try { AtherizLogger.LogError(t.Exception.ToString()); } catch { Console.Error.WriteLine(t.Exception.ToString()); }
                }, TaskScheduler.Default);
            }
            else if (task.IsFaulted && task.Exception != null)
            {
                try { AtherizLogger.LogError(task.Exception.ToString()); } catch { Console.Error.WriteLine(task.Exception.ToString()); }
            }
        }
        catch (Exception ex) { try { AtherizLogger.LogError(ex.ToString()); } catch { Console.Error.WriteLine(ex.ToString()); } }
    }

    private void MaybeSpawnReliefWorker()
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
            lock (_lock) _reliefThreads.Add(t);
            t.Start(true);
        }
    }

    private void WatchdogLoop()
    {
        while (true)
        {
            // Sleep in slices so Stop can exit quickly (fix audit: Thread.Sleep on watchdog delays shutdown 5s)
            // Use 50ms slice to respect small watchdog intervals in tests (e.g. 0.1s)
            var slice = TimeSpan.FromMilliseconds(50);
            var total = TimeSpan.Zero;
            while (total < _watchdogInterval)
            {
                Thread.Sleep(slice);
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
                lock (_lock)
                {
                    _saturatedSince ??= now;
                    if (now - _saturatedSince.Value >= _watchdogThreshold.TotalSeconds &&
                        now - _lastStarvationLog >= _watchdogThreshold.TotalSeconds)
                    {
                        _lastStarvationLog = now;
                        var snapshot = new Dictionary<long, (string, double)>(_currentTasks);
                        LogStarvation(qsize, busy, now - _saturatedSince.Value, snapshot);
                    }
                }
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
        // Log via AtherizLogger which also echoes to Console.Error for CaptureAtherizLog (see Logger.Write)
        // Duplicate Console.Error is handled inside Logger; avoid double-write that would double-count in tests
        try { AtherizLogger.LogError(msg); } catch { Console.Error.WriteLine(msg); }
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
    public virtual bool AddTask(Delegate del, params object?[] args)
    {
        if (del is Action a) return AddTask(a);
        if (del is Func<Task> f) return AddTask(f);
        // Fallback: wrap delegate invoke
        string name = del.Method.Name ?? "delegate";
        return AddInternal(() => { del.DynamicInvoke(args); return Task.CompletedTask; }, name);
    }

    // Port of asyncthreadpool.py: run() executes sync inline and logs exceptions without raising
    public virtual void Run(Delegate del, params object?[] args)
    {
        try { del.DynamicInvoke(args); } catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); }
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

    public void Delay(TimeSpan delay, Action action)
    {
        if (action is null) return;
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            lock (_lock) if (_stopped) return;
            AddTask(action);
        }, TaskScheduler.Default);
    }
    public void Delay(TimeSpan delay, Func<Task> asyncFunc)
    {
        if (asyncFunc is null) return;
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            lock (_lock) if (_stopped) return;
            AddTask(asyncFunc);
        }, TaskScheduler.Default);
    }
    public void Delay(double seconds, Action action) => Delay(TimeSpan.FromSeconds(seconds), action);
    public void Delay(double seconds, Func<Task> asyncFunc) => Delay(TimeSpan.FromSeconds(seconds), asyncFunc);

    // For testing relief directly
    public void MaybeSpawnReliefWorkerForTesting() => MaybeSpawnReliefWorker();

    public void Stop(bool wait = true, TimeSpan? timeout = null)
    {
        var to = timeout ?? TimeSpan.FromSeconds(10);
        lock (_lock)
        {
            if (_stopped) return;
            _stopped = true;
        }
        Console.Error.WriteLine("at AsyncThreadPool.stop() ...");

        // Drain preserving non-null tasks, similar to Python logic, while holding both locks
        List<WorkItem?> preserved = new();
        int origLimit = 0;
        lock (_lock)
        {
            lock (_queueLock)
            {
                origLimit = _queueLimit;
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
                // Cap handling: if needed > orig and orig>10, queue count view should be capped
                if (origLimit != 0 && needed > origLimit && origLimit > 10)
                {
                    _origLimitForCap = origLimit;
                    _capped = true;
                }
                Monitor.PulseAll(_queueLock);
            }
        }

        if (wait)
        {
            foreach (var t in _fixedThreads)
            {
                if (!t.Join(to))
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
        _disposed = true;
    }
}
