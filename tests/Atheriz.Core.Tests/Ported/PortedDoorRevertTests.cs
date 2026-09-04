// Port of atheriz/tests/test_door_revert.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDoorRevertTests
{
    private static (Node n1, Node n2, Door door, NodeHandler nh) SetupTwoNodesWithDoor(bool closed=true, string area="TestArea")
    {
        var nh = new NodeHandler(autoLoad:false); NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 0, 2, 0));
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 2, 0)));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0)));
        grid.AddNode(n1); grid.AddNode(n2); areaObj.AddGrid(grid); nh.AddArea(areaObj);
        var door = new Door(new Coord(area, 0, 0, 0), new Coord(area, 0, 2, 0), "north","south", (0,1), "X","O", closed,false);
        // Need MapHandler for door.map_close; create minimal handler and stash via holder
        var mh = new MapHandler(autoLoad:false);
        var miFrom = new MapInfo(area); miFrom.PostGrid[(0,1)] = "X"; // placeholder
        var miTo = miFrom;
        mh.SetMapInfo(area, 0, miFrom);
        MapHandlerHolder.Set(mh);
        GlobalServices.GetMapHandler(); // ensure singleton aligns? Instead set via holder
        // Patch node handler for door storage
        nh.AddDoor(door);
        return (n1,n2,door,nh);
    }

    private static (Node n1, Node n2, NodeHandler nh) SetupTwoNodesWithoutDoor(string area="TestArea")
    {
        var nh = new NodeHandler(autoLoad:false); NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 1, 0)));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0)));
        grid.AddNode(n1); grid.AddNode(n2); areaObj.AddGrid(grid); nh.AddArea(areaObj);
        // Clear doors
        // nh doors already empty
        var mh = new MapHandler(autoLoad:false);
        MapHandlerHolder.Set(mh);
        return (n1,n2,nh);
    }

    [Fact]
    public void TestDoorRemainsClosedAndMapRevertedWhenMoveFailsAfterOpen()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,door,nh) = SetupTwoNodesWithDoor(closed:true);
        Assert.True(door.Closed);
        var caller = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(caller);
        door.ResetCallCounts();
        // Mock: node2.at_pre_object_receive returns False -> MoveTo fails
        n2.AtPreObjectReceiveOverride = (src, exit) => false;
        var ex = new LoggedInExitCommand();
        ex.CallerId = caller.Id;
        ex.Location = n1.Coord;
        ex.Destination = n2.Coord;
        ex.ExitName = "north";
        // also set Name alias for faithfulness
        ex.Name = "north";
        ex.DoMove();
        Assert.True(door.Closed);
        var loc = caller.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.NotNull(loc);
        Assert.Equal(n1.Coord, loc!.Coord);
        Assert.NotEqual(n2.Coord, loc.Coord);
        Assert.True(door.TryCloseCallCount > 0 || door.MapCloseCallCount > 0);
    }

    [Fact]
    public void TestDoorClosesAfterSuccessfulMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,door,nh) = SetupTwoNodesWithDoor(closed:true);
        var caller = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(caller);
        door.ResetCallCounts();
        var ex = new LoggedInExitCommand();
        ex.CallerId = caller.Id;
        ex.Location = n1.Coord;
        ex.Destination = n2.Coord;
        ex.ExitName = "north";
        ex.DoMove();
        var loc = caller.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.NotNull(loc);
        Assert.Equal(n2.Coord, loc!.Coord);
        Assert.True(door.Closed);
    }

    [Fact]
    public void TestOpenDoorBranchDoesNotRevertOnMoveFailure()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,door,nh) = SetupTwoNodesWithDoor(closed:false);
        Assert.False(door.Closed);
        var caller = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(caller);
        door.ResetCallCounts();
        n2.AtPreObjectReceiveOverride = (s,e)=>false;
        var ex = new LoggedInExitCommand();
        ex.CallerId = caller.Id;
        ex.Location = n1.Coord;
        ex.Destination = n2.Coord;
        ex.ExitName = "north";
        ex.DoMove();
        Assert.False(door.Closed);
        var loc = caller.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.Equal(n1.Coord, loc!.Coord);
        Assert.Equal(0, door.TryCloseCallCount);
        Assert.Equal(0, door.MapCloseCallCount);
    }

    [Fact]
    public void TestOpenDoorBranchDoesNotRevertOnSuccess()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,door,nh) = SetupTwoNodesWithDoor(closed:false);
        Assert.False(door.Closed);
        var caller = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(caller);
        bool initial = door.Closed;
        var ex = new LoggedInExitCommand();
        ex.CallerId = caller.Id;
        ex.Location = n1.Coord;
        ex.Destination = n2.Coord;
        ex.ExitName = "north";
        ex.DoMove();
        Assert.Equal(initial, door.Closed);
        Assert.False(door.Closed);
        var loc = caller.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.Equal(n2.Coord, loc!.Coord);
    }

    [Fact]
    public void TestDoorTryCloseFallbackEnsuresClosedWhenTryCloseDenied()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,door,nh) = SetupTwoNodesWithDoor(closed:true);
        var caller = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(caller);
        n2.AtPreObjectReceiveOverride = (s,e)=>false;
        door.ResetCallCounts();
        // Mock try_close to return False via lock deny
        door.AddLock("close", _=>false);
        var ex = new LoggedInExitCommand();
        ex.CallerId = caller.Id;
        ex.Location = n1.Coord;
        ex.Destination = n2.Coord;
        ex.ExitName = "north";
        ex.DoMove();
        Assert.True(door.Closed);
        Assert.True(door.TryCloseCallCount > 0);
    }

    [Fact]
    public void TestDoorExceptionDuringMoveRevertsToClosed()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,door,nh) = SetupTwoNodesWithDoor(closed:true);
        var caller = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(caller);
        // Patch caller.move_to to throw RuntimeError("boom") via AtPreMoveOverride
        caller.AtPreMoveOverride = (dest, exit) => throw new InvalidOperationException("boom");
        var ex = new LoggedInExitCommand();
        ex.CallerId = caller.Id;
        ex.Location = n1.Coord;
        ex.Destination = n2.Coord;
        ex.ExitName = "north";
        bool raised = false;
        try { ex.DoMove(); Assert.Fail("should have raised"); } catch (InvalidOperationException) { raised = true; }
        Assert.True(raised);
        Assert.True(door.Closed);
    }

    [Fact]
    public void TestMoveWithoutDoorUnaffected()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1,n2,nh) = SetupTwoNodesWithoutDoor();
        var caller = GameObject.Create("Hero", isPc:true); ObjectRegistry.AddObject(caller);
        caller.IsConnected = true;
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(caller);
        // Ensure no doors
        var doors = nh.GetDoors(n1.Coord);
        // clear if any
        var ex = new LoggedInExitCommand();
        ex.CallerId = caller.Id;
        ex.Location = n1.Coord;
        ex.Destination = n2.Coord;
        ex.ExitName = "north";
        ex.DoMove();
        var loc = caller.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.Equal(n2.Coord, loc!.Coord);
    }
}
