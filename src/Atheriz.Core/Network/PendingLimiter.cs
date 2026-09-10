// Port of atheriz/network/telnet.py:121-324 + atheriz/network/websocket.py:29-114 pending limiter
using System.Diagnostics;

namespace Atheriz.Core.Network;

/// <summary>
/// Shared pending-bytes/count limiter mirroring <c>telnet.py:TelnetConnection._pending_bytes</c>
/// and <c>websocket.py:WebSocketConnection._pending_*</c>.
/// Faithful to Python: single lock, closing flag, maxBytes (+ optional maxCount), per-Task accounting
/// for async sends and sync ReleaseSync in finally (fixes telnet leak).
/// </summary>
public sealed class PendingLimiter
{
    private readonly int _maxBytes;
    private readonly int? _maxCount;
    private int _pendingBytes;
    private int _pendingCount;
    private readonly Dictionary<Task, int> _byTask = new();
    private readonly Lock _lock = new();
    private bool _closing;

    public PendingLimiter(int maxBytes, int? maxCount = null)
    {
        _maxBytes = maxBytes;
        _maxCount = maxCount;
    }

    public bool IsClosing
    {
        get { lock (_lock) return _closing; }
    }

    public void MarkClosing()
    {
        _ = TryMarkClosing();
    }

    public bool TryMarkClosing()
    {
        lock (_lock)
        {
            if (_closing) return false;
            _closing = true;
            return true;
        }
    }

    private bool CanReserveLocked(int nb)
    {
        if (_closing) return false;
        if (_pendingBytes + nb > _maxBytes) return false;
        if (_maxCount.HasValue && _pendingCount >= _maxCount.Value) return false;
        return true;
    }

    // Shared reserve core (call with _lock held). The sync-only zero-guard
    // stays OUTSIDE in TryReserve: an async zero reserve must take a slot
    // since Release(Task) decrements.
    private bool TryReserveCoreLocked(Task? task, int nb)
    {
        if (!CanReserveLocked(nb)) return false;
        _pendingBytes += nb;
        _pendingCount++;
        if (task is not null) _byTask[task] = nb;
        return true;
    }

    /// <summary>
    /// Reserve <paramref name="nb"/> bytes (and one count) without Task tracking.
    /// Mirrors sync reserve in <c>telnet.py:205,232</c> / <c>websocket.py:90</c> before sync write.
    /// Returns false if closing or would exceed maxBytes/maxCount (caller should Close).
    /// </summary>
    public bool TryReserve(int nb)
    {
        // Zero reserves nothing: taking a count slot here would leak, since
        // the release side is (correctly) a no-op for zero.
        if (nb == 0) return true;
        lock (_lock) return TryReserveCoreLocked(null, nb);
    }

    /// <summary>
    /// Reserve and associate with <paramref name="task"/> (async path).
    /// Mirrors <c>websocket.py:104-105</c> increment + _pendingTasks.add / _pendingBytesByTask[task]=nb.
    /// </summary>
    public bool TryReserve(Task task, int nb)
    {
        lock (_lock) return TryReserveCoreLocked(task, nb);
    }

    /// <summary>
    /// Called from task_done callback — mirrors <c>websocket.py:55-60</c>.
    /// </summary>
    public void Release(Task task)
    {
        lock (_lock)
        {
            if (_byTask.Remove(task, out var nb))
            {
                _pendingBytes = Math.Max(0, _pendingBytes - nb);
                _pendingCount = Math.Max(0, _pendingCount - 1);
            }
        }
    }

    /// <summary>
    /// Sync release for telnet success path — mirrors Python <c>with pending_lock: pending-=nb</c> in finally.
    /// Fixes telnet leak where success never decremented.
    /// Over-release clamps to zero instead of throwing: duplicate completions
    /// must neither corrupt debt nor crash send paths — but the imbalance is
    /// still surfaced via a warning.
    /// </summary>
    public void ReleaseSync(int nb)
    {
        lock (_lock)
        {
            if (nb == 0) return;
            if (_pendingCount <= 0 || _pendingBytes < nb)
            {
                AtherizLogger.LogWarning($"PendingLimiter.ReleaseSync({nb}) over-release (bytes={_pendingBytes}, count={_pendingCount}); clamping to zero.", "PendingLimiter");
                _pendingBytes = 0;
                _pendingCount = 0;
                return;
            }
            _pendingBytes -= nb;
            _pendingCount--;
        }
    }

    /// <summary>
    /// Associate already-reserved bytes with task (when reserve happened before task creation).
    /// exact accounting — when the task already carries a reservation
    /// (TryReserve(task, old)), only the delta is applied, so re-associating
    /// with a corrected byte count neither double-counts nor loses bytes.
    /// </summary>
    public void Track(Task task, int nb)
    {
        lock (_lock)
        {
            if (_byTask.TryGetValue(task, out var old))
                _pendingBytes += nb - old;
            _byTask[task] = nb;
        }
    }

    public int PendingBytes
    {
        get { lock (_lock) return _pendingBytes; }
    }

    public int PendingCount
    {
        get { lock (_lock) return _pendingCount; }
    }

    public List<Task> SnapshotTasks()
    {
        lock (_lock) return _byTask.Keys.ToList();
    }

    // For backwards compatibility / testing — expose internal state snapshot.
    public (int bytes, int count, int tracked) Snapshot()
    {
        lock (_lock) return (_pendingBytes, _pendingCount, _byTask.Count);
    }
}
