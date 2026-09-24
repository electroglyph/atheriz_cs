namespace Atheriz.Core.Globals;

/// <summary>
/// Bounded dict with lazy FIFO eviction at 4000 entries (retired queue
/// slots are consumed positionally, so Remove stays O(1)).
/// </summary>
public sealed class BoundedDictionary<TKey, TValue> where TKey : notnull
{
    private const int Limit = 4000;
    private readonly Dictionary<TKey, TValue> _dict = new();
    private readonly Queue<TKey> _order = new();
    // Retired queue-entry counts for lazy FIFO deletes: each successful
    // Remove retires exactly one queued entry, consumed positionally at
    // eviction/compaction time. Keeps Remove O(1) instead of rebuilding
    // the whole queue per remove. Guarded by _lock like everything else.
    private readonly Dictionary<TKey, int> _dead = new();
    private readonly Lock _lock = new();
    private void EvictIfNeeded()
    {
        if (_order.Count > Limit * 2) CompactLocked();
        if (_dict.Count <= Limit) return;
        // Only newly-enqueued keys call this, and they land at the tail,
        // so the head can never be the just-set key — dequeue (skipping
        // retired heads) until one live head is evicted.
        while (_dict.Count > Limit && _order.Count > 0)
        {
            var head = _order.Dequeue();
            if (_dead.TryGetValue(head, out var n) && n > 0)
            {
                if (n == 1) _dead.Remove(head);
                else _dead[head] = n - 1;
                continue;
            }
            _dict.Remove(head);
        }
    }
    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            var isNew = !_dict.ContainsKey(key);
            _dict[key] = value;
            if (!isNew) return;
            _order.Enqueue(key);
            EvictIfNeeded();
        }
    }
    /// <summary>Atomic read-modify-write under the dict lock (F005 login hot path).</summary>
    public TValue AddOrUpdate(TKey key, Func<bool, TValue, TValue> updater)
    {
        lock (_lock)
        {
            var isNew = !_dict.ContainsKey(key);
            var nv = updater(!isNew, isNew ? default! : _dict[key]);
            _dict[key] = nv;
            if (isNew)
            {
                _order.Enqueue(key);
                EvictIfNeeded();
            }
            return nv;
        }
    }
    /// <summary>
    /// Atomic check-then-set under the dict lock: sets <paramref name="value"/> only when
    /// <paramref name="allow"/> (exists, current-or-default) returns true.
    /// </summary>
    public bool CheckAndSet(TKey key, TValue value, Func<bool, TValue, bool> allow)
    {
        lock (_lock)
        {
            var exists = _dict.ContainsKey(key);
            if (!allow(exists, exists ? _dict[key] : default!)) return false;
            _dict[key] = value;
            if (!exists)
            {
                _order.Enqueue(key);
                EvictIfNeeded();
            }
            return true;
        }
    }
    public TValue? Get(TKey key)
    {
        lock (_lock) return _dict.TryGetValue(key, out var v) ? v : default;
    }
    public bool Contains(TKey key) { lock (_lock) return _dict.ContainsKey(key); }
    public bool TryGetValue(TKey key, out TValue? value) { lock (_lock) return _dict.TryGetValue(key, out value); }
    public void Remove(TKey key)
    {
        lock (_lock)
        {
            // Retire exactly one queued entry, and only when a live entry
            // actually died: an absent key has no queue entry to retire.
            if (_dict.Remove(key)) NoteDeadLocked(key);
        }
    }
    /// <summary>
    /// Atomic remove-if-value-matches under the dict lock: expiry cleanup
    /// must not delete a concurrently refreshed entry.
    /// </summary>
    public bool RemoveIfEqual(TKey key, TValue expected)
    {
        lock (_lock)
        {
            if (!_dict.TryGetValue(key, out var cur)) return false;
            if (!EqualityComparer<TValue>.Default.Equals(cur, expected)) return false;
            _dict.Remove(key);
            NoteDeadLocked(key);
            return true;
        }
    }
    // Caller holds _lock and just removed a live entry: retire its queue
    // slot lazily (consumed positionally by EvictIfNeeded/CompactLocked).
    private void NoteDeadLocked(TKey key)
    {
        _dead[key] = _dead.TryGetValue(key, out var n) ? n + 1 : 1;
    }
    // Amortized rebuild for churn without eviction pressure: retired heads
    // are otherwise only consumed by EvictIfNeeded, so a remove-heavy
    // workload under the limit would grow the queue without bound.
    private void CompactLocked()
    {
        if (_dead.Count == 0) return;
        var kept = new Queue<TKey>(_order.Count);
        foreach (var k in _order)
        {
            if (_dead.TryGetValue(k, out var n) && n > 0)
            {
                if (n == 1) _dead.Remove(k);
                else _dead[k] = n - 1;
            }
            else kept.Enqueue(k);
        }
        _order.Clear();
        foreach (var k in kept) _order.Enqueue(k);
    }
    public void Clear() { lock (_lock) { _dict.Clear(); _order.Clear(); _dead.Clear(); } }
    public Dictionary<TKey, TValue> Snapshot() { lock (_lock) return new Dictionary<TKey, TValue>(_dict); }
    public int Count { get { lock (_lock) return _dict.Count; } }
}