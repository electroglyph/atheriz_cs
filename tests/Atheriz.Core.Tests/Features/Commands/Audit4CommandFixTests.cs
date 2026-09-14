using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Regression pins for the audit4 command fixes (C-1..C-3).
[Collection("Ported")]
public class Audit4CommandFixTests
{
    private static GameObject MakePc(string name)
    {
        var o = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(o);
        o.ClearMessages();
        return o;
    }

    [Fact]
    public void SearchWithFallback_HiddenIdTarget_ReturnsEmpty()
    {
        // C-1: the #id leg must honor the view gate like the name path —
        // a view-locked target is not resolvable by id.
        using var env = GlobalTestEnv.Enter();
        var caller = MakePc("Caller");
        var hidden = MakePc("Hidden");
        hidden.AddLock("view", _ => false);

        Assert.Empty(CommandHelpers.SearchWithFallback(caller, $"#{hidden.Id}"));
    }

    [Fact]
    public void SearchWithFallback_DeletedIdTarget_ReturnsEmpty()
    {
        // C-1: a deleted target is not resolvable by id either.
        using var env = GlobalTestEnv.Enter();
        var caller = MakePc("Caller");
        var gone = MakePc("Gone");
        int id = gone.Id;
        gone.Delete(caller, true);
        Assert.True(gone.IsDeleted);

        Assert.Empty(CommandHelpers.SearchWithFallback(caller, $"#{id}"));
    }

    [Fact]
    public void FollowCommand_HiddenTarget_IsNotFollowable()
    {
        // C-1: follow must not latch onto a view-locked target.
        using var env = GlobalTestEnv.Enter();
        var follower = MakePc("Follower");
        var hidden = MakePc("Hidden");
        hidden.IsNpc = true;
        hidden.AddLock("view", _ => false);

        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(new[] { $"#{hidden.Id}" }));
        Assert.Null(follower.Following);
        Assert.Contains(follower.PeekMessages(), m => m.Contains("Could not find"));
    }

    [Fact]
    public void SocialsCommand_HiddenTarget_IsRejected()
    {
        // C-1: socials must not emote at a view-locked target.
        using var env = GlobalTestEnv.Enter();
        var actor = MakePc("Actor");
        var hidden = MakePc("Hidden");
        hidden.AddLock("view", _ => false);

        var cmd = new SocialsCommand();
        var (func, _, args) = cmd.Execute(actor, $"#{hidden.Id}", "smile");
        Assert.NotNull(func);
        func!(actor, args);
        Assert.Contains(actor.PeekMessages(), m => m.Contains("Could not find"));
    }

    [Fact]
    public void Execute_HelpFlag_SendsSingleMessage()
    {
        // C-2: explicit --help surfaces the help text exactly once (the
        // message IS the help), not as diagnosis-plus-help.
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        var cmd = new FollowCommand();
        var (func, _, _) = cmd.Execute(puppet, "--help");
        Assert.Null(func);
        Assert.Single(puppet.PeekMessages());
    }

    [Fact]
    public void QuellCommand_QuelledBuilder_ReachesAlreadyQuelledBranch()
    {
        // C-3: the access gate must not fold quelled state, or the
        // already-quelled branch is unreachable through dispatch.
        using var env = GlobalTestEnv.Enter();
        var c = Ported.PortedHelpers.MakeCaller("Alice", builder: true);
        c.Quelled = true;
        Assert.True(new QuellCommand().Access(c));
        c.ClearMessages();
        new QuellCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m == "You are already quelled!");
    }
}
