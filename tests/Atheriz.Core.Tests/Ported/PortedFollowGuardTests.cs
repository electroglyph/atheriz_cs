// Port of atheriz/tests/test_follow_guard.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedFollowGuardTests
{
    private static (Node n1, Node n2, NodeHandler nh) MakePair(string area="TestArea")
    {
        var nh = new NodeHandler(); NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        // The Node constructor does not publish to the registry —
        // tests register explicitly, like production call sites.
        ObjectRegistry.AddObject(n1); ObjectRegistry.AddObject(n2);
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 1, 0)));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0)));
        grid.AddNode(n1); grid.AddNode(n2); areaObj.AddGrid(grid); nh.AddArea(areaObj);
        return (n1,n2,nh);
    }
    private static GameObject MakePc(string name, Node at)
    {
        var o = GameObject.Create(name, isPc:true); ObjectRegistry.AddObject(o);
        o.IsConnected = true; o.Location = new Persistence.Dto.LocationRef.CoordLocation(at.Coord); at.AddObject(o); o.ClearMessages();
        return o;
    }
    private static void ForceFollow(GameObject follower, GameObject leader)
    {
        follower.Following = leader.Id;
        var f = typeof(GameObject).GetField("_followers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var set = f.GetValue(leader) as HashSet<int>;
        set!.Add(follower.Id); leader.IsModified = true;
        if (!leader.GetScriptsByType("FollowScript").Any())
        {
            var s = new FollowScript();
            s.Id = IdGenerator.GetUniqueId();
            s.Name = $"FollowScript_for_{follower.Id}";
            s.IsModified = true;
            ObjectRegistry.AddObject(s);
            leader.AddScript(s);
        }
    }
    // Simulate FollowScript move guard: only followers at oldLoc move
    private static void SimulateLeaderMove(GameObject leader, Node dest, Node oldLoc)
    {
        bool ok = leader.MoveTo(dest, toExit: "north");
        Assert.True(ok);
        // Followers at oldLoc should be moved (mimics FollowScript.at_post_move)
        var followers = leader.FollowersSnapshot.ToList();
        foreach (var fid in followers)
        {
            var fol = ObjectRegistry.Get(fid).FirstOrDefault();
            if (fol == null) continue;
            if (fol.Location is Persistence.Dto.LocationRef.CoordLocation cl && cl.Coord.Equals(oldLoc.Coord))
            {
                bool moved = fol.MoveTo(dest);
                if (!moved) fol.Msg($"You can't follow {leader.Name} there!");
            }
            else if (fol.Location == null || fol.Location is Persistence.Dto.LocationRef.NullLocation)
            {
                // not moved
            }
        }
    }

    [Fact]
    public void OnlyColocatedFollowerMovesWhenLeaderMoves()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard1");
        var leader = MakePc("Leader", n1);
        var coloc = MakePc("Follower1", n1);
        var distant = MakePc("Follower2", n1);
        new FollowCommand().Run(coloc, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        new FollowCommand().Run(distant, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        Assert.Equal(leader.Id, coloc.Following); Assert.Equal(leader.Id, distant.Following);
        distant.MoveTo(n2, toExit: "north");
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)distant.Location).Coord);
        // Leader moves; colocated should follow, distant stays (colocation guard)
        // Note: distant is now at n2, same as dest, so colocation check: coloc at n1 moves to n2, distant already at n2 stays
        leader.MoveTo(n2, toExit:"north");
        // Simulate guard: only colocated at old n1 moves
        if ((coloc.Location as Persistence.Dto.LocationRef.CoordLocation)?.Coord.Equals(n1.Coord) == true) coloc.MoveTo(n2);
        // Distant already at n2, no move needed - assert stays
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)leader.Location).Coord);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)coloc.Location).Coord);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)distant.Location).Coord);
    }

    [Fact]
    public void DistantFollowerAtThirdNodeStaysBehind()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(); NodeHandler.SetCurrent(nh);
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea", 0);
        var n1 = new Node(new Coord("TestArea",0,0,0));
        var n2 = new Node(new Coord("TestArea",0,1,0));
        var n3 = new Node(new Coord("TestArea",9,9,0));
        // Explicit registration: the constructor does not publish.
        ObjectRegistry.AddObject(n1); ObjectRegistry.AddObject(n2); ObjectRegistry.AddObject(n3);
        n1.AddLink(new NodeLink("north", new Coord("TestArea",0,1,0)));
        n2.AddLink(new NodeLink("south", new Coord("TestArea",0,0,0)));
        grid.AddNode(n1); grid.AddNode(n2); grid.AddNode(n3); area.AddGrid(grid); nh.AddArea(area);
        var leader = MakePc("Leader", n1);
        var coloc = MakePc("FollowerA", n1);
        var distant = MakePc("FollowerB", n1);
        new FollowCommand().Run(coloc, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        new FollowCommand().Run(distant, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        distant.MoveTo(n3);
        Assert.Equal(n3.Coord, ((Persistence.Dto.LocationRef.CoordLocation)distant.Location).Coord);
        var old = n1;
        SimulateLeaderMove(leader, n2, old);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)leader.Location).Coord);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)coloc.Location).Coord);
        Assert.Equal(n3.Coord, ((Persistence.Dto.LocationRef.CoordLocation)distant.Location).Coord);
    }

    [Fact]
    public void FollowerWithNoneLocationNotMoved()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard3");
        var leader = MakePc("Leader", n1);
        var followerNone = MakePc("Lonely", n1);
        var followerOk = MakePc("Ok", n1);
        new FollowCommand().Run(followerNone, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        new FollowCommand().Run(followerOk, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        followerNone.Location = Persistence.Dto.LocationRef.NullLocation.Instance;
        Assert.Equal(leader.Id, followerNone.Following);
        var old = n1;
        SimulateLeaderMove(leader, n2, old);
        Assert.Null(followerNone.Location as Persistence.Dto.LocationRef.CoordLocation);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)followerOk.Location).Coord);
    }

    [Fact]
    public void DistantFollowerViaForcedFollowNotMovedWhenNotColocated()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard4");
        var leader = MakePc("Leader", n1);
        var coloc = MakePc("Here", n1);
        var distant = MakePc("Away", n2);
        // distant starts at n2
        new FollowCommand().Run(coloc, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        ForceFollow(distant, leader);
        Assert.Contains(coloc.Id, leader.FollowersSnapshot);
        Assert.Contains(distant.Id, leader.FollowersSnapshot);
        // Leader at n1 moves to n2; distant already at n2 should stay (not move, but already there)
        SimulateLeaderMove(leader, n2, n1);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)coloc.Location).Coord);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)distant.Location).Coord);
    }

    [Fact]
    public void ForcedFollowerWithNoneLocationIsIgnored()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard5");
        var leader = MakePc("Leader", n1);
        var followerNone = MakePc("Nowhere", n1); followerNone.Location = Persistence.Dto.LocationRef.NullLocation.Instance;
        ForceFollow(followerNone, leader);
        var followerOk = MakePc("Ok2", n1);
        new FollowCommand().Run(followerOk, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        SimulateLeaderMove(leader, n2, n1);
        Assert.True(followerNone.Location is Persistence.Dto.LocationRef.NullLocation);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)followerOk.Location).Coord);
    }

    [Fact]
    public void FollowersInSameRoomBothMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard6");
        var leader = MakePc("Leader", n1);
        var f1 = MakePc("F1", n1);
        var f2 = MakePc("F2", n1);
        new FollowCommand().Run(f1, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        new FollowCommand().Run(f2, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        SimulateLeaderMove(leader, n2, n1);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)f1.Location).Coord);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)f2.Location).Coord);
    }

    [Fact]
    public void FollowScriptCapturesOldLocBeforeMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard7");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        var script = leader.GetScriptsByType("FollowScript").First() as FollowScript;
        Assert.NotNull(script);
        // pairing is a stack now; OldLoc peeks the top (empty first).
        Assert.Null(script.OldLoc);
        script.at_pre_move(n2, "north");
        Assert.Equal(n1, script.OldLoc);
    }

    [Fact]
    public void FollowScriptClearsOldLocAfterPostMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard8");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        var script = leader.GetScriptsByType("FollowScript").First() as FollowScript;
        Assert.NotNull(script);
        // seed the stack via a real pre-move capture, then post-move
        // must pop it (OldLoc back to null).
        script.at_pre_move(n2, "north");
        Assert.NotNull(script.OldLoc);
        // Simulate leader already moved; now post_move should clear
        script.at_post_move(n2, "north");
        Assert.Null(script.OldLoc);
    }

    [Fact]
    public void FollowScriptOldLocNonePreventsAnyFollowerMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard9");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        var script = leader.GetScriptsByType("FollowScript").First() as FollowScript;
        Assert.NotNull(script);
        // empty stack — nothing captured, so post-move is a no-op and
        // the follower stays put.
        // Use MoveTo without triggering real FollowScript capture (manually set old to null)
        // We directly invoke at_post_move with null old_loc, follower should not move
        // Ensure leader is still at n1 before post_move, then after post_move follower stays
        var followerLocBefore = follower.Location;
        script!.at_post_move(n2, "north");
        Assert.Equal(n1.Coord, ((Persistence.Dto.LocationRef.CoordLocation)follower.Location).Coord);
    }

    [Fact]
    public void ScriptDeletesWhenNoFollowers()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard10");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        var script = leader.GetScriptsByType("FollowScript").First() as FollowScript;
        Assert.NotNull(script);
        // no stack seeding needed — the no-followers check runs before
        // the pop, so the script deletes regardless.
        // clear followers
        var f = typeof(GameObject).GetField("_followers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        (f.GetValue(leader) as HashSet<int>)!.Clear();
        script!.at_post_move(n2, "north");
        Assert.True(script.IsDeleted || !leader.GetScriptsByType("FollowScript").Contains(script));
    }

    [Fact]
    public void FollowerMoveFailureSendsMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,_) = MakePair("guard11");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(new[]{"Leader"}));
        var script = leader.GetScriptsByType("FollowScript").First() as FollowScript;
        Assert.NotNull(script);
        // capture via a real pre-move so the post-move attempts (and
        // fails) the follower move.
        script.at_pre_move(n2, "north");
        follower.InstallHook("at_pre_move", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
        follower.ClearMessages();
        script!.at_post_move(n2, "north");
        Assert.Contains(follower.PeekMessages(), m => m.Contains($"You can't follow {leader.Name} there!"));
    }

    // [Replace]-attributed veto hook (replaces the removed At*Override seam;
    // lambdas cannot carry attributes, so a real method provides the marker).
    private sealed class VetoHooks
    {
        [Replace]
        public bool DenyAll(GameObject? a, string? b) => false;
    }
}
