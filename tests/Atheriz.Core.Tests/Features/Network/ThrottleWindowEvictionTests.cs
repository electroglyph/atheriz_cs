using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// ThrottleWindow eviction accept table on both paths (bounded probe and
// full sweep): expired hosts evict under the >=window predicate while
// unexpired hosts are kept, with lazy-alloc and deferred-remove semantics.
[Collection("Ported")]
public sealed class ThrottleWindowEvictionTests
{
    private static Lock NewLock() => new();

    [Fact]
    public void ProbePath_EvictsExpired_KeepsUnexpired()
    {
        var last = new Dictionary<string, double> { ["old"] = 0.0, ["fresh"] = 99.0 };
        Assert.True(ThrottleWindow.ShouldLog(last, NewLock(), "new", 5.0, 100.0));
        Assert.False(last.ContainsKey("old"));
        Assert.True(last.ContainsKey("fresh"));
        Assert.True(last.ContainsKey("new"));
    }

    [Fact]
    public void ProbePath_WindowBoundary_IsExpired()
    {
        // now - prev == window satisfies >=window: evicted and re-admitted.
        var last = new Dictionary<string, double> { ["edge"] = 95.0 };
        Assert.True(ThrottleWindow.ShouldLog(last, NewLock(), "edge", 5.0, 100.0));
        Assert.Equal(100.0, last["edge"]);
    }

    [Fact]
    public void ProbePath_ThrottlesWithinWindow_WithoutEvicting()
    {
        var last = new Dictionary<string, double> { ["hot"] = 99.0 };
        Assert.False(ThrottleWindow.ShouldLog(last, NewLock(), "hot", 5.0, 100.0));
        Assert.Equal(99.0, last["hot"]);
    }

    [Fact]
    public void ProbePath_EmptyDict_NoEvictionNeeded()
    {
        var last = new Dictionary<string, double>();
        Assert.True(ThrottleWindow.ShouldLog(last, NewLock(), "first", 5.0, 100.0));
        Assert.Single(last);
    }

    [Fact]
    public void SweepPath_EvictsAllExpired_KeepsUnexpired()
    {
        var last = new Dictionary<string, double>();
        for (int i = 0; i < 1030; i++) last[$"stale{i}"] = 0.0;
        for (int i = 0; i < 5; i++) last[$"live{i}"] = 99.0;
        Assert.True(ThrottleWindow.ShouldLog(last, NewLock(), "probe", 5.0, 100.0));
        for (int i = 0; i < 1030; i++) Assert.False(last.ContainsKey($"stale{i}"));
        for (int i = 0; i < 5; i++) Assert.True(last.ContainsKey($"live{i}"));
        Assert.True(last.ContainsKey("probe"));
    }

    [Fact]
    public void SweepPath_ProbeBound_StillThrottlesHotHost()
    {
        var last = new Dictionary<string, double>();
        for (int i = 0; i < 1100; i++) last[$"h{i}"] = 99.0;
        Assert.False(ThrottleWindow.ShouldLog(last, NewLock(), "h0", 5.0, 100.0));
    }
}
