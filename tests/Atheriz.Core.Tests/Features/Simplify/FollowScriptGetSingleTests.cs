using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// at_post_move resolves each follower id with a single lookup: a stale id
// (follower gone from the registry) is skipped exactly like the old
// empty-list branch, while live colocated followers still auto-move.
[Collection("Ported")]
public sealed class FollowScriptGetSingleTests
{
    private static (Node n1, Node n2, GameObject leader, GameObject follower) Setup(string area)
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
        GameObject MakePc(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.Location = LocationRef.FromCoord(n1.Coord);
            n1.AddObject(o);
            o.ClearMessages();
            return o;
        }
        return (n1, n2, MakePc("Leader"), MakePc("Follower"));
    }

    [Fact]
    public void PostMove_SkipsStaleFollowerId_MovesLiveFollower()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, n2, leader, follower) = Setup("stalefollow");
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(["Leader"]));
        // Stale id: created but never registered, so no object resolves.
        var ghost = GameObject.Create("Ghost");
        leader.AddFollower(ghost.Id);
        Assert.True(leader.MoveTo(n2, toExit: "north"));
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)leader.Location).Coord);
    }

    [Fact]
    public void PostMove_RemovedFollowerId_LeaderStillMoves()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, n2, leader, follower) = Setup("removedfollow");
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(["Leader"]));
        ObjectRegistry.RemoveObject(follower);
        Assert.True(leader.MoveTo(n2, toExit: "north"));
        Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)leader.Location).Coord);
    }
}
