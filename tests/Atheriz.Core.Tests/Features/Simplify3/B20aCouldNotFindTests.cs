// Pins for simplify3 B20a "Could not find" centralization: follow, group add,
// and socials target share one helper; the give "...here." variant stays out.
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B20aCouldNotFindTests
{
    private static (Node Room, GameObject Caller) EnterCaller(string area, string name)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(room);
        var caller = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        Assert.True(caller.MoveTo(room));
        caller.ClearMessages();
        return (room, caller);
    }

    [Fact]
    public void FollowCommand_UnknownTarget_ReportsCouldNotFind()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, caller) = EnterCaller("b20afollow", "b20a_follower");
        try
        {
            new FollowCommand().Run(caller, new FollowCommand().Parser!.ParseArgs(["ghostxyz"]));
            Assert.Contains("Could not find 'ghostxyz'.", caller.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_AddUnknownTarget_ReportsCouldNotFind()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader) = EnterCaller("b20agroup", "b20a_leader");
        try
        {
            var cmd = new GroupCommand();
            var pa = new GameArgumentParser.ParsedArgs();
            pa["args"] = new List<string> { "add", "ghostxyz" };
            cmd.Run(leader, pa);
            Assert.Contains("Could not find 'ghostxyz'.", leader.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void SocialsCommand_UnknownTarget_ReportsCouldNotFind()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, alice) = EnterCaller("b20asocial", "b20a_alice");
        try
        {
            var pa = new GameArgumentParser.ParsedArgs();
            pa.CmdString = "hug";
            pa["target"] = new List<string> { "ghostxyz" };
            new SocialsCommand().Run(alice, pa);
            Assert.Contains("Could not find 'ghostxyz'.", alice.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void FormatCouldNotFind_ReturnsByteIdenticalString()
    {
        Assert.Equal("Could not find 'ghostxyz'.", CommandHelpers.FormatCouldNotFind("ghostxyz"));
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("b20a_fmt", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        CommandHelpers.MsgCouldNotFind(caller, "ghostxyz");
        Assert.Contains("Could not find 'ghostxyz'.", caller.PeekMessages());
    }

    [Fact]
    public void GiveCommand_MissingTarget_KeepsHereVariant()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, giver) = EnterCaller("b20agive", "b20a_giver");
        try
        {
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(giver));
            giver.ClearMessages();
            var pa = new GiveCommand().Parser!.ParseArgs(["coin", "to", "ghostxyz"]);
            new GiveCommand().Run(giver, pa);
            Assert.Contains("Could not find 'ghostxyz' here.", giver.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
