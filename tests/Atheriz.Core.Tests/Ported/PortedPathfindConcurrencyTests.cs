// Port of atheriz/tests/test_pathfind_concurrency.py:1 — faithful 2 tests
using System.Threading;
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPathfindConcurrencyTests
{
    // test_pathfind_concurrency.py:19 test_get_doors_returns_copy_not_live
    [Fact] public void GetDoorsReturnsCopyNotLive() // test_pathfind_concurrency.py:19
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        NodeHandler.SetCurrent(nh);
        var coord = new Coord("TestCopy",0,0,0);
        var door = Door.Create(fromCoord:coord, fromExit:"north", toCoord:new Coord("TestCopy",0,1,0), toExit:"south", closed:false);
        // ensure clean
        // remove any existing at coord
        var existing = nh.GetDoors(coord);
        if (existing != null) foreach(var kv in existing.ToList()) { var d = kv.Value; nh.RemoveDoor(d); }
        nh.AddDoor(door);
        var d1 = nh.GetDoors(coord);
        Assert.NotNull(d1);
        Assert.True(d1!.ContainsKey("north"));
        // mutating returned dict must not affect handler
        d1.Remove("north");
        var d2 = nh.GetDoors(coord);
        Assert.True(d2!.ContainsKey("north"), "get_doors returned live dict, pop affected handler");
        // also check that new add_door doesn't affect previous snapshot
        var door2 = Door.Create(fromCoord:coord, fromExit:"east", toCoord:new Coord("TestCopy",1,0,0), toExit:"west", closed:false);
        nh.AddDoor(door2);
        Assert.False(d1.ContainsKey("east"));
        Assert.True(nh.GetDoors(coord)!.ContainsKey("east"));
        // cleanup
        nh.RemoveDoor(door);
        nh.RemoveDoor(door2);
    }

    // test_pathfind_concurrency.py:54 test_pathfind_no_torn_doors
    [Fact] public void PathfindNoTornDoors() // test_pathfind_concurrency.py:54
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("RaceArea");
        var grid = new NodeGrid("RaceArea", 0);
        foreach(var (x,y) in new[]{(0,0),(1,0),(2,0)}){
            var node = new Node(new Coord("RaceArea", x, y, 0));
            grid.Nodes[(x,y)] = node;
        }
        area.AddGrid(grid);
        nh.AddArea(area);
        var coordA = new Coord("RaceArea",0,0,0);
        var coordB = new Coord("RaceArea",1,0,0);
        var coordC = new Coord("RaceArea",2,0,0);
        var nodeA = nh.GetNode(coordA);
        var nodeB = nh.GetNode(coordB);
        var nodeC = nh.GetNode(coordC);
        Assert.NotNull(nodeA); Assert.NotNull(nodeB); Assert.NotNull(nodeC);
        nodeA!.AddLink(new NodeLink("east", coordB, new List<string>{"e"}));
        nodeB!.AddLink(new NodeLink("west", coordA, new List<string>{"w"}));
        nodeB.AddLink(new NodeLink("east", coordC, new List<string>{"e"}));
        nodeC!.AddLink(new NodeLink("west", coordB, new List<string>{"w"}));
        var door = Door.Create(fromCoord:coordA, fromExit:"east", toCoord:coordB, toExit:"west", closed:false);
        nh.AddDoor(door);
        var barrier = new Barrier(2);
        var errors = new List<string>();
        var stop = new ManualResetEventSlim(false);
        void DoorChurn()
        {
            try{
                barrier.SignalAndWait(5000);
                for(int i=0;i<50;i++){
                    if (stop.IsSet) break;
                    var d = Door.Create(fromCoord:coordB, fromExit:"east", toCoord:coordC, toExit:"west", closed:false);
                    nh.AddDoor(d);
                    lock(d.Lock){
                        // toggle via property under lock? Use Lock.EnterWriteLock
                        d.Lock.EnterWriteLock();
                        try { d.Closed = !d.Closed; } finally { d.Lock.ExitWriteLock(); }
                    }
                    nh.RemoveDoor(d);
                }
            } catch(Exception e){
                lock(errors){ errors.Add($"door_churn: {e}"); errors.Add(e.StackTrace ?? ""); }
            }
        }
        void PathfindLoop()
        {
            try{
                barrier.SignalAndWait(5000);
                for(int i=0;i<50;i++){
                    if (stop.IsSet) break;
                    var (ok, path, closed2) = Pathfind.AStar(nodeA!, nodeC!, null, nh);
                    Assert.IsType<List<Node>>(path);
                }
            } catch(Exception e){
                lock(errors){ errors.Add($"pathfind: {e}"); errors.Add(e.StackTrace ?? ""); }
            } finally { stop.Set(); }
        }
        var t1 = new Thread(DoorChurn);
        var t2 = new Thread(PathfindLoop);
        t1.Start(); t2.Start();
        t1.Join(5000);
        t2.Join(5000);
        Assert.Empty(errors);
        Assert.False(t1.IsAlive);
        Assert.False(t2.IsAlive);
        // cleanup
        nh.RemoveArea("RaceArea");
    }
}
