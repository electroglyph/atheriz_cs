// Port of atheriz/tests/test_follow.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedFollowTests
{
    private static (Node n1, Node n2, NodeHandler nh) SetupTestNodes(string area="TestArea")
    {
        var nh = new NodeHandler(); NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 1, 0)));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0)));
        grid.AddNode(n1); grid.AddNode(n2); areaObj.AddGrid(grid); nh.AddArea(areaObj);
        return (n1,n2,nh);
    }

    private static GameObject MakePc(string name, Node at, bool builder=false)
    {
        var o = GameObject.Create(name, isPc:true, privilege: builder? Privilege.Builder: Privilege.Player);
        ObjectRegistry.AddObject(o);
        o.IsConnected = true;
        o.Location = new Persistence.Dto.LocationRef.CoordLocation(at.Coord);
        at.AddObject(o);
        o.ClearMessages();
        return o;
    }

    private static GameObject FindByName(string name) => ObjectRegistry.FilterBy(o=>o.Name==name).First();

    [Fact]
    public void FollowCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var tup = SetupTestNodes("follow1");
        var n1 = tup.n1;
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(new[] { "Leader" }));
        Assert.Equal(leader.Id, follower.Following);
        Assert.Contains(follower.Id, leader.FollowersSnapshot);
        var scripts = leader.GetScriptsByType("FollowScript");
        Assert.Single(scripts);
    }

    [Fact]
    public void FollowMultipleFollowers()
    {
        using var env = GlobalTestEnv.Enter();
        var tup = SetupTestNodes("follow2");
        var n1 = tup.n1; var n2 = tup.n2;
        var leader = MakePc("Leader", n1);
        var f1 = MakePc("F1", n1);
        var f2 = MakePc("F2", n1);
        var cmd = new FollowCommand();
        cmd.Run(f1, cmd.Parser!.ParseArgs(new[] { "Leader" }));
        cmd.Run(f2, cmd.Parser!.ParseArgs(new[] { "Leader" }));
        Assert.Equal(2, leader.FollowersSnapshot.Count);
        Assert.Contains(f1.Id, leader.FollowersSnapshot);
        Assert.Contains(f2.Id, leader.FollowersSnapshot);
        // Verify FollowScript installed (Python creates FollowScript on leader)
        Assert.Single(leader.GetScriptsByType("FollowScript"));
        // Move the leader — FollowScript should auto-move colocated followers
        bool success = leader.MoveTo(n2, toExit: "north");
        Assert.True(success);
        // Followers should have moved automatically via FollowScript.at_post_move
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)f1.Location).Coord);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)f2.Location).Coord);
    }

    [Fact]
    public void NofollowCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var n1 = SetupTestNodes("follow3").n1;
        var leader = MakePc("Leader", n1);
        var f1 = MakePc("F1", n1);
        new FollowCommand().Run(f1, new FollowCommand().Parser!.ParseArgs(new[] { "Leader" }));
        Assert.Single(leader.FollowersSnapshot);
        Assert.Equal(leader.Id, f1.Following);
        var nofollow = new NofollowCommand();
        nofollow.Run(leader, null);
        Assert.True(leader.NoFollow);
        Assert.Empty(leader.FollowersSnapshot);
        Assert.Null(f1.Following);
        // Try following again when no_follow is True (non-builder should be blocked)
        f1.ClearMessages();
        new FollowCommand().Run(f1, new FollowCommand().Parser!.ParseArgs(new[] { "Leader" }));
        Assert.Contains(f1.PeekMessages(), m => m.Contains("will not lead"));
        // Toggle off
        nofollow.Run(leader, null);
        Assert.False(leader.NoFollow);
    }

    [Fact]
    public void CantFollowSelfOrNonexistent()
    {
        using var env = GlobalTestEnv.Enter();
        var n1 = SetupTestNodes("follow4").n1;
        var follower = MakePc("Follower", n1);
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(new[] { "Nobody" }));
        Assert.Contains(follower.PeekMessages(), m => m.Contains("Could not find"));
        follower.ClearMessages();
        cmd.Run(follower, cmd.Parser!.ParseArgs(new[] { "Follower" }));
        Assert.Contains(follower.PeekMessages(), m => m.Contains("can't follow yourself"));
        Assert.Null(follower.Following);
        Assert.Empty(follower.FollowersSnapshot);
    }

    [Fact]
    public void UnfollowCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var n1 = SetupTestNodes("follow5").n1;
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(new[] { "Leader" }));
        Assert.Equal(leader.Id, follower.Following);
        Assert.Contains(follower.Id, leader.FollowersSnapshot);
        new UnfollowCommand().Run(follower, null);
        Assert.Null(follower.Following);
        Assert.DoesNotContain(follower.Id, leader.FollowersSnapshot);
        Assert.Contains(follower.PeekMessages(), m => m.ToLowerInvariant().Contains("stop following"));
    }

    [Fact]
    public void UnfollowNotFollowing()
    {
        using var env = GlobalTestEnv.Enter();
        var n1 = SetupTestNodes("follow6").n1;
        var follower = MakePc("Follower", n1);
        new UnfollowCommand().Run(follower, null);
        Assert.Contains(follower.PeekMessages(), m => m.Contains("aren't following"));
    }
}
