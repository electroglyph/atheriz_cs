// Single-release accounting for a failed send-attach: exactly one decrement
// per reservation, and over-release is ignored with counters preserved.
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

[Collection("Ported")]
public sealed class PendingLimiterReleaseTests
{
    [Fact]
    public void PendingLimiter_TrackedRelease_ReleasesExactlyOnce()
    {
        // The attach-failure release must release exactly once — never
        // double-subtract the pool below zero.
        var lim = new PendingLimiter(1000);
        var t = Task.CompletedTask;
        Assert.True(lim.TryReserve(t, 10));
        lim.ReleaseAttachFailure(t, 10);
        Assert.Equal((0, 0, 0), lim.Snapshot());
        lim.Release(t);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void PendingLimiter_UntrackedRelease_ReleasesExactlyOnce()
    {
        var lim = new PendingLimiter(1000);
        Assert.True(lim.TryReserve(10));
        lim.ReleaseAttachFailure(null, 10);
        Assert.Equal((0, 0, 0), lim.Snapshot());
        lim.ReleaseSync(10);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void PendingLimiter_OverRelease_IsIgnored()
    {
        var lim = new PendingLimiter(1000);
        lim.ReleaseAttachFailure(null, 5);
        Assert.Equal((0, 0, 0), lim.Snapshot());
        lim.Release(Task.CompletedTask);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void PendingLimiter_TrackZeroWithoutReservation_TracksNothing()
    {
        // A zero Track with no prior reservation must not plant an entry:
        // the entry would carry no count slot, and Release(task) would warn
        // spuriously. (An async zero from TryReserve(task, 0) DOES hold a
        // slot, so Release(task) keeps no zero-guard — the guard lives in
        // Track, where "reserved nothing" is known.)
        var lim = new PendingLimiter(1000);
        var t = Task.CompletedTask;
        lim.Track(t, 0);
        Assert.Empty(lim.SnapshotTasks());
        lim.Release(t);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }

    [Fact]
    public void PendingLimiter_AsyncZeroReserve_HoldsSlotUntilRelease()
    {
        // Deliberate asymmetry: unlike sync TryReserve(0), the async zero
        // reserve takes a count slot (Release(task) decrements), so it must
        // keep its tracking entry — pinned here so no blanket zero-guard in
        // Release(task) can silently leak the slot.
        var lim = new PendingLimiter(1000);
        var t = Task.CompletedTask;
        Assert.True(lim.TryReserve(t, 0));
        Assert.Equal((0, 1, 1), lim.Snapshot());
        lim.Release(t);
        Assert.Equal((0, 0, 0), lim.Snapshot());
    }
}
