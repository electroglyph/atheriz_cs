using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Targets hidden by a view lock (or already deleted) are not actionable:
// neither the #id search leg nor follow/socials may resolve them.
[Collection("Ported")]
public class HiddenTargetGatingTests
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
        // The #id leg honors the view gate like the name path — a
        // view-locked target is not resolvable by id.
        using var env = GlobalTestEnv.Enter();
        var caller = MakePc("Caller");
        var hidden = MakePc("Hidden");
        hidden.AddLock("view", _ => false);

        Assert.Empty(CommandHelpers.SearchWithFallback(caller, $"#{hidden.Id}"));
    }

    [Fact]
    public void SearchWithFallback_DeletedIdTarget_ReturnsEmpty()
    {
        // A deleted target is not resolvable by id either.
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
        // Follow must not latch onto a view-locked target.
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
        // Socials must not emote at a view-locked target.
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
}
