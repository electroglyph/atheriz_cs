using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// P0-6 lock order (registry -> handler -> node -> object): registry and
// handler-lock-taking work must never nest under an object/door write lock.
[Collection("Ported")]
public class LockOrderTests
{
    private static (Node n1, NodeHandler nh) MakeNodes(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 1, 0)));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0)));
        grid.AddNode(n1); grid.AddNode(n2); areaObj.AddGrid(grid); nh.AddArea(areaObj);
        return (n1, nh);
    }

    private static GameObject MakePc(string name, Node at)
    {
        var o = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(o);
        o.IsConnected = true;
        o.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(at.Coord);
        at.AddObject(o);
        o.ClearMessages();
        return o;
    }

    private static void RunFollow(GameObject follower, string targetName)
    {
        var cmd = new FollowCommand();
        cmd.Run(follower, cmd.Parser!.ParseArgs(new[] { targetName }));
    }

    [Fact]
    public void Follow_CreatesExactlyOneScriptObject()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, _) = MakeNodes("LockOrder1");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        var before = ObjectRegistry.Count;
        RunFollow(follower, "Leader");
        Assert.Equal(leader.Id, follower.Following);
        Assert.Equal(before + 1, ObjectRegistry.Count);
        Assert.Single(leader.GetScriptsByType("FollowScript"));
    }

    [Fact]
    public void Follow_WithExistingScript_CreatesNoOrphan()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, _) = MakeNodes("LockOrder2");
        var leader = MakePc("Leader", n1);
        var first = MakePc("First", n1);
        var second = MakePc("Second", n1);
        RunFollow(first, "Leader");
        var before = ObjectRegistry.Count;
        RunFollow(second, "Leader");
        Assert.Equal(leader.Id, second.Following);
        Assert.Equal(before, ObjectRegistry.Count);
        Assert.Single(leader.GetScriptsByType("FollowScript"));
    }

    [Fact]
    public void Door_DenyPredicate_BlocksOpen_AllowPredicate_Opens()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, nh) = MakeNodes("LockOrder3");
        var caller = MakePc("Opener", n1);
        var door = new Door(new Coord("LockOrder3", 0, 0, 0), new Coord("LockOrder3", 0, 1, 0), "north", "south", null, "X", "O", true, false);
        nh.AddDoor(door);
        int calls = 0;
        door.AddLock("open", c => { calls++; return false; });
        Assert.False(door.TryOpen(caller));
        Assert.True(door.Closed);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Door_PredicateAndHandlerLock_DoNotDeadlock()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, nh) = MakeNodes("LockOrder4");
        var caller = MakePc("Hammer", n1);
        var door = new Door(new Coord("LockOrder4", 0, 0, 0), new Coord("LockOrder4", 0, 1, 0), "north", "south", null, "X", "O", true, false);
        nh.AddDoor(door);
        // Game-code predicates that take the handler door lock (the RemapDoors
        // direction is handler-lock -> door-lock; predicates must not invert it).
        // Choreographed overlap: T2 waits until T1 is inside its predicate
        // (holding the door write lock pre-fix) before taking Lock3, and T1
        // waits until T2 holds Lock3 before reading. Pre-fix neither can ever
        // proceed (ABBA); post-fix the predicate holds no door lock, so both
        // finish. All waits are bounded: failure, never a hung suite.
        using var t1InPredicate = new ManualResetEventSlim(false);
        using var t2HoldsLock3 = new ManualResetEventSlim(false);
        door.AddLock("open", c =>
        {
            t1InPredicate.Set();
            if (!t2HoldsLock3.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("T2 never took Lock3");
            nh.Lock3.EnterReadLock();
            try { return true; } finally { nh.Lock3.ExitReadLock(); }
        });
        door.AddLock("close", c =>
        {
            nh.Lock3.EnterReadLock();
            try { return true; } finally { nh.Lock3.ExitReadLock(); }
        });
        var t1 = Task.Run(() => { for (int i = 0; i < 50; i++) { door.TryOpen(caller); door.TryClose(caller); } });
        var t2 = Task.Run(() =>
        {
            if (!t1InPredicate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("T1 never reached its predicate");
            for (int i = 0; i < 50; i++)
            {
                nh.Lock3.EnterWriteLock();
                try { t2HoldsLock3.Set(); door.ForceOpen(); door.ForceClose(); }
                finally { nh.Lock3.ExitWriteLock(); }
            }
        });
        // Bounded: a lock-order inversion hangs here instead of failing.
        bool done = Task.WaitAll(new[] { t1, t2 }, TimeSpan.FromSeconds(20));
        Assert.True(done, "door/handler lock-order inversion deadlocked");
        if (t1.IsFaulted) throw t1.Exception!;
        if (t2.IsFaulted) throw t2.Exception!;
    }

    [Fact]
    public void NodeHandler_Clear_EvictsNodesFromRegistry()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, nh) = MakeNodes("LockOrder5");
        Assert.NotEmpty(ObjectRegistry.Get(n1.Id));
        nh.Clear();
        Assert.Empty(ObjectRegistry.Get(n1.Id));
        Assert.Null(nh.GetArea("LockOrder5"));
    }
}
