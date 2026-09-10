// Pins for the shared single-id fetch core (ObjectRegistry.GetSingle): same
// "not found" outcome as Get(id).FirstOrDefault() at every converted site,
// puppet #id keeping its own (non-ban) messages, and the channel alias
// cache evicting the stale key without a full-cache scan.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class SingleFetchHelperTests
{
    [Fact]
    public void GetSingle_MatchesListFetch_AndNullWhenMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var o = GameObject.Create("Solo");
        ObjectRegistry.AddObject(o);
        Assert.Same(o, ObjectRegistry.GetSingle(o.Id));
        Assert.Same(ObjectRegistry.Get(o.Id).FirstOrDefault(), ObjectRegistry.GetSingle(o.Id));
        Assert.Null(ObjectRegistry.GetSingle(-999));
        Assert.True(ObjectRegistry.TryGetSingle(o.Id, out var found) && ReferenceEquals(o, found));
        Assert.False(ObjectRegistry.TryGetSingle(-999, out _));
    }

    [Fact]
    public void Puppet_HashId_KeepsOwnMessages_NotBanWording()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("Builder", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var prop = GameObject.Create("Rock", isItem: true);
        ObjectRegistry.AddObject(prop);
        // Session first: puppet without one reports "no active session"
        // (established PuppetWithoutSessionMessages behavior).
        var sess = new Session();
        caller.Session = sess;
        sess.Puppet = caller;
        // Malformed id keeps the puppet-specific format message...
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["#x"]));
        Assert.Contains(caller.PeekMessages(), m => m == "Invalid ID format. Use #<number>.");
        caller.ClearMessages();
        // ...unknown id keeps the plain not-found message...
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["#999999"]));
        Assert.Contains(caller.PeekMessages(), m => m == "No object found with ID 999999.");
        caller.ClearMessages();
        // ...and a non-PC #id is NOT refused with ban wording (puppet
        // accepts any object; the IsPc gate lives in BanHelper only).
        new PuppetCommand().Run(caller, new PuppetCommand().Parser!.ParseArgs(["#" + prop.Id]));
        Assert.DoesNotContain(caller.PeekMessages(), m => m.Contains("ban"));
    }

    [Fact]
    public void Channel_AliasInsert_EvictsOnlyStaleKey()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var caller = GameObject.Create("Chatter", isPc: true);
            ObjectRegistry.AddObject(caller);
            caller.ClearMessages();
            var channel = Channel.Create("public");
            var cmd = new ChannelCommand();
            cmd.Run(caller, cmd.Parser!.ParseArgs(["-c", "public"]));
            Assert.True(ChannelCommand.TryGetCached("public", out _));
            // Rename + resolve under the new name: the stale key goes away,
            // the new key caches — the same surviving entry as the old scan.
            channel.Name = "pub2";
            cmd.Run(caller, cmd.Parser!.ParseArgs(["-c", "pub2"]));
            Assert.False(ChannelCommand.TryGetCached("public", out _));
            Assert.True(ChannelCommand.TryGetCached("pub2", out var cached));
            Assert.Same(channel, cached);
        }
        finally { ChannelCommand.ClearCache(); }
    }
}
