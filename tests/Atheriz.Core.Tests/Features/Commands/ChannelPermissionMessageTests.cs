// Channel permission/history messages: the shared CommandHelpers messages
// stay byte-identical through both BaseChannelCommand and ChannelCommand paths.
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class ChannelPermissionMessageTests
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
    public void BaseChannelCommand_ReplayWithoutViewPermission_ReportsViewDenied()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("b20aview");
        chan.AddLock("view", _ => false);
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        var caller = MakePc("b20a_viewer");
        cmd!.Run(caller, cmd.Parser!.ParseArgs(["-r"]));
        Assert.Contains("You do not have permission to view this channel.", caller.PeekMessages());
    }

    [Fact]
    public void BaseChannelCommand_ReplayEmptyHistory_ReportsNoHistory()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("b20aempty");
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        var caller = MakePc("b20a_reader");
        cmd!.Run(caller, cmd.Parser!.ParseArgs(["-r"]));
        Assert.Contains("No history available.", caller.PeekMessages());
    }

    [Fact]
    public void BaseChannelCommand_SendWithoutSendPermission_ReportsSendDenied()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("b20asend");
        chan.AddLock("send", _ => false);
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        var caller = MakePc("b20a_sender");
        var before = chan.History.Count;
        cmd!.Run(caller, cmd.Parser!.ParseArgs(["hello"]));
        Assert.Equal(before, chan.History.Count);
        Assert.Contains("You do not have permission to send to this channel.", caller.PeekMessages());
    }

    [Fact]
    public void ChannelCommand_SubscribeWithoutViewPermission_ReportsViewDenied()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var chan = Channel.Create("b20asub");
            chan.AddLock("view", _ => false);
            var caller = MakePc("b20a_subber");
            new ChannelCommand().Run(caller, ChannelArgs("b20asub", subscribe: true));
            Assert.Contains("You do not have permission to view this channel.", caller.PeekMessages());
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void ChannelCommand_ReplayEmptyHistory_ReportsNoHistory()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            Channel.Create("b20areplay");
            var caller = MakePc("b20a_replayer");
            new ChannelCommand().Run(caller, ChannelArgs("b20areplay", replay: true));
            Assert.Contains("No history available.", caller.PeekMessages());
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void ChannelCommand_SendWithoutSendPermission_ReportsSendDenied()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var chan = Channel.Create("b20achsend");
            chan.AddLock("send", _ => false);
            var caller = MakePc("b20a_chsender");
            var before = chan.History.Count;
            new ChannelCommand().Run(caller, ChannelArgs("b20achsend", message: ["hello"]));
            Assert.Equal(before, chan.History.Count);
            Assert.Contains("You do not have permission to send to this channel.", caller.PeekMessages());
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void MsgHelpers_SendByteIdenticalChannelStrings()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakePc("b20a_helper");
        CommandHelpers.MsgChannelViewDenied(caller);
        CommandHelpers.MsgChannelSendDenied(caller);
        CommandHelpers.MsgNoChannelHistory(caller);
        var msgs = caller.PeekMessages();
        Assert.Contains("You do not have permission to view this channel.", msgs);
        Assert.Contains("You do not have permission to send to this channel.", msgs);
        Assert.Contains("No history available.", msgs);
    }
}
