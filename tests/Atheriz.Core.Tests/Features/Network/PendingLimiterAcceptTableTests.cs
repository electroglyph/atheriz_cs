using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// PendingLimiter accept table: MarkClosing subsumes the duplicate store,
// the sync-only zero-guard asymmetry is preserved, and release paths stay
// exact (including the missing-task no-op).
[Collection("Ported")]
public sealed class PendingLimiterAcceptTableTests
{
    [Fact]
    public void SyncZero_ReservesNothing_EvenWhileClosing()
    {
        var lim = new PendingLimiter(maxBytes: 16, maxCount: 2);
        Assert.True(lim.TryReserve(0));
        Assert.Equal((0, 0, 0), lim.Snapshot());
        lim.MarkClosing();
        Assert.True(lim.IsClosing);
        // Zero-guard sits outside the closing check: still true, still empty.
        Assert.True(lim.TryReserve(0));
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void AsyncZero_TakesSlot_ReleasedByTask()
    {
        var lim = new PendingLimiter(maxBytes: 16, maxCount: 2);
        var task = Task.CompletedTask;
        Assert.True(lim.TryReserve(task, 0));
        Assert.Equal((0, 1, 1), lim.Snapshot());
        lim.Release(task);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void AsyncZero_WhileClosing_Refused()
    {
        var lim = new PendingLimiter(maxBytes: 16, maxCount: 2);
        lim.MarkClosing();
        Assert.False(lim.TryReserve(Task.CompletedTask, 0));
    }

    [Fact]
    public void FullLimiter_Refuses_BothPaths()
    {
        var lim = new PendingLimiter(maxBytes: 10, maxCount: 1);
        Assert.True(lim.TryReserve(10));
        Assert.False(lim.TryReserve(1));
        Assert.False(lim.TryReserve(Task.CompletedTask, 1));
        lim.ReleaseSync(10);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void Closing_Refuses_NonzeroReserves()
    {
        var lim = new PendingLimiter(maxBytes: 1024);
        lim.MarkClosing();
        Assert.False(lim.TryReserve(1));
        Assert.False(lim.TryMarkClosing());
    }

    [Fact]
    public void Release_MissingTask_IsNoOp()
    {
        var lim = new PendingLimiter(maxBytes: 1024);
        Assert.True(lim.TryReserve(4));
        var before = lim.Snapshot();
        lim.Release(Task.CompletedTask);
        Assert.Equal(before, lim.Snapshot());
        lim.ReleaseSync(4);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void Release_Task_RestoresExactDebt()
    {
        var lim = new PendingLimiter(maxBytes: 1024);
        var task = Task.CompletedTask;
        Assert.True(lim.TryReserve(task, 100));
        Assert.Equal((100, 1, 1), lim.Snapshot());
        lim.Release(task);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }
}
