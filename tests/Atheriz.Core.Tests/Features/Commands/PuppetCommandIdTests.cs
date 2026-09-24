using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Puppet is Builder+ only and authorizes via puppet locks; the #id path
// deliberately skips the view gate — disconnected PCs deny view while
// offline (PcView: IsConnected) and must stay puppetable.
[Collection("Ported")]
public sealed class PuppetCommandIdTests
{
    [Fact]
    public void Puppet_IdTarget_IgnoresViewLock()
    {
        // A builder puppets a view-denying NPC by #id: the view lock must
        // not gate puppet selection.
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("builder", isPc: true, privilege: Privilege.Builder);
        var sess = new Session(new TestConnection());
        caller.Session = sess;
        sess.Puppet = caller;
        ObjectRegistry.AddObject(caller);
        var target = GameObject.Create("idhidden", isNpc: true, privilege: Privilege.Guest);
        ObjectRegistry.AddObject(target);
        target.AddLock("view", _ => false);
        var pa = new GameArgumentParser.ParsedArgs();
        pa["target"] = $"#{target.Id}";
        caller.ClearMessages();
        new PuppetCommand().Run(caller, pa);
        Assert.True(target.IsPc);
        Assert.Same(target, sess.Puppet);
    }
}
