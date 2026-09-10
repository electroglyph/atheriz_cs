using Atheriz.Core.Globals;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Globals;

// GetTimespan unit folding produces byte-identical descriptions: the shared
// unit helper must keep the decimal math, ", "/"and" joins, and past/future
// suffixes exactly (defaults: 1 tick = 1 minute, 60 ticks = 1 hour).
[Collection("Ported")]
public class GameTimeTimespanDescTests
{
    private static GameTime Unloaded() => new(new AtherizSettings(), autoLoad: false);

    [Fact]
    public void GetTimespan_Zero_IsNow()
    {
        var ts = Unloaded().GetTimespan(0);
        Assert.Equal("now", ts.Desc);
        Assert.Equal(0, ts.Years);
        Assert.Equal(0, ts.Hours);
    }

    [Fact]
    public void GetTimespan_SingleUnits_UseSingular()
    {
        Assert.Equal("1 hour ago", Unloaded().GetTimespan(60).Desc);
        Assert.Equal("1 week and 2 days ago", Unloaded().GetTimespan(10080 + 2 * 1440).Desc);
    }

    [Fact]
    public void GetTimespan_MultiUnit_JoinsWithAnd()
    {
        // 1 year + 1 month + 1 hour: comma-joined with "and" before the last unit.
        var ts = Unloaded().GetTimespan(518400 + 43200 + 60);
        Assert.Equal("1 year, 1 month and 1 hour ago", ts.Desc);
        Assert.Equal(1, ts.Years);
        Assert.Equal(1, ts.Months);
        Assert.Equal(1, ts.Hours);
    }

    [Fact]
    public void GetTimespan_PluralUnits_UsePlurals()
    {
        Assert.Equal("2 hours ago", Unloaded().GetTimespan(120).Desc);
    }

    [Fact]
    public void GetTimespan_MinutesOnly_FormatsDoublePath()
    {
        Assert.Equal("30 minutes ago", Unloaded().GetTimespan(30).Desc);
    }

    [Fact]
    public void GetTimespan_Negative_IsFuture()
    {
        Assert.Equal("1 hour in the future", Unloaded().GetTimespan(-60).Desc);
    }
}
