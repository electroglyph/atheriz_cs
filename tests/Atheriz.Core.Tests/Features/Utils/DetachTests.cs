using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// A failed deep-copy must never hand back the live original: callers mutate
// the "detached" copy, so aliasing corrupts source state (GameUtils.cs:441-454).
// Pure-function checks over a holder whose Func member cannot survive a JSON
// round-trip, so Detach deterministically falls into its failure path.
public sealed class DetachTests
{
    // Holder with a member JSON cannot represent; serialization throws every run.
    private sealed class HoldsCallback
    {
        public Func<int>? Callback { get; set; } = () => 1;
        public int Value { get; set; } = 1;
    }

    [Fact]
    public void Detach_FailedRoundTrip_ReturnsIndependentCopy()
    {
        var original = new HoldsCallback();
        var copy = GameUtils.Detach(original);
        Assert.NotNull(copy);
        Assert.NotSame(original, copy);
    }

    [Fact]
    public void Detach_FailedRoundTrip_MutatingCopyLeavesOriginalUnchanged()
    {
        var original = new HoldsCallback { Value = 1 };
        var copy = GameUtils.Detach(original);
        Assert.NotNull(copy);
        copy!.Value = 999;
        Assert.Equal(1, original.Value);
    }
}
