using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Features.Concurrency;

// The write gate is re-entrant on one flow and exclusive across flows.
[Collection("Ported")]
public class WriteGateTests
{
    [Fact]
    public void SameFlow_NestedEnterExit_IsReentrant()
    {
        // Nested Enter on the same flow (DbWriteGate.cs:21-29) must not block,
        // and hold state must unwind one level per Exit.
        DbWriteGate.Enter();
        try
        {
            Assert.True(DbWriteGate.IsHeld);
            DbWriteGate.Enter();
            try
            {
                Assert.True(DbWriteGate.IsHeld);
            }
            finally
            {
                DbWriteGate.Exit();
            }
            Assert.True(DbWriteGate.IsHeld);
        }
        finally
        {
            DbWriteGate.Exit();
        }
        Assert.False(DbWriteGate.IsHeld);
    }

    [Fact]
    public void HeldGate_AdmitsNoSecondPermit()
    {
        // While one flow holds the gate (DbWriteGate.cs:27), no second permit is
        // available: a concurrent second writer is not admitted.
        DbWriteGate.Enter();
        try
        {
            Assert.Equal(0, DbWriteGate.Semaphore.CurrentCount);
        }
        finally
        {
            DbWriteGate.Exit();
        }
    }

    [Fact]
    public void ChildFlow_BalancedUse_LeavesParentHoldIntact()
    {
        // Hold state is per-flow (DbWriteGate.cs:14,18): ExecutionContext
        // flows initial values to a new thread by runtime design, but the
        // child's balanced Enter/Exit must not disturb the parent's hold.
        DbWriteGate.Enter();
        try
        {
            bool? childBalanced = null;
            var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    // Balanced Enter/Exit must restore the flow's own count,
                    // whatever it inherited.
                    bool before = DbWriteGate.IsHeld;
                    DbWriteGate.Enter();
                    DbWriteGate.Exit();
                    childBalanced = DbWriteGate.IsHeld == before;
                }
                finally
                {
                    done.Set();
                }
            })
            { IsBackground = true };
            thread.Start();
            Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "probe thread did not finish; possible deadlock");
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            Assert.True(childBalanced == true);
            Assert.True(DbWriteGate.IsHeld, "child flow disturbed the parent hold");
        }
        finally
        {
            DbWriteGate.Exit();
        }
    }

    [Fact]
    public void UnmatchedExit_DoesNotFreeAnotherFlowsSlot()
    {
        // An Exit with no matching Enter on its own flow (DbWriteGate.cs:42-54)
        // must not hand the holding flow's slot to a waiter.
        DbWriteGate.Enter();
        try
        {
            var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    try
                    {
                        DbWriteGate.Exit();
                    }
                    catch
                    {
                        // Either throwing or ignoring is acceptable; freeing the
                        // other flow's slot is not.
                    }
                }
                finally
                {
                    done.Set();
                }
            })
            { IsBackground = true };
            thread.Start();
            Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "probe thread did not finish; possible deadlock");
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            Assert.Equal(0, DbWriteGate.Semaphore.CurrentCount);
        }
        finally
        {
            // Reclaim the leaked permit when the bug is present, then release the
            // matched hold, so later tests see a balanced gate either way.
            if (DbWriteGate.Semaphore.CurrentCount > 0)
                DbWriteGate.Semaphore.Wait(TimeSpan.FromSeconds(30));
            DbWriteGate.Exit();
        }
    }

    [Fact]
    public void EnterAsync_ForkedFlowWhileHeld_Refuses()
    {
        // A Task.Run-inherited count is a fork, never ownership: EnterAsync
        // must fail fast instead of handing out a no-op lease that would run
        // DB work concurrently with the holder. Synchronous throughout: an
        // await inside a sync Enter hold could resume on another thread and
        // trip the fork guard in this test's own Exit.
        DbWriteGate.Enter();
        try
        {
            var ex = Record.Exception(() => Task.Run(() => DbWriteGate.EnterAsync()).GetAwaiter().GetResult());
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal(0, DbWriteGate.Semaphore.CurrentCount);
            Assert.True(DbWriteGate.IsHeld);
        }
        finally
        {
            DbWriteGate.Exit();
        }
    }

    [Fact]
    public void EnterAsync_StaleForkCopy_AdoptsFreshTake()
    {
        // The holder released before the fork ran: the copy is stale, so the
        // fork adopts a real take instead of refusing.
        var proceed = new ManualResetEventSlim(false);
        Exception? forkError = null;
        DbWriteGate.Enter();
        var t = Task.Run(() =>
        {
            try
            {
                proceed.Wait(TimeSpan.FromSeconds(30));
                using var hold = DbWriteGate.EnterAsync().GetAwaiter().GetResult();
                if (DbWriteGate.Semaphore.CurrentCount != 0) throw new Xunit.Sdk.XunitException("adopted take did not hold the semaphore");
            }
            catch (Exception ex) { forkError = ex; }
        });
        DbWriteGate.Exit();
        proceed.Set();
        Assert.True(t.Wait(TimeSpan.FromSeconds(30)), "fork task did not finish; possible deadlock");
        Assert.Null(forkError);
        Assert.Equal(1, DbWriteGate.Semaphore.CurrentCount);
        Assert.False(DbWriteGate.IsHeld);
    }

    [Fact]
    public void EnterAsync_OwnerFlow_NestsWithoutTake()
    {
        // Same-flow async re-entry stays a nested no-op lease.
        DbWriteGate.Enter();
        try
        {
            using var hold = DbWriteGate.EnterAsync().GetAwaiter().GetResult();
            Assert.Equal(0, DbWriteGate.Semaphore.CurrentCount);
            Assert.True(DbWriteGate.IsHeld);
        }
        finally
        {
            DbWriteGate.Exit();
        }
        Assert.Equal(1, DbWriteGate.Semaphore.CurrentCount);
    }

    [Fact]
    public void Exit_AfterThreadHop_ThrowsInsteadOfLeaking()
    {
        // The await-under-Enter shape (Enter, resume on another thread, Exit)
        // must fail fast, not leak the permit and hang all later Enters.
        DbWriteGate.Enter();
        try
        {
            var ex = Record.Exception(() => Task.Run(() => DbWriteGate.Exit()).GetAwaiter().GetResult());
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal(0, DbWriteGate.Semaphore.CurrentCount);
            Assert.True(DbWriteGate.IsHeld);
        }
        finally
        {
            DbWriteGate.Exit();
        }
        Assert.Equal(1, DbWriteGate.Semaphore.CurrentCount);
    }

    [Fact]
    public void Exit_WithoutEnter_Throws()
    {
        Assert.False(DbWriteGate.IsHeld);
        Assert.IsType<InvalidOperationException>(Record.Exception(() => DbWriteGate.Exit()));
    }
}
