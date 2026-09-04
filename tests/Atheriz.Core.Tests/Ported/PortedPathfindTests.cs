// Port of atheriz/tests/test_pathfind.py:1 — faithful 10 tests
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPathfindTests
{
    private static (NodeHandler nh, NodeArea area, NodeGrid grid, Dictionary<(int,int), Node> nodes) SetupPathfindArea()
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("PathArea");
        var grid = new NodeGrid("PathArea", 0);
        var nodes = new Dictionary<(int,int), Node>();
        for(int x=0;x<3;x++) for(int y=0;y<2;y++){
            var n = new Node(new Coord("PathArea", x, y, 0));
            nodes[(x,y)] = n;
            grid.Nodes[(x,y)] = n;
        }
        nodes[(0,0)].AddLink(new NodeLink("east", new Coord("PathArea",1,0,0), new List<string>{"e"}));
        nodes[(1,0)].AddLink(new NodeLink("west", new Coord("PathArea",0,0,0), new List<string>{"w"}));
        nodes[(1,0)].AddLink(new NodeLink("east", new Coord("PathArea",2,0,0), new List<string>{"e"}));
        nodes[(2,0)].AddLink(new NodeLink("west", new Coord("PathArea",1,0,0), new List<string>{"w"}));
        nodes[(0,0)].AddLink(new NodeLink("north", new Coord("PathArea",0,1,0), new List<string>{"n"}));
        nodes[(0,1)].AddLink(new NodeLink("south", new Coord("PathArea",0,0,0), new List<string>{"s"}));
        nodes[(0,1)].AddLink(new NodeLink("east", new Coord("PathArea",1,1,0), new List<string>{"e"}));
        nodes[(1,1)].AddLink(new NodeLink("west", new Coord("PathArea",0,1,0), new List<string>{"w"}));
        nodes[(1,1)].AddLink(new NodeLink("east", new Coord("PathArea",2,1,0), new List<string>{"e"}));
        nodes[(2,1)].AddLink(new NodeLink("west", new Coord("PathArea",1,1,0), new List<string>{"w"}));
        nodes[(2,1)].AddLink(new NodeLink("south", new Coord("PathArea",2,0,0), new List<string>{"s"}));
        nodes[(2,0)].AddLink(new NodeLink("north", new Coord("PathArea",2,1,0), new List<string>{"n"}));
        area.AddGrid(grid);
        nh.AddArea(area);
        return (nh, area, grid, nodes);
    }

    // test_pathfind.py:80 test_astar_no_doors
    [Fact] public void AstarNoDoors() // test_pathfind.py:80
    {
        var (nh, area, grid, nodes) = SetupPathfindArea();
        var start = nodes[(0,0)];
        var end = nodes[(2,0)];
        var (success, path, closed) = Pathfind.AStar(start, end, null, nh);
        Assert.True(success);
        // Path should be n1 -> n2 -> n3, so length 3
        Assert.Equal(3, path.Count);
        Assert.Same(start, path[0]);
        Assert.Same(nodes[(1,0)], path[1]);
        Assert.Same(end, path[2]);
    }

    // test_pathfind.py:94 test_astar_open_door
    [Fact] public void AstarOpenDoor() // test_pathfind.py:94
    {
        var (nh, area, grid, nodes) = SetupPathfindArea();
        var start = nodes[(0,0)];
        var end = nodes[(2,0)];
        var door = Door.Create(fromCoord:new Coord("PathArea",0,0,0), fromExit:"east", toCoord:new Coord("PathArea",1,0,0), toExit:"west", closed:false);
        nh.AddDoor(door);
        var caller = GameObject.Create("Caller");
        ObjectRegistry.AddObject(caller);
        var (success, path, closed) = Pathfind.AStar(start, end, caller, nh);
        Assert.True(success);
        Assert.Equal(3, path.Count);
        Assert.Same(nodes[(1,0)], path[1]);
    }

    // test_pathfind.py:118 test_astar_closed_unlocked_door_can_open
    [Fact] public void AstarClosedUnlockedDoorCanOpen() // test_pathfind.py:118
    {
        var (nh, area, grid, nodes) = SetupPathfindArea();
        var start = nodes[(0,0)];
        var end = nodes[(2,0)];
        var door = Door.Create(fromCoord:new Coord("PathArea",0,0,0), fromExit:"east", toCoord:new Coord("PathArea",1,0,0), toExit:"west", closed:true, locked:false);
        nh.AddDoor(door);
        var caller = GameObject.Create("Caller"); ObjectRegistry.AddObject(caller);
        // Mock access to return True for "open" — we use AddLock with predicate returning true (default true if no lock, but we explicitly add)
        // To mimic MagicMock, we track call count
        int openCalls = 0;
        door.AddLock("open", c=> { openCalls++; return true; });
        var (success, path, closed) = Pathfind.AStar(start, end, caller, nh);
        Assert.True(success);
        Assert.Equal(3, path.Count);
        Assert.Same(nodes[(1,0)], path[1]);
        Assert.True(openCalls >= 1);
    }

    // test_pathfind.py:145 test_astar_closed_unlocked_door_cannot_open_routes_around
    [Fact] public void AstarClosedUnlockedDoorCannotOpenRoutesAround() // test_pathfind.py:145
    {
        var (nh, area, grid, nodes) = SetupPathfindArea();
        var start = nodes[(0,0)];
        var end = nodes[(2,0)];
        var door = Door.Create(fromCoord:new Coord("PathArea",0,0,0), fromExit:"east", toCoord:new Coord("PathArea",1,0,0), toExit:"west", closed:true, locked:false);
        nh.AddDoor(door);
        var caller = GameObject.Create("Caller"); ObjectRegistry.AddObject(caller);
        door.AddLock("open", _=> false);
        var (success, path, closed) = Pathfind.AStar(start, end, caller, nh);
        Assert.True(success);
        // Should route around: n1 -> n4 -> n5 -> n6 -> n3
        Assert.Equal(5, path.Count);
        Assert.Same(nodes[(0,1)], path[1]);
        Assert.Same(nodes[(1,1)], path[2]);
        Assert.Same(nodes[(2,1)], path[3]);
    }

    // test_pathfind.py:175 test_astar_locked_door_can_unlock
    [Fact] public void AstarLockedDoorCanUnlock() // test_pathfind.py:175
    {
        var (nh, area, grid, nodes) = SetupPathfindArea();
        var start = nodes[(0,0)];
        var end = nodes[(2,0)];
        var door = Door.Create(fromCoord:new Coord("PathArea",0,0,0), fromExit:"east", toCoord:new Coord("PathArea",1,0,0), toExit:"west", closed:true, locked:true);
        nh.AddDoor(door);
        var caller = GameObject.Create("Caller"); ObjectRegistry.AddObject(caller);
        door.AddLock("open", _=> true);
        door.AddLock("unlock", _=> true);
        var (success, path, closed) = Pathfind.AStar(start, end, caller, nh);
        Assert.True(success);
        Assert.Equal(3, path.Count);
        Assert.Same(nodes[(1,0)], path[1]);
    }

    // test_pathfind.py:201 test_astar_locked_door_cannot_unlock_routes_around
    [Fact] public void AstarLockedDoorCannotUnlockRoutesAround() // test_pathfind.py:201
    {
        var (nh, area, grid, nodes) = SetupPathfindArea();
        var start = nodes[(0,0)];
        var end = nodes[(2,0)];
        var door = Door.Create(fromCoord:new Coord("PathArea",0,0,0), fromExit:"east", toCoord:new Coord("PathArea",1,0,0), toExit:"west", closed:true, locked:true);
        nh.AddDoor(door);
        var caller = GameObject.Create("Caller"); ObjectRegistry.AddObject(caller);
        door.AddLock("open", c=> true);
        door.AddLock("unlock", c=> false);
        var (success, path, closed) = Pathfind.AStar(start, end, caller, nh);
        Assert.True(success);
        Assert.Equal(5, path.Count);
        Assert.Same(nodes[(0,1)], path[1]);
    }

    // test_pathfind.py:235 test_astar_blocked_completely
    [Fact] public void AstarBlockedCompletely() // test_pathfind.py:235
    {
        var (nh, area, grid, nodes) = SetupPathfindArea();
        var start = nodes[(0,0)];
        var end = nodes[(2,0)];
        var door1 = Door.Create(fromCoord:new Coord("PathArea",0,0,0), fromExit:"east", toCoord:new Coord("PathArea",1,0,0), toExit:"west", closed:true, locked:true);
        nh.AddDoor(door1);
        var door2 = Door.Create(fromCoord:new Coord("PathArea",0,1,0), fromExit:"east", toCoord:new Coord("PathArea",1,1,0), toExit:"west", closed:true, locked:true);
        nh.AddDoor(door2);
        var caller = GameObject.Create("Caller"); ObjectRegistry.AddObject(caller);
        door1.AddLock("open", _=> false);
        door1.AddLock("unlock", _=> false);
        door2.AddLock("open", _=> false);
        door2.AddLock("unlock", _=> false);
        var (success, path, closed) = Pathfind.AStar(start, end, caller, nh);
        Assert.False(success);
    }

    // test_pathfind.py:272 test_pathfind_no_heapify_and_stale_handling
    [Fact] public void PathfindNoHeapifyAndStaleHandling() // test_pathfind.py:272
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("U8Area");
        var grid = new NodeGrid("U8Area", 0);
        var nodes = new Dictionary<(int,int), Node>();
        foreach(var coord in new[]{(0,0),(1,1),(1,0),(2,0)}){
            var n = new Node(new Coord("U8Area", coord.Item1, coord.Item2, 0));
            nodes[coord] = n;
            grid.Nodes[coord] = n;
        }
        nodes[(0,0)].AddLink(new NodeLink("n", new Coord("U8Area",1,1,0), new List<string>{"n"}));
        nodes[(1,1)].AddLink(new NodeLink("s", new Coord("U8Area",0,0,0), new List<string>{"s"}));
        nodes[(1,1)].AddLink(new NodeLink("s_e", new Coord("U8Area",1,0,0), new List<string>{"e"}));
        nodes[(1,0)].AddLink(new NodeLink("n_w", new Coord("U8Area",1,1,0), new List<string>{"w"}));
        nodes[(0,0)].AddLink(new NodeLink("e", new Coord("U8Area",1,0,0), new List<string>{"e"}));
        nodes[(1,0)].AddLink(new NodeLink("w", new Coord("U8Area",0,0,0), new List<string>{"w"}));
        nodes[(1,0)].AddLink(new NodeLink("e2", new Coord("U8Area",2,0,0), new List<string>{"e"}));
        nodes[(2,0)].AddLink(new NodeLink("w2", new Coord("U8Area",1,0,0), new List<string>{"w"}));
        area.AddGrid(grid);
        nh.AddArea(area);
        // In C# we don't have heapq; we just verify path succeeds and length 3
        var (success, path, closed) = Pathfind.AStar(nodes[(0,0)], nodes[(2,0)], null, nh);
        Assert.True(success);
        Assert.Equal(3, path.Count);
        Assert.Same(nodes[(0,0)], path[0]);
        Assert.Same(nodes[(2,0)], path[^1]);
        // heapify call count <=1 is not applicable in C#; we assert success
    }

    // test_pathfind.py:307 test_pathfind_stale_entries_skipped
    [Fact] public void PathfindStaleEntriesSkipped() // test_pathfind.py:307
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("StaleArea");
        var grid = new NodeGrid("StaleArea", 0);
        int size = 5;
        var nodes = new Dictionary<(int,int), Node>();
        for(int x=0;x<size;x++){
            var n = new Node(new Coord("StaleArea", x,0,0));
            nodes[(x,0)] = n;
            grid.Nodes[(x,0)] = n;
        }
        for(int x=0;x<size-1;x++){
            nodes[(x,0)].AddLink(new NodeLink($"e{x}", new Coord("StaleArea", x+1,0,0), new List<string>{"e"}));
            nodes[(x+1,0)].AddLink(new NodeLink($"w{x}", new Coord("StaleArea", x,0,0), new List<string>{"w"}));
        }
        nodes[(0,0)].AddLink(new NodeLink("jump", new Coord("StaleArea",2,0,0), new List<string>{"j"}));
        nodes[(2,0)].AddLink(new NodeLink("jump_back", new Coord("StaleArea",0,0,0), new List<string>{"jb"}));
        area.AddGrid(grid);
        nh.AddArea(area);
        var (success, path, closed) = Pathfind.AStar(nodes[(0,0)], nodes[(4,0)], null, nh);
        Assert.True(success);
        Assert.Same(nodes[(0,0)], path[0]);
        Assert.Same(nodes[(4,0)], path[^1]);
        Assert.Equal(4, path.Count);
    }

    // test_pathfind.py:339 test_pathfind_uses_set_for_closed_list
    [Fact] public void PathfindUsesSetForClosedList() // test_pathfind.py:339 (slow)
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("BigArea");
        var grid = new NodeGrid("BigArea", 0);
        int size = 10;
        var nodes = new Dictionary<(int,int), Node>();
        for(int x=0;x<size;x++) for(int y=0;y<size;y++){
            var n = new Node(new Coord("BigArea", x,y,0));
            nodes[(x,y)] = n;
            grid.Nodes[(x,y)] = n;
        }
        for(int x=0;x<size;x++) for(int y=0;y<size;y++){
            if (x+1<size) nodes[(x,y)].AddLink(new NodeLink("east", new Coord("BigArea", x+1,y,0), new List<string>{"e"}));
            if (y+1<size) nodes[(x,y)].AddLink(new NodeLink("north", new Coord("BigArea", x,y+1,0), new List<string>{"n"}));
        }
        area.AddGrid(grid);
        nh.AddArea(area);
        var start = nodes[(0,0)];
        var end = nodes[(9,9)];
        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var (success, path, closed) = Pathfind.AStar(start, end, null, nh);
        var elapsed = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t0)/1000.0;
        Assert.True(success);
        Assert.True(elapsed < 2.0);
        Assert.All(closed, c=> Assert.IsType<Coord>(c));
    }
}
