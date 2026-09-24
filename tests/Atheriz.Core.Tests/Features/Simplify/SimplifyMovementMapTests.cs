using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;
using Atheriz.Core;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from MoveSelfGuardTests.cs
// Move guards: self-moves are refused in both identity forms (the earlier
// checks subsume the later repeat), and the non-node-to-node announce path
// delivers with a null reverse link instead of throwing.
[Collection("Ported")]
public class MoveSelfGuardTests
{
    [Fact]
    public void MoveTo_Self_IsRefused()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("one");
            ObjectRegistry.AddObject(obj);

            Assert.False(obj.MoveTo(obj));

            var sameId = GameObject.Create("other");
            sameId.Id = obj.Id;
            Assert.False(obj.MoveTo(sameId));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_ContainerToNode_AnnouncesWithoutReverseLink()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var node = new Node(new Coord("limbo", 0, 0, 0));
            ObjectRegistry.AddObject(node);
            var mover = GameObject.Create("mover");
            ObjectRegistry.AddObject(mover);
            var receiver = GameObject.Create("recv");
            ObjectRegistry.AddObject(receiver);
            mover.MoveTo(room, force: true, announce: false);
            receiver.MoveTo(node, force: true, announce: false);

            Assert.True(mover.MoveTo(node, force: true, announce: true));

            Assert.NotEmpty(receiver.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from NodeHomeMoveTests.cs
// Node delete home-move: each child's coord home resolves through the shared
// coord index (with scan fallback), matching MoveTo's resolution exactly.
[Collection("Ported")]
public class NodeHomeMoveTests
{
    [Fact]
    public void NodeDelete_MovesChildrenHomeByCoord()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var coordA = new Coord("limbo", 0, 0, 0);
            var coordB = new Coord("limbo", 5, 5, 0);
            var nodeA = new Node(coordA);
            var nodeB = new Node(coordB);
            ObjectRegistry.AddObject(nodeA);
            ObjectRegistry.AddObject(nodeB);
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var kids = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                var kid = GameObject.Create($"kid{i}");
                ObjectRegistry.AddObject(kid);
                kid.Home = new LocationRef.CoordLocation(coordB);
                nodeA.AddObject(kid);
                kids.Add(kid);
            }

            var result = nodeA.Delete(admin, recursive: false);

            Assert.NotNull(result);
            foreach (var kid in kids)
            {
                var loc = Assert.IsType<LocationRef.CoordLocation>(kid.Location);
                Assert.Equal(coordB, loc.Coord);
                Assert.Contains(kid.Id, nodeB.ContentsSnapshot);
            }
            Assert.DoesNotContain(kids[0].Id, nodeA.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from NodeHydrationTests.cs
// Deduped node hydration with centralized null backfill: Node.Name is
// coord-derived with a no-op setter (pre-existing Node.Links.cs override),
// so the plain branch's Name write is unobservable — both branches yield
// the coord-string Name. The pin covers what hydration guarantees: the
// other fields assign and null collections backfill instead of throwing.
[Collection("Ported")]
public class NodeHydrationTests
{
    private sealed class HydrationProbeNode : Node
    {
        public HydrationProbeNode(Coord coord) : base(coord) { }
    }

    static NodeHydrationTests()
    {
        Node.RegisterPersistedSubtype("SimplifyHydrationProbe", typeof(HydrationProbeNode), c => new HydrationProbeNode(c));
    }

    private static NodeAreaDto MakeArea(NodeDto plain, NodeDto sub)
    {
        return new NodeAreaDto
        {
            Name = "hydration",
            Grids =
            {
                [0] = new NodeGridDto
                {
                    Area = "hydration",
                    Z = 0,
                    Nodes = { ["0,0"] = plain, ["1,0"] = sub },
                },
            },
        };
    }

    [Fact]
    public void ToDomain_BothBranchesYieldCoordName_OtherFieldsAssign()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = new NodeDto { Coord = new Coord("hydration", 0, 0, 0), Name = "PlainName", Id = 9101 };
        var sub = new NodeDto { Coord = new Coord("hydration", 1, 0, 0), Name = "SubName", Id = 9102, ObjectType = "SimplifyHydrationProbe" };
        var area = MakeArea(plain, sub).ToDomain();
        var grid = area.Grids[0];
        Assert.Equal("hydration(0,0,0)", grid.Nodes[(0, 0)].Name);
        Assert.Equal(9101, grid.Nodes[(0, 0)].Id);
        var subNode = Assert.IsType<HydrationProbeNode>(grid.Nodes[(1, 0)]);
        Assert.Equal("hydration(1,0,0)", subNode.Name);
        Assert.Equal(9102, subNode.Id);
        Assert.NotEqual("SubName", subNode.Name);
    }

    [Fact]
    public void ToDomain_NullCollections_BackfillToEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = new NodeDto { Coord = new Coord("hydration", 0, 0, 0), Name = "Nully", Id = 9201 };
        plain.Links = null!;
        plain.Nouns = null!;
        plain.Scripts = null!;
        var sub = new NodeDto { Coord = new Coord("hydration", 1, 0, 0), Name = "Sub", Id = 9202 };
        var area = MakeArea(plain, sub).ToDomain();
        var node = area.Grids[0].Nodes[(0, 0)];
        Assert.NotNull(node.Links);
        Assert.Empty(node.Links);
        Assert.NotNull(node.Nouns);
        Assert.Empty(node.Nouns);
    }
}

// Merged from NodeNounCaseTests.cs
// Node nouns: the store is case-insensitive, so no case-variant hunt is
// needed on add and a single remove clears every casing.
[Collection("Ported")]
public class NodeNounCaseTests
{
    [Fact]
    public void AddNoun_OverwritesAcrossCasings()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));

        node.AddNoun("Sword", "a blade");
        Assert.Equal("a blade", node.GetNoun("sWORD"));

        node.AddNoun("SWORD", "other");
        Assert.Equal("other", node.GetNoun("sword"));
    }

    [Fact]
    public void RemoveNoun_ClearsEveryCasing()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));
        node.AddNoun("Sword", "a blade");

        node.RemoveNoun("SwOrD");

        Assert.Null(node.GetNoun("sword"));
        Assert.Null(node.GetNoun("SWORD"));
    }
}

// Merged from MazeBacktrackTests.cs
[Collection("Ported")]
public sealed class MazeBacktrackTests
{
    [Fact]
    public void CreateMaze_DegenerateSizes_Terminate()
    {
        using var env = GlobalTestEnv.Enter();
        var tiny = MazeCommand.CreateMaze(1, 1);
        Assert.Empty(tiny);
        var small = MazeCommand.CreateMaze(2, 2);
        Assert.NotNull(small);
    }

    [Fact]
    public void CreateMaze_EdgesAreInBoundsAndAdjacent()
    {
        using var env = GlobalTestEnv.Enter();
        const int w = 6;
        const int h = 6;
        var maze = MazeCommand.CreateMaze(w, h);
        Assert.NotEmpty(maze);
        foreach (var (cell, neighbors) in maze)
        {
            Assert.InRange(cell.Item1, 0, w - 1);
            Assert.InRange(cell.Item2, 0, h - 1);
            foreach (var next in neighbors)
            {
                Assert.InRange(next.Item1, 0, w - 1);
                Assert.InRange(next.Item2, 0, h - 1);
                Assert.Equal(1, Math.Abs(next.Item1 - cell.Item1) + Math.Abs(next.Item2 - cell.Item2));
            }
        }
    }

    [Fact]
    public void GenMapAndGrid_NodeCountMatchesMaze_LinkTargetsInBounds()
    {
        using var env = GlobalTestEnv.Enter();
        const int w = 4;
        const int h = 4;
        var (map, grid) = MazeCommand.GenMapAndGrid(w, h, "backtrack-sym");
        // One node per maze key (dead-end cells carry no outgoing edge and
        // get no node — the backtrack change must not alter that shape).
        Assert.Equal(map.Count, grid.Nodes.Count);
        foreach (var kv in grid.Nodes.ToList())
        {
            var (x, y) = kv.Key;
            var node = grid.GetNode(x, y);
            Assert.NotNull(node);
            foreach (var link in node.GetLinks())
            {
                Assert.InRange(link.Coord.X, 0, w - 1);
                Assert.InRange(link.Coord.Y, 0, h - 1);
            }
        }
    }
}

// Merged from PathfindSharedCoreTests.cs
// Shared link snapshot, iteration-cap resolution, and folded child loop:
// optimal paths are preserved and the shared cap override is honored.
[Collection("Ported")]
public class PathfindSharedCoreTests
{
    private static (Coord a, Coord b, NodeHandler nh) MakeLinkedPair()
    {
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var coordA = new Coord("simplify", 0, 0, 0);
        var coordB = new Coord("simplify", 2, 0, 0);
        var a = new Node(coordA);
        var b = new Node(coordB);
        if (ObjectRegistry.Get(a.Id).Count == 0) ObjectRegistry.AddObject(a);
        if (ObjectRegistry.Get(b.Id).Count == 0) ObjectRegistry.AddObject(b);
        a.AddLink(new NodeLink("east", coordB, new List<string> { "e" }));
        b.AddLink(new NodeLink("west", coordA, new List<string> { "w" }));
        nh.AddNode(a);
        nh.AddNode(b);
        return (coordA, coordB, nh);
    }

    [Fact]
    public void AStar_DirectLink_ReturnsOptimalTwoNodePath()
    {
        var (coordA, coordB, nh) = MakeLinkedPair();
        try
        {
            var start = nh.GetNode(coordA)!;
            var end = nh.GetNode(coordB)!;
            var (found, path, _) = Pathfind.AStar(start, end, null, nh);
            Assert.True(found);
            Assert.Equal(new[] { coordA, coordB }, path.Select(n => n.Coord).ToArray());
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void FindPath_AgreesWithAStar_OnSharedSnapshot()
    {
        var (coordA, coordB, nh) = MakeLinkedPair();
        try
        {
            Assert.Equal(new[] { coordA, coordB }, Pathfind.FindPath(coordA, coordB, nh));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void IterationCapOverride_ZeroDeniesEveryPath()
    {
        var (coordA, coordB, nh) = MakeLinkedPair();
        try
        {
            Assert.Null(Pathfind.FindPath(coordA, coordB, nh, 0));
            var start = nh.GetNode(coordA)!;
            var end = nh.GetNode(coordB)!;
            Assert.False(Pathfind.AStar(start, end, null, nh, 0).Found);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}

// Merged from DirectionDistanceCoreTests.cs
// Unified direction/distance cores: Coord and tuple overloads agree, and every
// Dist3d overload returns the same Pow-formulation value.
public class DirectionDistanceCoreTests
{
    [Theory]
    [InlineData("north", 0, 0, 0, 1)]
    [InlineData("south", 0, 1, 0, 0)]
    [InlineData("east", 0, 0, 1, 0)]
    [InlineData("west", 1, 0, 0, 0)]
    [InlineData("northeast", 0, 0, 1, 1)]
    [InlineData("southwest", 1, 1, 0, 0)]
    [InlineData("", 0, 0, 0, 0)]
    public void GetDir_CoordOverload_MatchesCompass(string expected, int ox, int oy, int dx, int dy)
    {
        Assert.Equal(expected, GameUtils.GetDir(new Coord("limbo", ox, oy, 0), new Coord("limbo", dx, dy, 0)));
    }

    [Fact]
    public void GetDir_SameArea_ComposeNsEwInOrder()
    {
        Assert.Equal("northeast", GameUtils.GetDir(
            new Coord("limbo", 0, 0, 0), new Coord("limbo", 1, 1, 0)));
    }

    [Fact]
    public void Dist3d_CoordAndTupleOverloads_AgreeOnKnownValue()
    {
        var coordOrigin = new Coord("a", 0, 0, 0);
        var coordDest = new Coord("a", 3, 4, 0);
        Assert.Equal(5.0, GameUtils.Dist3d(coordOrigin, coordDest), 9);
        Assert.Equal(5.0, GameUtils.Dist3d((0, 0, 0), (3, 4, 0)), 9);
    }
}

// Merged from DirectionTableTests.cs
[Collection("Ported")]
public sealed class DirectionTableTests
{
    private static GameObject MakeBuilderIn(Node node)
    {
        var c = GameObject.Create("Builder", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(c);
        c.Location = new LocationRef.CoordLocation(node.Coord);
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

// Merged from DoorMapPaintTests.cs
// Door map paint: MapClose/MapOpen share one parameterized body differing
// only in preset symbol + closed flag; the gate (map disabled, missing symbol
// coord, default endpoints) returns before touching any handler.
[Collection("Ported")]
public class DoorMapPaintTests
{
    [Fact]
    public void MapClose_MapOpen_AreNoOps_WithoutSymbolCoord()
    {
        var door = new Door(new Coord("limbo", 0, 0, 0), new Coord("limbo", 0, 1, 0), "north", "south");

        door.MapClose();
        door.MapOpen();

        Assert.True(door.Closed);
        Assert.False(door.Locked);
    }

    [Fact]
    public void MapPaint_ReturnsEarly_WhenMapDisabled()
    {
        bool prev = AtherizSettings.Global.MapEnabled;
        AtherizSettings.Global.MapEnabled = false;
        try
        {
            var door = new Door(new Coord("limbo", 0, 0, 0), new Coord("limbo", 0, 1, 0), "north", "south", (1, 2));

            door.MapClose();
            door.MapOpen();

            Assert.True(door.Closed);
        }
        finally { AtherizSettings.Global.MapEnabled = prev; }
    }
}

// Merged from DoorGlyphPrefixTests.cs
// Door-glyph prefix hoist only: all eight defaults stay byte-identical, and
// AllSymbols keeps reflecting live reassignments (no static cache).
public class DoorGlyphPrefixTests
{
    private const string Prefix = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m";

    [Fact]
    public void DoorGlyphDefaults_KeepExactBytes()
    {
        var settings = new AtherizSettings();
        Assert.Equal(Prefix + "━\x1b[0m", settings.NsClosedDoor);
        Assert.Equal(Prefix + "┚\x1b[0m", settings.NsOpenDoor1);
        Assert.Equal(Prefix + "┒\x1b[0m", settings.NsOpenDoor2);
        Assert.Equal(Prefix + "┃\x1b[0m", settings.EwClosedDoor);
        Assert.Equal(Prefix + "┙\x1b[0m", settings.EwOpenDoor1);
        Assert.Equal(Prefix + "┕\x1b[0m", settings.EwOpenDoor2);
        Assert.Equal(Prefix + "╳\x1b[0m", settings.UdClosedDoor);
        Assert.Equal(Prefix + "▽\x1b[0m", settings.UdOpenDoor);
    }

    [Fact]
    public void AllSymbols_ReflectsLiveReassignment()
    {
        var settings = new AtherizSettings { SingleWallPlaceholder = "Z" };
        Assert.Contains("Z", settings.AllSymbols);
        Assert.DoesNotContain("༗", settings.AllSymbols);
    }
}
