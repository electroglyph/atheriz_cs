using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One snapshot pass for both PC counts: same materialized list, so one loop
// is arithmetically identical to Count(predicate) + Count. Static predicate
// (no per-call closure); the 5 s render cache untouched.
[Collection("Ported")]
public class ConnectionScreenCountTests
{
    private static void ResetCache()
    {
        typeof(ConnectionScreen).GetField("_cacheTs", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0.0);
    }

    [Fact]
    public void GetOnline_EmptyRegistry_ZeroZero()
    {
        using var env = GlobalTestEnv.Enter();
        ResetCache();
        Assert.Equal((0, 0), ConnectionScreen.GetOnline());
    }

    [Fact]
    public void GetOnline_SinglePass_CountsBoth()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.AddObject(GameObject.Create("known_pc", isPc: true));
        ObjectRegistry.AddObject(GameObject.Create("mob"));
        ResetCache();
        var (online, known) = ConnectionScreen.GetOnline();
        Assert.Equal(1, known);
        Assert.Equal(0, online);
        Assert.True(known >= online);
    }

    [Fact]
    public void Counting_UsesOneLoop_AndStaticPredicate()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
        Assert.Contains("static readonly Func<", src);
        Assert.Contains("IsPcFilter", src);
        Assert.DoesNotContain(".Count(o =>", src);
    }
}
