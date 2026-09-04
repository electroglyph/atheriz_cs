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
    private readonly object _lock = new();
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
        lock (_lock) { _closing = true; }
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

    /// <summary>
    /// Reserve <paramref name="nb"/> bytes (and one count) without Task tracking.
    /// Mirrors sync reserve in <c>telnet.py:205,232</c> / <c>websocket.py:90</c> before sync write.
    /// Returns false if closing or would exceed maxBytes/maxCount (caller should Close).
    /// </summary>
    public bool TryReserve(int nb)
    {
        lock (_lock)
        {
            if (!CanReserveLocked(nb)) return false;
            _pendingBytes += nb;
            _pendingCount++;
            return true;
        }
    }

    /// <summary>
    /// Reserve and associate with <paramref name="task"/> (async path).
    /// Mirrors <c>websocket.py:104-105</c> increment + _pendingTasks.add / _pendingBytesByTask[task]=nb.
    /// </summary>
    public bool TryReserve(Task task, int nb)
    {
        lock (_lock)
        {
            if (!CanReserveLocked(nb)) return false;
            _pendingBytes += nb;
            _pendingCount++;
            _byTask[task] = nb;
            return true;
        }
    }

    /// <summary>
    /// Called from task_done callback — mirrors <c>websocket.py:55-60</c>.
    /// </summary>
    public void Release(Task task)
    {
        lock (_lock)
        {
            if (_byTask.TryGetValue(task, out var nb))
            {
                _byTask.Remove(task);
                _pendingBytes = Math.Max(0, _pendingBytes - nb);
                _pendingCount = Math.Max(0, _pendingCount - 1);
            }
        }
    }

    /// <summary>
    /// Sync release for telnet success path — mirrors Python <c>with pending_lock: pending-=nb</c> in finally.
    /// Fixes telnet leak where success never decremented.
    /// </summary>
    public void ReleaseSync(int nb)
    {
        lock (_lock)
        {
            _pendingBytes = Math.Max(0, _pendingBytes - nb);
            _pendingCount = Math.Max(0, _pendingCount - 1);
            // if this was tracked via Task, also remove mapping if any task holds same nb? not needed for sync.
            // For safety, remove any task with matching nb when sync path used (should not be tracked).
        }
    }

    /// <summary>
    /// Associate already-reserved bytes with task (when reserve happened before task creation).
    /// No extra increment; just store mapping for later Release(task).
    /// </summary>
    public void Track(Task task, int nb)
    {
        lock (_lock)
        {
            // pending already incremented via TryReserve(nb); just record.
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
