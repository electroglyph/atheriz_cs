using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Globals;

// Negative ticks floor like Python // (time.py:390-398), not truncate.
[Collection("Ported")]
public class GameTimeNegativeTests
{
    [Fact]
    public void Calendar_NegativeTick_FloorsDay()
    {
        var gt = new GameTime(null, autoLoad: false);
        gt.Ticks = 0;
        var zero = gt.GetTime();
        gt.Ticks = -1;
        var neg = gt.GetTime();
        // One minute before epoch is still the previous calendar day
        // (truncation bugs collapse it onto day zero).
        Assert.NotEqual(zero.Day, neg.Day);
    }
}
