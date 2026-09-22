using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Dice average uses long/double math (no int overflow) and rejects
// faces below 1. Pure functions only — no engine globals touched.
public sealed class DiceRollAverageTests
{
    [Fact]
    public void DiceRollAverage_MaxFaces_NoOverflow()
    {
        Assert.Equal(1073741824.0, GameUtils.DiceRollAverage(1, int.MaxValue));
        Assert.Equal(7.0, GameUtils.DiceRollAverage(2, 6));
    }

    [Fact]
    public void DiceRollAverage_ZeroFaces_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameUtils.DiceRollAverage(2, 0));
    }
}
