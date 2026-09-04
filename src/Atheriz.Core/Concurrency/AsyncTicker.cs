namespace Atheriz.Core.Concurrency;

/// <summary>
/// Port of <c>atheriz/globals/asyncthreadpool.py:457</c> AsyncTicker + TimeSlot.
/// Each interval has a slot that fires all coros every interval seconds.
/// Pending dedup prevents overlapping ticks.
/// </summary>
public sealed class AsyncTicker
{
    private readonly object _lock = new();
    private readonly Dictionary<double, TimeSlot> _slots = new();
    private readonly AsyncThreadPool _pool;

    public AsyncTicker(AsyncThreadPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>For tests that don't care about pool, create own internal pool.</summary>
    public AsyncTicker() : this(new AsyncThreadPool()) { }

    public IReadOnlyDictionary<double, TimeSlot> Slots
    {
        get { lock (_lock) return new Dictionary<double, TimeSlot>(_slots); }
    }

    // For tests: direct accessor like Python dict
    public TimeSlot? GetSlot(double interval)
    {
        lock (_lock) { _slots.TryGetValue(interval, out var s); return s; }
    }
    public bool TryGetSlot(double interval, out TimeSlot? slot)
    {
        lock (_lock) return _slots.TryGetValue(interval, out slot);
    }

    public void AddCoro(Func<Task> coro, double interval) => AddCoro(coro, TimeSpan.FromSeconds(interval));
    public void AddCoro(Action coro, double interval) => AddCoro(coro, TimeSpan.FromSeconds(interval));

    public void AddCoro(Func<Task> coro, TimeSpan interval)
    {
        double key = interval.TotalSeconds;
        lock (_lock)
        {
            if (!_slots.TryGetValue(key, out var slot))
            {
                slot = new TimeSlot(interval, _pool);
                _slots[key] = slot;
            }
            slot.AddCoro(coro);
            slot.Start();
        }
    }

    public void AddCoro(Action coro, TimeSpan interval)
    {
        double key = interval.TotalSeconds;
        lock (_lock)
        {
            if (!_slots.TryGetValue(key, out var slot))
            {
                slot = new TimeSlot(interval, _pool);
                _slots[key] = slot;
            }
            slot.AddCoro(coro);
            slot.Start();
        }
    }

    public void RemoveCoro(Func<Task> coro, double interval) => RemoveCoro(coro, TimeSpan.FromSeconds(interval));
    public void RemoveCoro(Action coro, double interval) => RemoveCoro(coro, TimeSpan.FromSeconds(interval));

    public void RemoveCoro(Func<Task> coro, TimeSpan interval)
    {
        double key = interval.TotalSeconds;
        lock (_lock)
        {
            if (_slots.TryGetValue(key, out var slot))
                slot.RemoveCoro(coro);
        }
    }

    public void RemoveCoro(Action coro, TimeSpan interval)
    {
        double key = interval.TotalSeconds;
        lock (_lock)
        {
            if (_slots.TryGetValue(key, out var slot))
                slot.RemoveCoro(coro);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Stop();
            _slots.Clear();
        }
    }

    public void Stop()
    {
        Console.Error.WriteLine("at AsyncTicker.stop() ...");
        List<TimeSlot> copy;
        lock (_lock) copy = _slots.Values.ToList();
        foreach (var s in copy)
        {
            try { s.Stop(); } catch (Exception ex) { Console.Error.WriteLine($"Error stopping ticker slot {s.Interval}:\n{ex}"); }
        }
    }

    public class TimeSlot
    {
        private readonly object _lock = new();
        private readonly TimeSpan _interval;
        private readonly AsyncThreadPool _pool;
        private readonly HashSet<Delegate> _coros = new();
        private readonly HashSet<Delegate> _pending = new();
        private bool _running;
        private Task? _future;
        private CancellationTokenSource? _cts;

        // Python parity: slot.coros set and slot.running
        public IReadOnlySet<Delegate> Coros { get { lock (_lock) return new HashSet<Delegate>(_coros); } }
        public HashSet<Delegate> CorosSnapshot { get { lock (_lock) return new HashSet<Delegate>(_coros); } }
        public bool running { get { lock (_lock) return _running; } }

        public TimeSlot(TimeSpan interval, AsyncThreadPool pool)
        {
            _interval = interval;
            _pool = pool;
        }

        public TimeSpan Interval => _interval;

        public void AddCoro(Func<Task> coro) { lock (_lock) _coros.Add(coro); }
        public void AddCoro(Action coro) { lock (_lock) _coros.Add(coro); }
        public void AddCoro(Delegate coro) { lock (_lock) _coros.Add(coro); }
        public bool ContainsCoro(Delegate coro) { lock (_lock) return _coros.Contains(coro); }

        public void RemoveCoro(Func<Task> coro)
        {
            lock (_lock)
            {
                _coros.Remove(coro);
                _pending.Remove(coro);
                if (_coros.Count == 0) StopInternal();
            }
        }

        public void RemoveCoro(Action coro)
        {
            lock (_lock)
            {
                _coros.Remove(coro);
                _pending.Remove(coro);
                if (_coros.Count == 0) StopInternal();
            }
        }
        public void RemoveCoro(Delegate coro)
        {
            lock (_lock)
            {
                _coros.Remove(coro);
                _pending.Remove(coro);
                if (_coros.Count == 0) StopInternal();
            }
        }

        public virtual void Stop()
        {
            lock (_lock) StopInternal();
        }

        private void StopInternal()
        {
            _running = false;
            var cts = _cts;
            _cts = null;
            _future = null;
            cts?.Cancel();
        }

        private void Release(Delegate coro) { lock (_lock) _pending.Remove(coro); }

        private void TickOnce(Delegate coro)
        {
            try
            {
                if (coro is Func<Task> asyncFunc)
                {
                    var task = asyncFunc();
                    if (!task.IsCompleted)
                    {
                        _ = task.ContinueWith(t =>
                        {
                            Release(coro);
                            if (t.IsFaulted) Console.Error.WriteLine(t.Exception?.ToString());
                        }, TaskScheduler.Default);
                    }
                    else
                    {
                        Release(coro);
                        if (task.IsFaulted) Console.Error.WriteLine(task.Exception?.ToString());
                    }
                }
                else if (coro is Action action)
                {
                    try { action(); } finally { Release(coro); }
                }
                else
                {
                    Release(coro);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Release(coro);
            }
        }

        private async Task TimerAsync(CancellationToken ct)
        {
            var nextTick = DateTime.UtcNow + _interval;
            try
            {
                while (true)
                {
                    lock (_lock) if (!_running) break;
                    var delay = nextTick - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
                    }
                    else if (delay < -_interval)
                    {
                        nextTick = DateTime.UtcNow;
                    }
                    List<Delegate> batch;
                    lock (_lock)
                    {
                        if (!_running) break;
                        batch = _coros.Where(c => !_pending.Contains(c)).ToList();
                        foreach (var c in batch) _pending.Add(c);
                    }
                    foreach (var c in batch)
                    {
                        lock (_lock)
                        {
                            if (!_coros.Contains(c))
                            {
                                _pending.Remove(c);
                                continue;
                            }
                        }
                        string name = c.Method.Name;
                        if (!_pool.AddTask(() => TickOnce(c), name))
                            Release(c);
                    }
                    nextTick += _interval;
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_running) return;
                _running = true;
                _cts = new CancellationTokenSource();
                try { _future = Task.Run(() => TimerAsync(_cts.Token), _cts.Token); }
                catch { _running = false; _cts = null; throw; }
            }
        }

        // For tests / introspection
        public int CoroCount { get { lock (_lock) return _coros.Count; } }
        public int PendingCount { get { lock (_lock) return _pending.Count; } }
        public bool Running { get { lock (_lock) return _running; } }
    }
}
