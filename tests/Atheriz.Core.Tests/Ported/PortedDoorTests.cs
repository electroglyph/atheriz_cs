// Port of atheriz/tests/test_door.py:1 — faithful 1:1 (45 tests split across two files, this file covers 1-23)
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDoorTests
{
    // Helpers — mirrors test_door.py fixtures
    private static GameObject MakeCaller(Node? location = null, bool isBuilder = true) => PortedHelpers.MakeCaller(location, isBuilder);

    private static GameObject MakeCallerWithCoord(Coord coord, bool isBuilder = true) => PortedHelpers.MakeCallerWithCoord(coord, isBuilder);

    private static GameArgumentParser.ParsedArgs MakeArgs(bool north=false,bool south=false,bool east=false,bool west=false,bool up=false,bool down=false,bool remove=false,bool auto=false)
    {
        var pa = new GameArgumentParser.ParsedArgs();
        pa["north"] = north;
        pa["south"] = south;
        pa["east"] = east;
        pa["west"] = west;
        pa["up"] = up;
        pa["down"] = down;
        pa["remove"] = remove;
        pa["auto"] = auto;
        pa["args"] = new List<string>();
        return pa;
    }

    private static (NodeHandler nh, NodeArea area, NodeGrid grid, Node startNode) SetupArea()
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea", 0);
        var startNode = new Node(new Coord("TestArea", 0, 0, 0));
        grid.Nodes[(0, 0)] = startNode;
        area.AddGrid(grid);
        nh.AddArea(area);
        // ensure startNode is in ObjectRegistry (Node ctor already adds) and handler
        return (nh, area, grid, startNode);
    }

    private static NodeHandler CreateNodeHandler()
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        return nh;
    }

    // ==================== Parser Tests ====================
    // test_door.py:99 test_door_command_attributes
    [Fact] public void DoorCommandAttributes() // test_door.py:99
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new DoorCommand();
        Assert.Equal("door", cmd.Key);
        Assert.Equal("Building", cmd.Category);
        Assert.True(cmd.UseParser);
    }

    // test_door.py:106 test_door_command_parser_setup
    [Fact] public void DoorCommandParserSetup() // test_door.py:106
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new DoorCommand();
        var parser = cmd.Parser;
        Assert.NotNull(parser);
        var parsed = parser!.ParseArgs(new List<string>{"-n","-a"});
        Assert.True(parsed.GetBool("north"));
        Assert.True(parsed.GetBool("auto"));
        Assert.False(parsed.GetBool("south"));
    }

    // test_door.py:117 test_door_parser_has_up_down
    [Fact] public void DoorParserHasUpDown() // test_door.py:117
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new DoorCommand();
        var parsed = cmd.Parser!.ParseArgs(new List<string>{"--up"});
        Assert.True(parsed.GetBool("up"));
        Assert.False(parsed.GetBool("down"));
        parsed = cmd.Parser!.ParseArgs(new List<string>{"--down"});
        Assert.True(parsed.GetBool("down"));
        Assert.False(parsed.GetBool("up"));
        parsed = cmd.Parser!.ParseArgs(new List<string>{"-r","-u"});
        Assert.True(parsed.GetBool("remove"));
        Assert.True(parsed.GetBool("up"));
    }

    // ==================== Error Handling Tests ====================
    // test_door.py:135 test_remove_without_direction
    [Fact] public void RemoveWithoutDirection() // test_door.py:135
    {
        using var env = GlobalTestEnv.Enter();
        var nh = CreateNodeHandler();
        var locNode = new Node(new Coord("TestArea", 0, 0, 0));
        var caller = MakeCaller(locNode);
        var cmd = new DoorCommand();
        var args = MakeArgs(remove:true);
        cmd.Run(caller, args);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("must specify a direction"));
    }

    // test_door.py:144 test_no_location
    [Fact] public void NoLocation() // test_door.py:144
    {
        using var env = GlobalTestEnv.Enter();
        var nh = CreateNodeHandler();
        var caller = MakeCaller(location:null);
        var cmd = new DoorCommand();
        var args = MakeArgs(north:true);
        cmd.Run(caller, args);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("invalid location"));
    }

    // test_door.py:153 test_create_north_no_dest_no_auto
    [Fact] public void CreateNorthNoDestNoAuto() // test_door.py:153
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        var cmd = new DoorCommand();
        var args = MakeArgs(north:true);
        cmd.Run(caller, args);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("no node at the destination"));
    }

    // ==================== Door Creation Tests (North) ====================
    // test_door.py:166 test_create_door_north_auto
    [Fact] public void CreateDoorNorthAuto() // test_door.py:166
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        var cmd = new DoorCommand();
        var args = MakeArgs(north:true, auto:true);
        cmd.Run(caller, args);
        // Destination node at y+2 should have been created
        var destNode = nh.GetNode(new Coord("TestArea", 0, 2, 0));
        Assert.NotNull(destNode);
        // Door should exist from both sides
        var doorsFrom = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        Assert.NotNull(doorsFrom);
        Assert.True(doorsFrom!.ContainsKey("north"));
        var doorsTo = nh.GetDoors(new Coord("TestArea", 0, 2, 0));
        Assert.NotNull(doorsTo);
        Assert.True(doorsTo!.ContainsKey("south"));
        // Same door object on both sides
        Assert.Same(doorsFrom["north"], doorsTo["south"]);
        var door = doorsFrom["north"];
        Assert.Equal(new Coord("TestArea", 0, 0, 0), door.FromCoord);
        Assert.Equal(new Coord("TestArea", 0, 2, 0), door.ToCoord);
        Assert.Equal("north", door.FromExit);
        Assert.Equal("south", door.ToExit);
        Assert.True(door.Closed);
    }

    // test_door.py:199 test_create_door_north_links
    [Fact] public void CreateDoorNorthLinks() // test_door.py:199
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        var cmd = new DoorCommand();
        cmd.Run(caller, MakeArgs(north:true, auto:true));
        var hereLinks = startNode.GetLinks();
        var northLinks = hereLinks.Where(l => l.Name == "north").ToList();
        Assert.Single(northLinks);
        Assert.Equal(new Coord("TestArea", 0, 2, 0), northLinks[0].Coord);
        var destNode = nh.GetNode(new Coord("TestArea", 0, 2, 0));
        Assert.NotNull(destNode);
        var destLinks = destNode!.GetLinks();
        var southLinks = destLinks.Where(l => l.Name == "south").ToList();
        Assert.Single(southLinks);
        Assert.Equal(new Coord("TestArea", 0, 0, 0), southLinks[0].Coord);
    }

    // test_door.py:221 test_create_door_north_with_existing_dest
    [Fact] public void CreateDoorNorthWithExistingDest() // test_door.py:221
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var destNode = new Node(new Coord("TestArea", 0, 2, 0));
        grid.Nodes[(0, 2)] = destNode;
        // also register destNode in handler via AddNode? But grid direct is enough, GetNode will find via area/grid
        ObjectRegistry.AddObject(destNode);
        var caller = MakeCaller(startNode);
        var cmd = new DoorCommand();
        cmd.Run(caller, MakeArgs(north:true));
        var doorsFrom = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        Assert.NotNull(doorsFrom);
        Assert.True(doorsFrom!.ContainsKey("north"));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Created door"));
    }

    // test_door.py:238 test_create_door_north_removes_door_node
    [Fact] public void CreateDoorNorthRemovesDoorNode() // test_door.py:238
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var doorCoordNode = new Node(new Coord("TestArea", 0, 1, 0));
        grid.Nodes[(0, 1)] = doorCoordNode;
        ObjectRegistry.AddObject(doorCoordNode);
        var destNode = new Node(new Coord("TestArea", 0, 2, 0));
        grid.Nodes[(0, 2)] = destNode;
        ObjectRegistry.AddObject(destNode);
        var caller = MakeCaller(startNode);
        var cmd = new DoorCommand();
        cmd.Run(caller, MakeArgs(north:true));
        Assert.Null(nh.GetNode(new Coord("TestArea", 0, 1, 0)));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Removed node"));
    }

    // test_door.py:258 test_create_door_north_symbol
    [Fact] public void CreateDoorNorthSymbol() // test_door.py:258
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        cmdRunNorthAutoAndAssertSymbol(nh, startNode, caller);
    }
    private static void cmdRunNorthAutoAndAssertSymbol(NodeHandler nh, Node startNode, GameObject caller)
    {
        var cmd = new DoorCommand();
        cmd.Run(caller, MakeArgs(north:true, auto:true));
        var settings = new AtherizSettings();
        var door = nh.GetDoors(new Coord("TestArea", 0, 0, 0))!["north"];
        Assert.Equal(settings.NsClosedDoor, door.ClosedSymbol);
        Assert.Equal(settings.NsOpenDoor1, door.OpenSymbol);
        Assert.Equal((0, 1), door.SymbolCoord);
    }

    // test_door.py:275 test_create_door_south_auto
    [Fact] public void CreateDoorSouthAuto() // test_door.py:275
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        var cmd = new DoorCommand();
        cmd.Run(caller, MakeArgs(south:true, auto:true));
        var destNode = nh.GetNode(new Coord("TestArea", 0, -2, 0));
        Assert.NotNull(destNode);
        var doorsFrom = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        Assert.True(doorsFrom!.ContainsKey("south"));
        var doorsTo = nh.GetDoors(new Coord("TestArea", 0, -2, 0));
        Assert.True(doorsTo!.ContainsKey("north"));
        var door = doorsFrom["south"];
        Assert.Equal("south", door.FromExit);
        Assert.Equal("north", door.ToExit);
        Assert.Equal((0, -1), door.SymbolCoord);
        var settings = new AtherizSettings();
        Assert.Equal(settings.NsClosedDoor, door.ClosedSymbol);
    }

    // test_door.py:299 test_create_door_south_links
    [Fact] public void CreateDoorSouthLinks() // test_door.py:299
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        new DoorCommand().Run(caller, MakeArgs(south:true, auto:true));
        var hereLinks = startNode.GetLinks();
        var southLinks = hereLinks.Where(l => l.Name == "south").ToList();
        Assert.Single(southLinks);
        Assert.Equal(new Coord("TestArea", 0, -2, 0), southLinks[0].Coord);
        var destNode = nh.GetNode(new Coord("TestArea", 0, -2, 0));
        Assert.NotNull(destNode);
        var destLinks = destNode!.GetLinks();
        var northLinks = destLinks.Where(l => l.Name == "north").ToList();
        Assert.Single(northLinks);
        Assert.Equal(new Coord("TestArea", 0, 0, 0), northLinks[0].Coord);
    }

    // test_door.py:321 test_create_door_east_auto
    [Fact] public void CreateDoorEastAuto() // test_door.py:321
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        new DoorCommand().Run(caller, MakeArgs(east:true, auto:true));
        var destNode = nh.GetNode(new Coord("TestArea", 2, 0, 0));
        Assert.NotNull(destNode);
        var doorsFrom = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        Assert.True(doorsFrom!.ContainsKey("east"));
        var doorsTo = nh.GetDoors(new Coord("TestArea", 2, 0, 0));
        Assert.True(doorsTo!.ContainsKey("west"));
        var door = doorsFrom["east"];
        Assert.Equal("east", door.FromExit);
        Assert.Equal("west", door.ToExit);
        Assert.Equal((1, 0), door.SymbolCoord);
        var settings = new AtherizSettings();
        Assert.Equal(settings.EwClosedDoor, door.ClosedSymbol);
        Assert.Equal(settings.EwOpenDoor1, door.OpenSymbol);
    }

    // test_door.py:346 test_create_door_east_links
    [Fact] public void CreateDoorEastLinks() // test_door.py:346
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        new DoorCommand().Run(caller, MakeArgs(east:true, auto:true));
        var hereLinks = startNode.GetLinks();
        var eastLinks = hereLinks.Where(l => l.Name == "east").ToList();
        Assert.Single(eastLinks);
        Assert.Equal(new Coord("TestArea", 2, 0, 0), eastLinks[0].Coord);
    }

    // test_door.py:362 test_create_door_west_auto
    [Fact] public void CreateDoorWestAuto() // test_door.py:362
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        new DoorCommand().Run(caller, MakeArgs(west:true, auto:true));
        var destNode = nh.GetNode(new Coord("TestArea", -2, 0, 0));
        Assert.NotNull(destNode);
        var doorsFrom = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        Assert.True(doorsFrom!.ContainsKey("west"));
        var doorsTo = nh.GetDoors(new Coord("TestArea", -2, 0, 0));
        Assert.True(doorsTo!.ContainsKey("east"));
        var door = doorsFrom["west"];
        Assert.Equal("west", door.FromExit);
        Assert.Equal("east", door.ToExit);
        Assert.Equal((-1, 0), door.SymbolCoord);
        var settings = new AtherizSettings();
        Assert.Equal(settings.EwClosedDoor, door.ClosedSymbol);
        Assert.Equal(settings.EwOpenDoor2, door.OpenSymbol);
    }

    // test_door.py:387 test_create_door_west_links
    [Fact] public void CreateDoorWestLinks() // test_door.py:387
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        new DoorCommand().Run(caller, MakeArgs(west:true, auto:true));
        var hereLinks = startNode.GetLinks();
        var westLinks = hereLinks.Where(l => l.Name == "west").ToList();
        Assert.Single(westLinks);
        Assert.Equal(new Coord("TestArea", -2, 0, 0), westLinks[0].Coord);
    }

    // test_door.py:403 test_remove_door_north
    [Fact] public void RemoveDoorNorth() // test_door.py:403
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var cmd = new DoorCommand();
        var caller = MakeCaller(startNode);
        cmd.Run(caller, MakeArgs(north:true, auto:true));
        Assert.NotNull(nh.GetDoors(new Coord("TestArea", 0, 0, 0)));
        Assert.True(nh.GetDoors(new Coord("TestArea", 0, 0, 0))!.ContainsKey("north"));
        caller.ClearMessages();
        cmd.Run(caller, MakeArgs(remove:true, north:true));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Removed"));
        var doors = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        if (doors != null) Assert.False(doors.ContainsKey("north"));
    }

    // test_door.py:426 test_remove_door_south
    [Fact] public void RemoveDoorSouth() // test_door.py:426
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var cmd = new DoorCommand();
        var caller = MakeCaller(startNode);
        cmd.Run(caller, MakeArgs(south:true, auto:true));
        caller.ClearMessages();
        cmd.Run(caller, MakeArgs(remove:true, south:true));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Removed"));
        var doors = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        if (doors != null) Assert.False(doors.ContainsKey("south"));
    }

    // test_door.py:442 test_remove_door_east
    [Fact] public void RemoveDoorEast() // test_door.py:442
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var cmd = new DoorCommand();
        var caller = MakeCaller(startNode);
        cmd.Run(caller, MakeArgs(east:true, auto:true));
        caller.ClearMessages();
        cmd.Run(caller, MakeArgs(remove:true, east:true));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Removed"));
        var doors = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        if (doors != null) Assert.False(doors.ContainsKey("east"));
    }

    // test_door.py:458 test_remove_door_west
    [Fact] public void RemoveDoorWest() // test_door.py:458
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var cmd = new DoorCommand();
        var caller = MakeCaller(startNode);
        cmd.Run(caller, MakeArgs(west:true, auto:true));
        caller.ClearMessages();
        cmd.Run(caller, MakeArgs(remove:true, west:true));
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Removed"));
        var doors = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        if (doors != null) Assert.False(doors.ContainsKey("west"));
    }

    // test_door.py:474 test_create_door_up_auto
    [Fact] public void CreateDoorUpAuto() // test_door.py:474
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, area, grid, startNode) = SetupArea();
        var caller = MakeCaller(startNode);
        new DoorCommand().Run(caller, MakeArgs(up:true, auto:true));
        var destNode = nh.GetNode(new Coord("TestArea", 0, 0, 2));
        Assert.NotNull(destNode);
        var doorsFrom = nh.GetDoors(new Coord("TestArea", 0, 0, 0));
        Assert.True(doorsFrom!.ContainsKey("up"));
        var doorsTo = nh.GetDoors(new Coord("TestArea", 0, 0, 2));
        Assert.True(doorsTo!.ContainsKey("down"));
        Assert.Same(doorsFrom["up"], doorsTo["down"]);
        var door = doorsFrom["up"];
        Assert.Equal(new Coord("TestArea", 0, 0, 0), door.FromCoord);
        Assert.Equal(new Coord("TestArea", 0, 0, 2), door.ToCoord);
        Assert.Equal("up", door.FromExit);
        Assert.Equal("down", door.ToExit);
        Assert.True(door.Closed);
        var hereLinks = startNode.GetLinks();
        var upLinks = hereLinks.Where(l => l.Name == "up").ToList();
        Assert.Single(upLinks);
        Assert.Equal(new Coord("TestArea", 0, 0, 2), upLinks[0].Coord);
        var destLinks = destNode!.GetLinks();
        var downLinks = destLinks.Where(l => l.Name == "down").ToList();
        Assert.Single(downLinks);
        Assert.Equal(new Coord("TestArea", 0, 0, 0), downLinks[0].Coord);
    }
}
