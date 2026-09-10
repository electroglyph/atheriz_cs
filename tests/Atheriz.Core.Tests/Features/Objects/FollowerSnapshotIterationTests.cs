using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// Follower delivery still walks the live snapshot directly: a leader move
// auto-moves colocated followers through at_post_move without a copy.
[Collection("Ported")]
public class FollowerSnapshotIterationTests
{
    private static (Node n1, Node n2) SetupNodes(string area)
    {
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 1, 0)));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0)));
        grid.AddNode(n1);
        grid.AddNode(n2);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(n1);
        ObjectRegistry.AddObject(n2);
        return (n1, n2);
    }

    private static GameObject MakePc(string name, Node at)
    {
        var o = GameObject.Create(name, isPc: true, privilege: Privilege.Player);
        ObjectRegistry.AddObject(o);
        o.IsConnected = true;
        o.Location = new LocationRef.CoordLocation(at.Coord);
        at.AddObject(o);
        o.ClearMessages();
        return o;
    }

    [Fact]
    public void LeaderMove_AutoMovesFollower()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = SetupNodes("snapfollow");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(["Leader"]));
        Assert.Contains(follower.Id, leader.FollowersSnapshot);
        Assert.True(leader.MoveTo(n2, toExit: "north"));
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
    }

    [Fact]
    public void NonColocatedFollower_StaysBehind()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = SetupNodes("snapstay");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(["Leader"]));
        Assert.True(follower.MoveTo(n2));
        follower.ClearMessages();
        Assert.True(leader.MoveTo(n2, toExit: "north"));
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)leader.Location).Coord);
    }
}
