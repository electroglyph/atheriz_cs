using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Channel verbs parse once into a ChannelAction: list beats unsubscribe
// beats subscribe beats replay beats send.
[Collection("Ported")]
public sealed class ChannelActionPrecedenceTests
{
    private static ChannelAction ParseChannelAction(params string[] argv)
    {
        // Mirror ChannelCommand's full parser: common defs plus its own
        // -l/-c/-s extras (BaseChannelCommand has no list/subscribe flags).
        var p = new GameArgumentParser();
        ChannelActionParser.AddCommonArgs(p);
        p.AddArgument("-l", "--list").Help("List all channels").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-c", "--channel").Help("Channel to target");
        p.AddArgument("-s", "--subscribe").Help("Subscribe to channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        return ChannelActionParser.Parse(p.ParseArgs(argv));
    }

    [Fact]
    public void ChannelAction_ListBeatsUnsubscribe()
    {
        // -l wins over every other flag, matching the old triage order.
        Assert.Equal(ChannelAction.List, ParseChannelAction("-l", "-u"));
    }

    [Fact]
    public void ChannelAction_UnsubscribeBeatsSubscribeAndReplay()
    {
        // -u wins over -s and -r.
        Assert.Equal(ChannelAction.Unsubscribe, ParseChannelAction("-u", "-s", "-r"));
    }

    [Fact]
    public void ChannelAction_SubscribeBeatsReplay()
    {
        // -s wins over -r; a bare message (or nothing) is a send.
        Assert.Equal(ChannelAction.Subscribe, ParseChannelAction("-s", "-r"));
        Assert.Equal(ChannelAction.Replay, ParseChannelAction("-r"));
        Assert.Equal(ChannelAction.Send, ParseChannelAction("hello"));
        Assert.Equal(ChannelAction.Send, ParseChannelAction());
    }
}
