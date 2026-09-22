using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Quoted string contents must pass through verbatim (SetCommand.cs:49-64),
// and conversion failures must not be misreported as read-only (SetHelper.cs:72, SetCommand.cs:95).
[Collection("Ported")]
public class SetCommandTests
{
    [Fact]
    public void Set_QuotedStringContainingKeyword_IsPreservedVerbatim()
    {
        // The inner keyword inside a quoted literal must survive untouched.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("bob", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            builder.Desc = "before";
            builder.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(builder, "set me desc \"'Nonexistent'\"", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.DoesNotContain("nullxistent", builder.Desc);
            Assert.Contains("Nonexistent", builder.Desc);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Set_InvalidTickSeconds_DoesNotReportReadOnly()
    {
        // A non-numeric value for a numeric property is a conversion problem, not a lock problem.
        ObjectRegistry.ClearAll();
        try
        {
            var builder = GameObject.Create("bob", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            builder.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(builder, "set me tick_seconds abc", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var msgs = string.Join("\n", builder.PeekMessages());
            Assert.DoesNotContain("read-only", msgs, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1.0, builder.TickSeconds);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    private static GameObject MakePlayer(string name, bool isPc = false)
    {
        var c = GameObject.Create(name, "", isPc: isPc, privilege: Privilege.Player);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static GameObject MakeSuperuser(string name = "Root")
    {
        var c = GameObject.Create(name, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    [Fact]
    public void Set_Name_DuplicateBlockedForSuperuser()
    {
        // Superuser rename to a taken character name is refused.
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var taken = GameObject.Create("TakenName", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(taken);
        var target = GameObject.Create("Target", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(target);
        var cmd = new SetCommand();
        cmd.Run(root, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "TakenName"]));
        Assert.Contains(root.PeekMessages(), m => m == "Character with this name (TakenName) already exists.");
        Assert.Equal("Target", target.Name);
    }

    [Fact]
    public void Set_Name_SameNameAllowed()
    {
        // Rename to the target's own current name is not a collision.
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = GameObject.Create("TargetSelf", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(target);
        var cmd = new SetCommand();
        cmd.Run(root, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "TargetSelf"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set TargetSelf.name = 'TargetSelf'");
        Assert.Equal("TargetSelf", target.Name);
    }

    [Fact]
    public void Set_Name_ReservedWordRejected()
    {
        // Reserved words are rejected on rename (shares the reserved-word
        // validation).
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = GameObject.Create("TargetR", "", isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(target);
        var cmd = new SetCommand();
        cmd.Run(root, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "here"]));
        Assert.Contains(root.PeekMessages(), m => m == "That name is reserved.");
        Assert.Equal("TargetR", target.Name);
    }

    [Fact]
    public void Set_Name_AsBuilder_HitsProtectedGate()
    {
        // Non-superusers still hit the protected gate before validation.
        using var env = GlobalTestEnv.Enter();
        var bob = MakePlayer("Bob");
        bob.PrivilegeLevel = Privilege.Builder;
        var target = MakePlayer("TargetB");
        target.PrivilegeLevel = Privilege.Player;
        var cmd = new SetCommand();
        cmd.Run(bob, cmd.Parser!.ParseArgs(["#" + target.Id, "name", "NewName"]));
        Assert.Contains(bob.PeekMessages(), m => m == "'name' is protected and cannot be set.");
        Assert.Equal("TargetB", target.Name);
    }
}
