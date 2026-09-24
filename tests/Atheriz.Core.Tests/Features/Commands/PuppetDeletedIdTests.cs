using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Companion to PuppetCommandIdTests: a deleted #id target reports
// unavailability instead of reaching the puppet-lock branch.
[Collection("Ported")]
public sealed class PuppetDeletedIdTests
{
    [Fact]
    public void Puppet_DeletedIdTarget_ReportsNotAvailable()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("builderdel", isPc: true, privilege: Privilege.Builder);
        var sess = new Session(new TestConnection());
        caller.Session = sess;
        sess.Puppet = caller;
        ObjectRegistry.AddObject(caller);
        var target = GameObject.Create("doomed", isNpc: true, privilege: Privilege.Guest);
        ObjectRegistry.AddObject(target);
        // A failing puppet lock proves the deleted check runs first: without
        // it the command would answer "You cannot puppet doomed." instead.
        target.AddLock("puppet", _ => false);
        target.IsDeleted = true;
        var pa = new GameArgumentParser.ParsedArgs();
        pa["target"] = $"#{target.Id}";
        caller.ClearMessages();
        new PuppetCommand().Run(caller, pa);
        Assert.Contains($"{target.Name} is not available.", caller.PeekMessages());
        Assert.DoesNotContain($"You cannot puppet {target.Name}.", caller.PeekMessages());
        Assert.Same(caller, sess.Puppet);
    }
}
