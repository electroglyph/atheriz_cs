// Port of atheriz/tests/test_door_pathfind.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDoorPathfindTests
{
    private static (NodeHandler nh, Node n1, Node n2, Door door) SetupTwoNodes(bool closed, bool locked, Func<GameObject,bool>? accessMock = null, string area="PathArea")
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 1, 0, 0));
        n1.AddLink(new NodeLink("east", new Coord(area, 1, 0, 0), new List<string>{"e"}));
        n2.AddLink(new NodeLink("west", new Coord(area, 0, 0, 0), new List<string>{"w"}));
        grid.AddNode(n1); grid.AddNode(n2);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        var door = new Door(new Coord(area, 0, 0, 0), new Coord(area, 1, 0, 0), "east", "west", null, "", "", closed, locked);
        if (accessMock != null)
        {
            // Simplified: original used MagicMock side_effect for access; we use AddLock for "open" that records call
            // But we need per-perm mock; caller will provide delegate that checks perm via closure
            // For faithfulness, we add delegate that captures perm check via external counters
            // Here we treat accessMock as open check; for pathfind we need both open/unlock
            // Instead caller will add locks explicitly per test
            door.AddLock("open", accessMock);
        }
        // Ensure map handler for completeness
        var mh = new MapHandler(autoLoad:false);
        MapHandlerHolder.Set(mh);
        nh.AddDoor(door);
        NodeHandler.SetCurrent(nh);
        return (nh, n1, n2, door);
    }

    [Fact]
    public void TestDoorTryLockOpenFailsNotLocked()
    {
        using var env = GlobalTestEnv.Enter();
        var door = new Door(new Coord("A",0,0,0), new Coord("A",0,1,0), "north","south", null, "", "", false,false);
        var caller = GameObject.Create("caller", privilege: Privilege.Builder); ObjectRegistry.AddObject(caller);
        var loc = new Node(new Coord("A",0,0,0)); NodeHandler.GetCurrent()?.AddNode(loc);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(loc.Coord); loc.AddObject(caller);
        int accessCalls = 0;
        door.AddLock("lock", _ => { accessCalls++; return true; });
        var result = door.TryLock(caller);
        Assert.False(result);
        Assert.False(door.Locked);
        // Access was called via try_lock's Access("lock")
        Assert.True(accessCalls > 0);
    }

    [Fact]
    public void TestDoorTryLockOpenFailsEvenIfAccessTrue()
    {
        using var env = GlobalTestEnv.Enter();
        var door = new Door(new Coord("A",0,0,0), new Coord("A",0,1,0), "north","south", null, "", "", false,false);
        var caller = GameObject.Create("c2"); ObjectRegistry.AddObject(caller);
        var loc = new Node(new Coord("A",0,0,0)); NodeHandler.GetCurrent()?.AddNode(loc);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(loc.Coord); loc.AddObject(caller);
        int calls = 0;
        door.AddLock("lock", _=> { calls++; return true; });
        Assert.False(door.TryLock(caller));
        Assert.False(door.Locked);
        var door2 = new Door(new Coord("B",0,0,0), new Coord("B",1,0,0), "east","west", null, "", "", false,false);
        int calls2 = 0;
        door2.AddLock("lock", _=> { calls2++; return true; });
        var caller2 = GameObject.Create("c3"); ObjectRegistry.AddObject(caller2);
        var loc2 = new Node(new Coord("B",0,0,0)); NodeHandler.GetCurrent()?.AddNode(loc2);
        caller2.Location = new Persistence.Dto.LocationRef.CoordLocation(loc2.Coord); loc2.AddObject(caller2);
        Assert.False(door2.TryLock(caller2));
        Assert.True(calls >0 || calls2>0);
    }

    [Fact]
    public void TestDoorTryLockClosedSucceeds()
    {
        using var env = GlobalTestEnv.Enter();
        var door = new Door(new Coord("A",0,0,0), new Coord("A",0,1,0), "north","south", null, "", "", true,false);
        int calls = 0;
        door.AddLock("lock", _=> { calls++; return true; });
        var caller = GameObject.Create("c"); ObjectRegistry.AddObject(caller);
        var loc = new Node(new Coord("A",0,0,0)); NodeHandler.GetCurrent()?.AddNode(loc);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(loc.Coord); loc.AddObject(caller);
        Assert.True(door.TryLock(caller));
        Assert.True(door.Locked);
        Assert.True(calls>0);
    }

    [Fact]
    public void TestDoorTryLockClosedAlreadyLockedFails()
    {
        using var env = GlobalTestEnv.Enter();
        var door = new Door(new Coord("A",0,0,0), new Coord("A",0,1,0), "north","south", null, "", "", true,true);
        door.AddLock("lock", _=>true);
        var caller = GameObject.Create("c"); ObjectRegistry.AddObject(caller);
        var loc = new Node(new Coord("A",0,0,0)); NodeHandler.GetCurrent()?.AddNode(loc);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(loc.Coord); loc.AddObject(caller);
        Assert.False(door.TryLock(caller));
        Assert.True(door.Locked);
    }

    [Fact]
    public void TestDoorTryLockNoAccessFails()
    {
        using var env = GlobalTestEnv.Enter();
        var door = new Door(new Coord("A",0,0,0), new Coord("A",0,1,0), "north","south", null, "", "", true,false);
        int calls =0;
        door.AddLock("lock", _=> { calls++; return false; });
        var caller = GameObject.Create("c"); ObjectRegistry.AddObject(caller);
        var loc = new Node(new Coord("A",0,0,0)); NodeHandler.GetCurrent()?.AddNode(loc);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(loc.Coord); loc.AddObject(caller);
        Assert.False(door.TryLock(caller));
        Assert.False(door.Locked);
        Assert.True(calls>0);
    }

    private static GameObject MakeCaller() => PortedHelpers.MakeCaller();

    [Fact]
    public void TestPathfindOpenLockedStillTraversable()
    {
        using var env = GlobalTestEnv.Enter();
        // Original: access_mock returns False for any perm, but door is open so traversable
        int unlockCalls=0, openCalls=0;
        var (nh,n1,n2,door) = SetupTwoNodes(closed:false, locked:true);
        // Mock access to count calls and return False (but not used since closed false)
        door.AddLock("unlock", _=> { unlockCalls++; return false; });
        door.AddLock("open", _=> { openCalls++; return false; });
        var caller = MakeCaller();
        var (found, path, _) = Pathfind.AStar(n1, n2, caller, nh);
        Assert.True(found);
        Assert.Equal(2, path.Count);
        Assert.Equal(n1, path[0]); Assert.Equal(n2, path[1]);
        // For open door, access should not be checked (or checked but still traversable) — counts may be 0
        // Just verify traversable
    }

    [Fact]
    public void TestPathfindClosedLockedWithoutUnlockBlocks()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupTwoNodes(closed:true, locked:true);
        // access_mock: unlock->False, open->True
        int unlockCalls=0, openCalls=0;
        door.AddLock("unlock", _=> { unlockCalls++; return false; });
        door.AddLock("open", _=> { openCalls++; return true; });
        var caller = MakeCaller();
        var (found, _, _) = Pathfind.AStar(n1, n2, caller, nh);
        Assert.False(found);
        Assert.True(unlockCalls>0); // unlock was checked and failed
    }

    [Fact]
    public void TestPathfindClosedLockedWithUnlockAllows()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupTwoNodes(closed:true, locked:true);
        int unlockCalls=0, openCalls=0;
        door.AddLock("unlock", _=> { unlockCalls++; return true; });
        door.AddLock("open", _=> { openCalls++; return true; });
        var caller = MakeCaller();
        var (found, path, _) = Pathfind.AStar(n1, n2, caller, nh);
        Assert.True(found);
        Assert.Equal(2, path.Count);
        Assert.Equal(n2, path[1]);
        Assert.True(unlockCalls>0 && openCalls>0);
    }

    [Fact]
    public void TestPathfindClosedWithoutOpenBlocks()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupTwoNodes(closed:true, locked:false);
        int openCalls=0;
        door.AddLock("open", _=> { openCalls++; return false; });
        var caller = MakeCaller();
        var (found, _, _) = Pathfind.AStar(n1, n2, caller, nh);
        Assert.False(found);
        Assert.True(openCalls>0);
    }

    [Fact]
    public void TestPathfindClosedWithOpenAllows()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupTwoNodes(closed:true, locked:false);
        int openCalls=0;
        door.AddLock("open", _=> { openCalls++; return true; });
        var caller = MakeCaller();
        var (found, _, _) = Pathfind.AStar(n1, n2, caller, nh);
        Assert.True(found);
        Assert.True(openCalls>0);
    }

    [Fact]
    public void TestPathfindOpenLockedWithUnlockAndOpenTrueStillTraversable()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupTwoNodes(closed:false, locked:true);
        door.AddLock("unlock", _=>true); door.AddLock("open", _=>true);
        var caller = MakeCaller();
        var (found, _, _) = Pathfind.AStar(n1, n2, caller, nh);
        Assert.True(found);
    }

    [Fact]
    public void TestExitCommandOpenLockedAllowsMoveLegacy()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad:false); NodeHandler.SetCurrent(nh);
        var mh = new MapHandler(autoLoad:false); MapHandlerHolder.Set(mh);
        var area = new NodeArea("ExitArea");
        var grid = new NodeGrid("ExitArea", 0);
        var src = new Node(new Coord("ExitArea",0,0,0));
        var dst = new Node(new Coord("ExitArea",0,1,0));
        src.AddLink(new NodeLink("north", new Coord("ExitArea",0,1,0), new List<string>{"n"}));
        dst.AddLink(new NodeLink("south", new Coord("ExitArea",0,0,0), new List<string>{"s"}));
        grid.AddNode(src); grid.AddNode(dst); area.AddGrid(grid); nh.AddArea(area);
        var door = new Door(new Coord("ExitArea",0,0,0), new Coord("ExitArea",0,1,0), "north","south", null, "", "", false,true);
        nh.AddDoor(door);
        var player = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(player);
        player.Location = new Persistence.Dto.LocationRef.CoordLocation(src.Coord); src.AddObject(player);
        var cmd = new LoggedInExitCommand();
        cmd.CallerId = player.Id;
        cmd.Location = src.Coord;
        cmd.Destination = dst.Coord;
        cmd.ExitName = "north";
        cmd.Name = "north";
        cmd.DoorKey = "north";
        var prevMapEnabled = AtherizSettings.Global.MapEnabled;
        try
        {
            AtherizSettings.Global.MapEnabled = false;
            // Patch node handler already via SetCurrent
            cmd.DoMove();
        }
        finally { AtherizSettings.Global.MapEnabled = prevMapEnabled; }
        var loc = player.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.NotNull(loc);
        Assert.Equal(dst.Coord, loc!.Coord);
    }

    [Fact]
    public void TestExitCommandClosedLockedBlocksWithoutTryOpen()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad:false); NodeHandler.SetCurrent(nh);
        var mh = new MapHandler(autoLoad:false); MapHandlerHolder.Set(mh);
        var area = new NodeArea("ExitArea2");
        var grid = new NodeGrid("ExitArea2", 0);
        var src = new Node(new Coord("ExitArea2",0,0,0));
        var dst = new Node(new Coord("ExitArea2",0,1,0));
        src.AddLink(new NodeLink("north", new Coord("ExitArea2",0,1,0), new List<string>{"n"}));
        dst.AddLink(new NodeLink("south", new Coord("ExitArea2",0,0,0), new List<string>{"s"}));
        grid.AddNode(src); grid.AddNode(dst); area.AddGrid(grid); nh.AddArea(area);
        var door = new Door(new Coord("ExitArea2",0,0,0), new Coord("ExitArea2",0,1,0), "north","south", null, "", "", true,true);
        // Mock access to return False for any perm -> AddLock false for open and unlock
        int openCalls=0, unlockCalls=0;
        door.AddLock("open", _=> { openCalls++; return false; });
        door.AddLock("unlock", _=> { unlockCalls++; return false; });
        nh.AddDoor(door);
        var player = GameObject.Create("Hero2", isPc:true); ObjectRegistry.AddObject(player);
        player.Location = new Persistence.Dto.LocationRef.CoordLocation(src.Coord); src.AddObject(player);
        var cmd = new LoggedInExitCommand();
        cmd.CallerId = player.Id;
        cmd.Location = src.Coord;
        cmd.Destination = dst.Coord;
        cmd.ExitName = "north";
        cmd.Name = "north";
        var prev = AtherizSettings.Global.MapEnabled;
        try
        {
            AtherizSettings.Global.MapEnabled = false;
            cmd.DoMove();
        }
        finally { AtherizSettings.Global.MapEnabled = prev; }
        var loc = player.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.NotNull(loc);
        Assert.Equal(src.Coord, loc!.Coord);
    }
}
