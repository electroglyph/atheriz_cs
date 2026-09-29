// Roundtrip pins for the map_create_rooms wire message: grant ->
// handshake -> MapCreateRoomsHandler creates the batch, links neighbors
// both ways, and acks create_ok (or denies with a rotated key).
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class MapCreateRoomsTests
{
    private static TestConnection Conn()
    {
        var c = new TestConnection();
        c.ClientHost = "10.0.0.1";
        return c;
    }

    private static void ResetChains()
    {
        MapEdit.Reset();
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        InputFuncs.NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
    }

    private static string Handshake(TestConnection conn)
    {
        var key = MapEdit.Grant("10.0.0.1", "TestArea", 0);
        new InputFuncs().MapEditHandler(conn, [key, 0, new List<object?>()], []);
        return conn.Sent[^1].Args[1] as string ?? throw new InvalidOperationException("no handshake key");
    }

    private static Node AddRoom(string area, int x, int y, string desc = "Old desc.")
    {
        // Fresh handler every time (mirrors MapEditRoomTests): each test
        // owns its world, so LiveRoom asserts are meaningful and order-free.
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord(area, x, y, 0)) { Desc = desc };
        nh.AddNode(room);
        return room;
    }

    private static void FreshNodes() => NodeHandler.SetCurrent(new NodeHandler(autoLoad: false));

    private static Node? LiveRoom(int x, int y) =>
        NodeHandler.GetCurrent()?.GetArea("TestArea")?.GetGrid(0)?.GetNode(x, y);

    private static Dictionary<string, object?> Exit(string name, int x, int y, List<string>? aliases = null)
    {
        var dict = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["coord"] = new List<object?> { "TestArea", x, y, 0 },
        };
        if (aliases is not null) dict["aliases"] = new List<object?>(aliases.Cast<object?>());
        return dict;
    }

    private static Dictionary<string, object?> RoomEntry(int x, int y) =>
        new() { ["x"] = x, ["y"] = y, ["name"] = null, ["desc"] = null };

    private static Dictionary<string, object?> ExitsEntry(int x, int y, List<Dictionary<string, object?>> exits) =>
        new() { ["x"] = x, ["y"] = y, ["exits"] = new List<object?>(exits.Cast<object?>()) };

    private static Dictionary<string, object?> Payload(List<Dictionary<string, object?>> rooms, List<Dictionary<string, object?>> exits) =>
        new()
        {
            ["rooms"] = new List<object?>(rooms.Cast<object?>()),
            ["exits"] = new List<object?>(exits.Cast<object?>()),
        };

    private static List<string> LinkNames(Node room) =>
        room.GetLinks().Select(l => $"{l.Name}->{l.Coord.X},{l.Coord.Y}").OrderBy(s => s).ToList();

    [Fact]
    public void MapCreateRooms_AdjacentPair_CreatesAndLinksBothWays()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(
                [RoomEntry(0, 0), RoomEntry(1, 0)],
                [ExitsEntry(0, 0, [Exit("east", 1, 0, ["e"])]), ExitsEntry(1, 0, [Exit("west", 0, 0, ["w"])])])], []);
            var west = LiveRoom(0, 0);
            var east = LiveRoom(1, 0);
            Assert.NotNull(west);
            Assert.NotNull(east);
            Assert.Equal("New room.", west.Desc);
            Assert.Equal(["east->1,0"], LinkNames(west));
            Assert.Equal(["e"], west.GetLinks()[0].Aliases);
            Assert.Equal(["west->0,0"], LinkNames(east));
            Assert.Equal(["w"], east.GetLinks()[0].Aliases);
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Equal("create_ok", conn.Sent[1].Cmd);
            Assert.Equal(1, conn.Sent[1].Args[0]);
            Assert.IsType<string>(conn.Sent[1].Args[1]);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_LinksToPreExistingNeighborBothWays()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var existing = AddRoom("TestArea", 5, 5);
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(
                [RoomEntry(5, 4)],
                [ExitsEntry(5, 4, [Exit("north", 5, 5, ["n"])]), ExitsEntry(5, 5, [Exit("south", 5, 4, ["s"])])])], []);
            var fresh = LiveRoom(5, 4);
            Assert.NotNull(fresh);
            Assert.Equal(["north->5,5"], LinkNames(fresh));
            Assert.Equal(["south->5,4"], LinkNames(existing));
            Assert.Equal("Old desc.", existing.Desc);
            Assert.Equal("create_ok", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_MissingExitTarget_DeniesWithNoMutation()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(
                [RoomEntry(0, 0)],
                [ExitsEntry(0, 0, [Exit("doom", 9, 9)])])], []);
            Assert.Single(conn.Sent);
            Assert.Equal("create_denied", conn.Sent[0].Cmd);
            Assert.Equal(1, conn.Sent[0].Args[0]);
            var rotated = conn.Sent[0].Args[1] as string;
            Assert.NotNull(rotated);
            Assert.Contains("doom", conn.Sent[0].Args[2] as string);
            // Validate-all-first: the batch room was never created.
            Assert.Null(LiveRoom(0, 0));
            // The rotated key still works: the session survived the deny.
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [rotated, 2, Payload(
                [RoomEntry(0, 0), RoomEntry(1, 0)],
                [ExitsEntry(0, 0, [Exit("east", 1, 0, ["e"])]), ExitsEntry(1, 0, [Exit("west", 0, 0, ["w"])])])], []);
            Assert.Equal("create_ok", conn.Sent[^1].Cmd);
            Assert.NotNull(LiveRoom(0, 0));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_ExitsForUnknownRoom_Denies()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(
                [RoomEntry(0, 0)], [ExitsEntry(7, 7, [])])], []);
            Assert.Single(conn.Sent);
            Assert.Equal("create_denied", conn.Sent[0].Cmd);
            Assert.Equal("No room at (7, 7).", conn.Sent[0].Args[2]);
            Assert.Null(LiveRoom(0, 0));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_MissingRoomsKey_Rejects()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["exits"] = new List<object?>() }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid create payload.", conn.Sent[0].Args[0]);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_TooManyRooms_Rejects()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            var rooms = new List<Dictionary<string, object?>>();
            for (int i = 0; i < 257; i++) rooms.Add(RoomEntry(i, 0));
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(rooms, [])], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Too many rooms (max 256).", conn.Sent[0].Args[0]);
            Assert.Null(LiveRoom(0, 0));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_DuplicateDelivery_ReplaysVerdictWithoutDuplicatingLinks()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            var payload = Payload(
                [RoomEntry(0, 0), RoomEntry(1, 0)],
                [ExitsEntry(0, 0, [Exit("east", 1, 0, ["e"])]), ExitsEntry(1, 0, [Exit("west", 0, 0, ["w"])])]);
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, payload], []);
            Assert.Equal("create_ok", conn.Sent[^1].Cmd);
            var rotated = conn.Sent[^1].Args[1] as string;
            Assert.NotNull(rotated);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [rotated, 2, payload], []);
            Assert.Equal("create_ok", conn.Sent[^1].Cmd);
            Assert.Equal(["east->1,0"], LinkNames(LiveRoom(0, 0)!));
            Assert.Equal(["west->0,0"], LinkNames(LiveRoom(1, 0)!));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_IsRegisteredUnderBothNames()
    {
        var handlers = new InputFuncs().GetHandlers();
        Assert.True(handlers.ContainsKey("map_create_rooms"));
        Assert.True(handlers.ContainsKey("MapCreateRoomsHandler"));
        Assert.Same(handlers["map_create_rooms"], handlers["MapCreateRoomsHandler"]);
    }

    [Fact]
    public void MapCreateRooms_StampsRoomPlaceholderForCreatedRooms()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        var mh = new MapHandler(autoLoad: false);
        InputFuncs.MapHandlerFactory = () => mh;
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(
                [RoomEntry(0, 0), RoomEntry(1, 0)],
                [ExitsEntry(0, 0, [Exit("east", 1, 0, ["e"])]), ExitsEntry(1, 0, [Exit("west", 0, 0, ["w"])])])], []);
            Assert.Equal("create_ok", conn.Sent[^1].Cmd);
            var pre = mh.EnsureMapInfo("TestArea", 0).PreGrid;
            var roomPH = Atheriz.Core.Settings.AtherizSettings.Global.RoomPlaceholder;
            Assert.Equal(roomPH, pre[(0, 0)]);
            Assert.Equal(roomPH, pre[(1, 0)]);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_DoesNotOverwriteDrawnArt()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        var mh = new MapHandler(autoLoad: false);
        InputFuncs.MapHandlerFactory = () => mh;
        try
        {
            mh.EnsureMapInfo("TestArea", 0).SetPreCell((0, 0), "#");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(
                [RoomEntry(0, 0)], [ExitsEntry(0, 0, [])])], []);
            Assert.Equal("create_ok", conn.Sent[^1].Cmd);
            Assert.NotNull(LiveRoom(0, 0));
            Assert.Equal("#", mh.EnsureMapInfo("TestArea", 0).PreGrid[(0, 0)]);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapCreateRooms_RenderedBoundsCoverCreatedRooms()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        var mh = new MapHandler(autoLoad: false);
        InputFuncs.MapHandlerFactory = () => mh;
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapCreateRoomsHandler(conn, [hk, 1, Payload(
                [RoomEntry(3, 2), RoomEntry(4, 2)],
                [ExitsEntry(3, 2, [Exit("east", 4, 2, ["e"])]), ExitsEntry(4, 2, [Exit("west", 3, 2, ["w"])])])], []);
            Assert.Equal("create_ok", conn.Sent[^1].Cmd);
            // The user-visible property: the in-game map text spans the new
            // rooms, so the character symbol position lands inside it.
            var mi = mh.EnsureMapInfo("TestArea", 0);
            var (rendered, minX, maxY) = MapInfo.RenderGrid(mi.PostGrid);
            Assert.Equal(3, minX);
            Assert.Equal(2, maxY);
            Assert.Contains("  ", rendered);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }
}
