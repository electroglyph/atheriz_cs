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
    // Async-lease ownership. The claim is written to AsyncLocal BEFORE the
    // first await, so it flows to the caller's continuation and any nested
    // EnterAsync on the same logical flow (any pool thread). The global
    // holder id pairs it with the live lease: a stale per-flow token from a
    // released lease never equals the current holder, so sequential leases in
    // one flow take the normal path. A token match on the acquire thread is
    // same-flow nesting (no semaphore take); a match on any other thread is
    // a fork — refused fail-fast, like the sync fork path.
    private static long _claimSource;
    private static readonly AsyncLocal<long> _asyncClaim = new();
    private static long _asyncHolder;
    private static int _asyncHolderThread;

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

    /// <summary>
    /// Blocking take. Never await while holding a sync <see cref="Enter"/>:
    /// the continuation may resume on another pool thread, where
    /// <see cref="Exit"/> throws fail-fast instead of releasing and the
    /// permit leaks — async flows must use <see cref="EnterAsync"/> and
    /// dispose the returned lease. Re-entrant on the owning flow; forked
    /// flows inherit the count but never ownership (see
    /// <see cref="TryEnter"/>).
    /// </summary>
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

    // Genuinely async take — awaiting the semaphore frees the pool thread
    // instead of blocking it, so saturated saves no longer pin drain
    // workers. Recorded as a lease (not AsyncLocal): an AsyncLocal write
    // made after an await lands in a forked ExecutionContext and never
    // propagates back to the caller's flow, so the matching exit would see
    // count 0 and leak the permit.
    // Deliberately NOT an async method: the ownership claim below must be
    // published on the CALLER's ExecutionContext, and async builders scope
    // AsyncLocal writes (a pre-await write inside an async body never
    // reaches the caller). A plain method body runs on the caller's context
    // directly, so the claim sticks for nested takes. Only the true-wait
    // path goes async (EnterAsyncCore).
    public static Task<WriteHold> EnterAsync(CancellationToken ct = default)
    {
        // Async-owner fast path: a nested EnterAsync on the flow already
        // holding the async lease must not take the semaphore (the outer
        // lease cannot release while awaiting it — deadlock). Same-thread
        // match is same-flow nesting; any other thread carrying the live
        // token is a fork and is refused instead of running DB work beside
        // the holder. (A nested take after an await that resumes on another
        // pool thread also refuses — loud failure beats silent deadlock;
        // nested takes in this codebase are synchronous, so the pattern
        // never triggers.) A stale per-flow token from a released lease
        // never equals the live holder, so sequential leases take the
        // normal path.
        long flowClaim = _asyncClaim.Value;
        long liveHolder = Volatile.Read(ref _asyncHolder);
        if (flowClaim != 0 && flowClaim == liveHolder)
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref _asyncHolderThread))
                return Task.FromResult(WriteHold.Nested());
            throw new InvalidOperationException("DbWriteGate.EnterAsync refused on a forked flow while another flow holds the gate.");
        }
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
            return Task.FromResult(WriteHold.Owned(0));
        }
        if (_recursion.Value > 0) return Task.FromResult(WriteHold.Nested());
        long claim = Interlocked.Increment(ref _claimSource);
        _asyncClaim.Value = claim;
        return EnterAsyncCore(claim, ct);
    }

    private static async Task<WriteHold> EnterAsyncCore(long claim, CancellationToken ct)
    {
        await _sem.WaitAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _asyncHolder, claim);
        Volatile.Write(ref _asyncHolderThread, Environment.CurrentManagedThreadId);
        return WriteHold.Owned(claim);
    }

    /// <summary>
    /// Async-take lease from <see cref="EnterAsync"/>. Dispose releases the
    /// semaphore hold (no-op for nested holds). Thread-agnostic: the owning
    /// flow may exit on another pool thread after awaits.
    /// </summary>
    public readonly struct WriteHold : IDisposable
    {
        private readonly bool _owns;
        private readonly long _claim;
        internal WriteHold(bool owns) => (_owns, _claim) = (owns, 0);
        internal WriteHold(bool owns, long claim) => (_owns, _claim) = (owns, claim);
        internal static WriteHold Nested() => new(false);
        internal static WriteHold Owned(long claim) => new(true, claim);
        public void Dispose()
        {
            if (!_owns) return;
            // Clear the async-owner fast-path token only if this lease is
            // still the live holder, then release the permit.
            if (_claim != 0 && Volatile.Read(ref _asyncHolder) == _claim)
                Volatile.Write(ref _asyncHolder, 0);
            if (_claim == 0)
            {
                // Stale-fork adopt (EnterAsync) recorded count 1 on this flow
                // to make nested takes re-entrant; the lease owns that mark,
                // so disposing the lease undoes exactly one level. Otherwise
                // the flow keeps a phantom hold and later Enter() calls pass
                // through beside the real holder.
                if (_recursion.Value > 0) _recursion.Value--;
            }
            _sem.Release();
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
