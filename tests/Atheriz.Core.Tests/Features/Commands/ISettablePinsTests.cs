using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins for the ISettable merge: known properties dispatch through the
// explicit per-type switch with the same accepted inputs and failures the
// old Convert.ChangeType path had; unknown names still land in extras.
[Collection("Ported")]
public sealed class ISettablePinsTests
{
    private static GameObject MakeObj()
    {
        var o = GameObject.Create("settable");
        ObjectRegistry.AddObject(o);
        return o;
    }

    [Fact]
    public void ISettable_StringProperty_SetsThroughAllSpellings()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        Assert.True(o.TrySetProperty("Desc", "a", out var e1));
        Assert.Null(e1);
        Assert.Equal("a", o.Desc);
        SetHelper.SetAttr(o, "desc", "b");
        Assert.Equal("b", o.Desc);
        SetHelper.SetAttr(o, "_desc", "c");
        Assert.Equal("c", o.Desc);
        Assert.True(SetHelper.HasKnownProp(o, "desc"));
    }

    [Fact]
    public void ISettable_BoolProperty_AcceptsBoolTextAndNumber()
    {
        // Mirrors Convert.ChangeType: bools pass, "True" parses, non-zero
        // numbers are true.
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        SetHelper.SetAttr(o, "quelled", true);
        Assert.True(o.Quelled);
        SetHelper.SetAttr(o, "quelled", "True");
        Assert.True(o.Quelled);
        SetHelper.SetAttr(o, "quelled", 1);
        Assert.True(o.Quelled);
        SetHelper.SetAttr(o, "quelled", false);
        Assert.False(o.Quelled);
    }

    [Fact]
    public void ISettable_DoubleProperty_RejectsTextWithConversionError()
    {
        // Non-numeric text for a double is a conversion failure, never a
        // read-only refusal, with the same FormatException as before.
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        var ex = Assert.Throws<FormatException>(() => SetHelper.SetAttr(o, "tick_seconds", "abc"));
        Assert.Contains("abc", ex.Message);
        Assert.Throws<FormatException>(() => o.TrySetProperty("tick_seconds", "abc", out _));
        Assert.Equal(1.0, o.TickSeconds);
    }

    [Fact]
    public void ISettable_IntProperty_RoundsDoubleLikeChangeType()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        SetHelper.SetAttr(o, "following", 5.5);
        Assert.Equal(6, o.Following);
        SetHelper.SetAttr(o, "following", "7");
        Assert.Equal(7, o.Following);
    }

    [Fact]
    public void ISettable_EnumProperty_AcceptsOnlyPrivilegeValues()
    {
        // Mirrors Convert.ChangeType to an enum target: only an actual
        // privilege converts; numbers and text never did and still throw.
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        SetHelper.SetAttr(o, "privilege_level", Privilege.Helper);
        Assert.Equal(Privilege.Helper, o.PrivilegeLevel);
        Assert.Throws<InvalidCastException>(() => SetHelper.SetAttr(o, "privilege_level", 5));
        Assert.Throws<InvalidCastException>(() => SetHelper.SetAttr(o, "privilege_level", "Builder"));
    }

    [Fact]
    public void ISettable_ReadOnlyProperty_ReportsReadOnly()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        Assert.False(o.TrySetProperty("id", 5, out var err));
        Assert.NotNull(err);
        Assert.Throws<InvalidOperationException>(() => SetHelper.SetAttr(o, "id", 5));
        Assert.False(o.TrySetProperty("is_pc", true, out _));
        Assert.True(SetHelper.HasKnownProp(o, "is_pc"));
    }

    [Fact]
    public void ISettable_UnknownName_FallsThroughToExtras()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        Assert.False(o.TrySetProperty("note", "hi", out var err));
        Assert.Null(err);
        Assert.False(SetHelper.HasKnownProp(o, "note"));
        SetHelper.SetAttr(o, "note", "hi");
        Assert.True(o.HasExtra("note"));
    }

    [Fact]
    public void ISettable_SessionProperty_AcceptsSessionOnly()
    {
        using var env = GlobalTestEnv.Enter();
        var o = MakeObj();
        var sess = new Session();
        SetHelper.SetAttr(o, "session", sess);
        Assert.Same(sess, o.Session);
        Assert.Throws<InvalidCastException>(() => SetHelper.SetAttr(o, "session", "x"));
    }

    [Fact]
    public void ISettable_NodeProperties_DispatchThroughOverride()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("settablepin", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        SetHelper.SetAttr(node, "theme", "dark");
        Assert.Equal("dark", node.Theme);
        Assert.False(node.TrySetProperty("scripts_set", new HashSet<int>(), out var err));
        Assert.NotNull(err);
        Assert.Throws<InvalidCastException>(() => SetHelper.SetAttr(node, "coord", "nonsense"));
        // Base properties still resolve on the subtype.
        Assert.True(SetHelper.HasKnownProp(node, "desc"));
    }

    [Fact]
    public void ISettable_ChannelAndAccountProperties_DispatchThroughOverride()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("settablechan");
        SetHelper.SetAttr(ch, "created_by", 3);
        Assert.Equal(3, ch.CreatedBy);
        Assert.False(ch.TrySetProperty("listeners", new HashSet<int>(), out _));
        var acct = Account.Create("settableacct", "pass12345");
        SetHelper.SetAttr(acct, "ban_reason", "spam");
        Assert.Equal("spam", acct.BanReason);
        Assert.False(acct.TrySetProperty("characters", new List<int>(), out _));
    }
}
