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
/// Take is available in two forms. The synchronous Enter records the hold in
/// AsyncLocal with no await in between, keeping it visible to the caller's
/// flow (see Enter). The async EnterAsync instead returns a lease: an
/// AsyncLocal write made after an await lands in a forked ExecutionContext
/// and never propagates back to the caller's flow, so the matching exit
/// would see count 0 and leak the permit.
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

    // Owner check : the AsyncLocal count alone cannot tell a
    // same-flow re-entry from a Task.Run-inherited copy on a thread that
    // never called Enter — the holder thread id can. A count>0 on any other
    // thread is a fork, never ownership. Used by TryEnter and EnterAsync,
    // which refuse live-holder forks; Enter deliberately keeps count-based
    // passthrough (see below).
    private static bool IsOwnerFlow() =>
        _recursion.Value > 0 && Environment.CurrentManagedThreadId == Volatile.Read(ref _holderThreadId);

    public static void Enter()
    {
        // Deliberately count-based, NOT thread-checked 
        // a blocking Enter on a forked flow would deadlock whenever the
        // holder joins the fork (WriteGateTests.ChildFlow pins the
        // must-complete pattern), so passthrough is the only safe choice
        // for the blocking take. Fork *concurrency* under Enter remains the
        // caller's responsibility — same as Python's RLock, which would
        // deadlock this pattern outright. TryEnter (below) refuses forks.
        if (_recursion.Value > 0)
        {
            _recursion.Value++;
            return;
        }
        _sem.Wait();
        _recursion.Value = 1;
        Volatile.Write(ref _holderThreadId, Environment.CurrentManagedThreadId);
    }

    public static async Task<WriteHold> EnterAsync(CancellationToken ct = default)
    {
        // genuinely async take — awaiting the semaphore frees the
        // pool thread instead of blocking it, so saturated saves no longer
        // pin drain workers. Recorded as a lease (not AsyncLocal): an
        // AsyncLocal write made after an await lands in a forked
        // ExecutionContext and never propagates back to the caller's flow,
        // so the matching exit would see count 0 and leak the permit.
        // Re-entrant on a flow already holding via sync Enter (AsyncLocal
        // reads flow down reliably): nested hold, no semaphore take.
        // A forked copy (count inherited, different thread) is never
        // ownership — same rule as TryEnter: refuse while the holder is live,
        // adopt as a fresh take when the copy is stale. Without this the fork
        // would run DB work concurrently with the holder under a no-op lease.
        // Mixed nesting the other way (sync Enter inside an async lease) is
        // unsupported — no such call pattern exists.
        if (_recursion.Value > 0 && !IsOwnerFlow())
        {
            if (!_sem.Wait(TimeSpan.Zero))
                throw new InvalidOperationException("DbWriteGate.EnterAsync refused on a forked flow while another flow holds the gate.");
            _recursion.Value = 1;
            Volatile.Write(ref _holderThreadId, Environment.CurrentManagedThreadId);
            return WriteHold.Owned();
        }
        if (_recursion.Value > 0) return WriteHold.Nested();
        await _sem.WaitAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _holderThreadId, Environment.CurrentManagedThreadId);
        return WriteHold.Owned();
    }

    /// <summary>
    /// Async-take lease from <see cref="EnterAsync"/>. Dispose releases the
    /// semaphore hold (no-op for nested holds). Thread-agnostic: the owning
    /// flow may exit on another pool thread after awaits.
    /// </summary>
    public readonly struct WriteHold : IDisposable
    {
        private readonly bool _owns;
        internal WriteHold(bool owns) => _owns = owns;
        internal static WriteHold Nested() => new(false);
        internal static WriteHold Owned() => new(true);
        public void Dispose()
        {
            if (_owns) _sem.Release();
        }
    }

    /// <summary>
    /// Bounded take: returns false instead of hanging forever when another
    /// flow holds the gate past <paramref name="timeout"/>. Re-entrant on the
    /// owning flow like <see cref="Enter"/>.
    /// </summary>
    public static bool TryEnter(TimeSpan timeout)
    {
        if (IsOwnerFlow())
        {
            _recursion.Value++;
            return true;
        }
        // Fork (or fresh flow): a forked copy is never ownership — fail
        // fast instead of running DB work concurrently with the holder
        // . A stale copy (semaphore free) is adopted as a fresh
        // take; a live holder means refusal, never inheritance.
        if (_recursion.Value > 0)
        {
            if (!_sem.Wait(TimeSpan.Zero)) return false;
            _recursion.Value = 1;
            Volatile.Write(ref _holderThreadId, Environment.CurrentManagedThreadId);
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
            throw new InvalidOperationException("DbWriteGate.Exit without a matching Enter on this flow.");
        }
        if (c > 1)
        {
            _recursion.Value = c - 1;
            return;
        }
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref _holderThreadId))
        {
            // Inherited copy on a thread that never took the gate: this is the
            // await-under-Enter shape (Enter, await, resume elsewhere). Fail
            // fast instead of leaking the permit and hanging all later Enters;
            // async flows must use EnterAsync. Balanced child Enter/Exit pairs
            // never reach here (their count is above 1 when they unwind).
            _recursion.Value = 0;
            throw new InvalidOperationException("DbWriteGate.Exit on a thread that never took the gate: do not await under a sync Enter — use EnterAsync instead.");
        }
        _recursion.Value = 0;
        _sem.Release();
    }
}
