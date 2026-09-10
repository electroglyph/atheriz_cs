// Pins for the set/unset hoists: hoisted trims, the case-sensitive
// move/teleport gate (Ordinal — not the OrdinalIgnoreCase Protected set),
// and the first-match repr switch.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class SetCommandGateReprTests
{
    private static GameObject MakeSuperuser(string name = "Root")
    {
        var c = GameObject.Create(name, privilege: Atheriz.Core.Privilege.Admin);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static GameObject MakeBuilder(string name = "Bob")
    {
        var c = GameObject.Create(name, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    [Fact]
    public void Set_LowercaseLocation_AsSuperuser_HitsMoveGate()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = MakeBuilder("Target");
        new SetCommand().Run(root, new SetCommand().Parser!.ParseArgs(["Target", "location", "x"]));
        Assert.Contains(root.PeekMessages(), m => m == "'location' cannot be set directly; use move/teleport instead.");
    }

    [Fact]
    public void Set_UppercaseLocation_AsSuperuser_BypassesMoveGate()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = MakeBuilder("Target");
        // The gate is Ordinal: "LOCATION" is not gated (and not a known
        // property either), so it lands in extras like any junk attribute.
        new SetCommand().Run(root, new SetCommand().Parser!.ParseArgs(["Target", "LOCATION", "x"]));
        Assert.Contains(root.PeekMessages(), m => m.Contains("Warning: 'LOCATION' is a new attribute"));
        Assert.True(target.HasExtra("LOCATION"));
    }

    [Fact]
    public void Set_Location_AsBuilder_HitsProtectedGateFirst()
    {
        using var env = GlobalTestEnv.Enter();
        var bob = MakeBuilder();
        var target = MakeBuilder("Target");
        target.PrivilegeLevel = Atheriz.Core.Privilege.Player;
        new SetCommand().Run(bob, new SetCommand().Parser!.ParseArgs(["Target", "location", "x"]));
        Assert.Contains(bob.PeekMessages(), m => m == "'location' is protected and cannot be set.");
    }

    [Fact]
    public void Set_ReprCoversNullStringBool()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = MakeBuilder("Target");
        var cmd = new SetCommand();
        cmd.Run(root, cmd.Parser!.ParseArgs(["Target", "desc", "None"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set Target.desc = None");
        root.ClearMessages();
        cmd.Run(root, cmd.Parser!.ParseArgs(["Target", "desc", "hello"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set Target.desc = 'hello'");
        root.ClearMessages();
        cmd.Run(root, cmd.Parser!.ParseArgs(["Target", "desc", "True"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set Target.desc = True");
    }

    [Fact]
    public void IsProtected_CaseInsensitive_WhileGateStaysSensitive()
    {
        Assert.True(SetHelper.IsProtected("is_pc"));
        Assert.True(SetHelper.IsProtected("IS_PC"));
        Assert.True(SetHelper.IsProtected("Is_Pc"));
        Assert.False(SetHelper.IsProtected("desc"));
    }
}
