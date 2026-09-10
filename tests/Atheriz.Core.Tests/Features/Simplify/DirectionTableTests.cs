// Pins for the direction-flag tables (DoorDirectionCommand row order +
// per-row wording, DoorCommand removal order): the table loops must reproduce
// the old if-chains exactly, with opposite pairs n<->s, e<->w, u<->d.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class DirectionTableTests
{
    private static GameObject MakeBuilderIn(Node node)
    {
        var c = GameObject.Create("Builder", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(c);
        c.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static Node MakeRoom(string area, int x, int y)
    {
        var n = new Node(new Coord(area, x, y, 0));
        ObjectRegistry.AddObject(n);
        return n;
    }

    [Fact]
    public void DoorDirection_MissingMessages_KeepPerRowWording()
    {
        using var env = GlobalTestEnv.Enter();
        NodeHandler.SetCurrent(new NodeHandler());
        var room = MakeRoom("dirtable1", 0, 0);
        var c = MakeBuilderIn(room);
        // up/down rows carry their own wording ("no door up", no "to the").
        new OpenCommand().Run(c, new OpenCommand().Parser!.ParseArgs(["-u"]));
        Assert.Contains(c.PeekMessages(), m => m == "There is no door up.");
        c.ClearMessages();
        new OpenCommand().Run(c, new OpenCommand().Parser!.ParseArgs(["-d"]));
        Assert.Contains(c.PeekMessages(), m => m == "There is no door down.");
        c.ClearMessages();
        new OpenCommand().Run(c, new OpenCommand().Parser!.ParseArgs(["-n"]));
        Assert.Contains(c.PeekMessages(), m => m == "There is no door to the north.");
    }

    [Fact]
    public void DoorDirection_MultipleFlags_ActInTableOrder()
    {
        using var env = GlobalTestEnv.Enter();
        NodeHandler.SetCurrent(new NodeHandler());
        var room = MakeRoom("dirtable2", 0, 0);
        var c = MakeBuilderIn(room);
        new OpenCommand().Run(c, new OpenCommand().Parser!.ParseArgs(["-d", "-n"]));
        var msgs = c.PeekMessages().Where(m => m.StartsWith("There is no door", StringComparison.Ordinal)).ToList();
        Assert.Equal(["There is no door to the north.", "There is no door down."], msgs);
    }

    [Fact]
    public void Build_DirectionsTable_OppositePairsAndDeltasAgree()
    {
        var d = BuildCommand.Directions;
        Assert.Equal(("south", "north"), (d["n"].back, d["s"].back));
        Assert.Equal(("north", "south"), (d["s"].back, d["n"].back));
        Assert.Equal(("west", "east"), (d["e"].back, d["w"].back));
        Assert.Equal(("east", "west"), (d["w"].back, d["e"].back));
        Assert.Equal(("down", "up"), (d["u"].back, d["d"].back));
        Assert.Equal(("up", "down"), (d["d"].back, d["u"].back));
        // Deltas are negated across each pair.
        Assert.Equal((0, 1, 0), (d["n"].dx, d["n"].dy, d["n"].dz));
        Assert.Equal((0, -1, 0), (d["s"].dx, d["s"].dy, d["s"].dz));
        Assert.Equal((1, 0, 0), (d["e"].dx, d["e"].dy, d["e"].dz));
        Assert.Equal((-1, 0, 0), (d["w"].dx, d["w"].dy, d["w"].dz));
    }

    [Fact]
    public void DoorRemove_MultipleFlags_RemovedInTableOrder()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        const string area = "dirtable3";
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var room = MakeRoom(area, 0, 0);
        var north = MakeRoom(area, 0, 2);
        var south = MakeRoom(area, 0, -2);
        grid.AddNode(room); grid.AddNode(north); grid.AddNode(south);
        areaObj.AddGrid(grid); nh.AddArea(areaObj);
        var northDoor = new Door(room.Coord, new Coord(area, 0, 2, 0), "north", "south", (0, 1), "X", "O", true, false);
        var southDoor = new Door(room.Coord, new Coord(area, 0, -2, 0), "south", "north", (0, -1), "X", "O", true, false);
        nh.AddDoor(northDoor);
        nh.AddDoor(southDoor);
        var c = MakeBuilderIn(room);
        new DoorCommand().Run(c, new DoorCommand().Parser!.ParseArgs(["-r", "-s", "-n"]));
        var removed = c.PeekMessages().Where(m => m.StartsWith("Removed ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, removed.Count);
        // Established removal order is north before south regardless of flag order.
        Assert.Contains("north", removed[0]);
        Assert.Contains("south", removed[1]);
        var remaining = nh.GetDoors(room.Coord);
        Assert.True(remaining is null || remaining.Count == 0);
    }
}
