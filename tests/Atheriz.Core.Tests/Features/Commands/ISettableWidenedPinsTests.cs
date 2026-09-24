using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins restoring Convert.ChangeType coverage in the ISettable switches:
// bools and widened numerics convert like the old generic path did
// (including OverflowException on overflow, not InvalidCastException).
[Collection("Ported")]
public sealed class ISettableWidenedPinsTests
{
    private static GameObject MakeObj()
    {
        var o = GameObject.Create("widened");
        ObjectRegistry.AddObject(o);
        return o;
    }

    [Fact]
    public void ISettable_IntProperty_AcceptsBoolAndWidenedNumerics()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        SetHelper.SetAttr(o, "following", true);
        Assert.Equal(1, o.Following);
        SetHelper.SetAttr(o, "following", 2L);
        Assert.Equal(2, o.Following);
        SetHelper.SetAttr(o, "following", 3.0f);
        Assert.Equal(3, o.Following);
        SetHelper.SetAttr(o, "following", 4m);
        Assert.Equal(4, o.Following);
        SetHelper.SetAttr(o, "following", (short)5);
        Assert.Equal(5, o.Following);
    }

    [Fact]
    public void ISettable_DoubleProperty_AcceptsBoolAndLong()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        SetHelper.SetAttr(o, "tick_seconds", true);
        Assert.Equal(1.0, o.TickSeconds);
        SetHelper.SetAttr(o, "tick_seconds", 5L);
        Assert.Equal(5.0, o.TickSeconds);
    }

    [Fact]
    public void ISettable_BoolProperty_AcceptsWidenedNumerics()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        SetHelper.SetAttr(o, "quelled", 1L);
        Assert.True(o.Quelled);
        SetHelper.SetAttr(o, "quelled", 0.0);
        Assert.False(o.Quelled);
        SetHelper.SetAttr(o, "quelled", (decimal)2);
        Assert.True(o.Quelled);
    }

    [Fact]
    public void ISettable_IntProperty_OverflowStillThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        Assert.Throws<OverflowException>(() => SetHelper.SetAttr(o, "following", long.MaxValue));
    }
}
