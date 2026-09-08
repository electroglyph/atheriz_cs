using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// A failed detach raises (utils.py:538-550), like Python — never the live
// original, never a silent blank. Holder whose Func member cannot survive
// a JSON round-trip, so Detach deterministically hits the failure path.
public sealed class DetachTests
{
    // Holder with a member JSON cannot represent; serialization throws every run.
    private sealed class HoldsCallback
    {
        public Func<int>? Callback { get; set; } = () => 1;
        public int Value { get; set; } = 1;
    }

    [Fact]
    public void Detach_FailedRoundTrip_Throws()
    {
        var original = new HoldsCallback();
        Assert.ThrowsAny<Exception>(() => GameUtils.Detach(original));
    }

    [Fact]
    public void Detach_RoundTrip_CopiesIndependently()
    {
        var original = new HoldsCallback { Value = 1, Callback = null };
        var copy = GameUtils.Detach(original);
        Assert.NotNull(copy);
        Assert.NotSame(original, copy);
        copy!.Value = 999;
        Assert.Equal(1, original.Value);
    }
}
