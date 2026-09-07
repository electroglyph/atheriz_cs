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
            Assert.Equal(0, DbWriteGate.SemaphoreForTesting.CurrentCount);
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
            Assert.Equal(0, DbWriteGate.SemaphoreForTesting.CurrentCount);
        }
        finally
        {
            // Reclaim the leaked permit when the bug is present, then release the
            // matched hold, so later tests see a balanced gate either way.
            if (DbWriteGate.SemaphoreForTesting.CurrentCount > 0)
                DbWriteGate.SemaphoreForTesting.Wait(TimeSpan.FromSeconds(30));
            DbWriteGate.Exit();
        }
    }
}
