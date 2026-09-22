using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// A view-denied channel reads as not-found on every ChannelCommand branch,
// so probing a name cannot distinguish "view-locked" from "nonexistent".
// A viewable channel without send keeps the send-denied message.
[Collection("Ported")]
public sealed class ChannelViewNotFoundTests
{
    private static GameObject MakePc(string name)
    {
        var go = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(go);
        go.IsConnected = true;
        go.ClearMessages();
        return go;
    }

    private static GameArgumentParser.ParsedArgs ChannelArgs(
        string channel, bool subscribe = false, bool replay = false, List<string>? message = null)
    {
        var pa = new GameArgumentParser.ParsedArgs();
        pa["channel"] = channel;
        pa["list"] = false;
        pa["unsubscribe"] = false;
        pa["subscribe"] = subscribe;
        pa["replay"] = replay;
        pa["message"] = message ?? [];
        return pa;
    }

    [Fact]
    public void ChannelCommand_ViewLockedChannel_SubscribeReplaySendReportNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var chan = Channel.Create("vhidden");
            chan.AddLock("view", _ => false);
            var caller = MakePc("vhidden_prober");
            new ChannelCommand().Run(caller, ChannelArgs("vhidden", subscribe: true));
            new ChannelCommand().Run(caller, ChannelArgs("vhidden", replay: true));
            var before = chan.History.Count;
            new ChannelCommand().Run(caller, ChannelArgs("vhidden", message: ["hello"]));
            Assert.Equal(before, chan.History.Count);
            var text = string.Join("\n", caller.PeekMessages());
            Assert.DoesNotContain("permission", text);
            Assert.Equal(3, caller.PeekMessages().Count(m => m == "Channel vhidden not found."));
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void ChannelCommand_OpenChannel_SendDelivers()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var chan = Channel.Create("vopen");
            var caller = MakePc("vopen_sender");
            var before = chan.History.Count;
            new ChannelCommand().Run(caller, ChannelArgs("vopen", message: ["hi"]));
            Assert.Equal(before + 1, chan.History.Count);
        }
        finally { ChannelCommand.ClearCache(); }
    }
}
