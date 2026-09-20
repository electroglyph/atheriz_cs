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
}
