// Port of atheriz/database_setup.py:Database.lock RLock (re-entrant)
using System.Threading;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Re-entrant gate mirroring Python `RLock`. Same logical flow may re-enter
/// without deadlock (StartStop.DoShutdown → SaveWorld → nested SaveObjects).
/// Re-entrancy is tracked per async-flow via AsyncLocal; exclusion via
/// SemaphoreSlim(1,1). A stray Exit with no matching Enter on its flow is a
/// no-op, and a thread that never took the gate cannot release it (a Release
/// on the 1→0 step happens only on the taker thread), so neither can free
/// another flow's slot.
/// Take is deliberately synchronous (EnterAsync returns a completed task):
/// AsyncLocal writes made after an await inside the taker land in a forked
/// ExecutionContext and never propagate back to the caller's flow, so the
/// matching Exit would see count 0 and leak the permit (a permanent hang
/// under any contention). Recording the hold with no await in between keeps
/// it visible to the caller's flow, including across later await thread-hops
/// (paired with ExitAfterThreadHop at the async call site when a hop happened).
/// </summary>
public static class DbWriteGate
{
    private static readonly SemaphoreSlim _sem = new(1, 1);
    private static readonly AsyncLocal<int> _recursion = new();
    // Taker identity for the outermost hold. ExecutionContext flows AsyncLocal
    // values to child threads, so the recursion count alone cannot tell a
    // same-flow re-entry from an inherited copy on a thread that never called
    // Enter — the holder thread id can.
    private static int _holderThreadId;

    public static SemaphoreSlim Semaphore => _sem;

    public static bool IsHeld => _recursion.Value > 0;

    public static void Enter()
    {
        if (_recursion.Value > 0)
        {
            _recursion.Value++;
            return;
        }
        _sem.Wait();
        _recursion.Value = 1;
        Volatile.Write(ref _holderThreadId, Environment.CurrentManagedThreadId);
    }

    public static Task EnterAsync(CancellationToken ct = default)
    {
        if (_recursion.Value > 0)
        {
            _recursion.Value++;
            return Task.CompletedTask;
        }
        _sem.Wait(ct);
        _recursion.Value = 1;
        Volatile.Write(ref _holderThreadId, Environment.CurrentManagedThreadId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Bounded take: returns false instead of hanging forever when another
    /// flow holds the gate past <paramref name="timeout"/>. Re-entrant on the
    /// owning flow like <see cref="Enter"/>.
    /// </summary>
    public static bool TryEnter(TimeSpan timeout)
    {
        if (_recursion.Value > 0)
        {
            _recursion.Value++;
            return true;
        }
        if (!_sem.Wait(timeout))
            return false;
        _recursion.Value = 1;
        Volatile.Write(ref _holderThreadId, Environment.CurrentManagedThreadId);
        return true;
    }

    public static void Exit()
    {
        var c = _recursion.Value;
        if (c <= 0)
        {
#if DEBUG
            System.Diagnostics.Debug.Fail("DbWriteGate.Exit without a matching Enter on this flow.");
#endif
            _recursion.Value = 0;
            return;
        }
        if (c > 1)
        {
            _recursion.Value = c - 1;
            return;
        }
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref _holderThreadId))
        {
            // Inherited copy on a thread that never took the gate: drop our
            // forked count but never free another flow's slot.
#if DEBUG
            System.Diagnostics.Debug.Fail("DbWriteGate.Exit on a thread that never took the gate (forked AsyncLocal copy).");
#endif
            _recursion.Value = 0;
            return;
        }
        _recursion.Value = 0;
        _sem.Release();
    }

    /// <summary>
    /// Paired exit for <see cref="EnterAsync"/> when awaits between take and
    /// exit may have resumed on a different pool thread (same flow, new
    /// thread). Mirrors <see cref="Exit"/> unwinding but attests ownership via
    /// the caller's take/exit pairing instead of the taker-thread check.
    /// </summary>
    internal static void ExitAfterThreadHop()
    {
        var c = _recursion.Value;
        if (c <= 0)
        {
#if DEBUG
            System.Diagnostics.Debug.Fail("DbWriteGate.ExitAfterThreadHop without a matching EnterAsync on this flow (permit leaked).");
#endif
            _recursion.Value = 0;
            return;
        }
        if (c > 1)
        {
            _recursion.Value = c - 1;
            return;
        }
        _recursion.Value = 0;
        _sem.Release();
    }
}
