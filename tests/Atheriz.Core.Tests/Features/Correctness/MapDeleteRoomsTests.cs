// Roundtrip pins for the map_delete_rooms wire message: grant ->
// handshake -> MapDeleteRoomsHandler removes the batch (evacuating
// occupants to fallbacks) and acks delete_ok (or denies with a rotated
// key, leaving the world untouched).
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class MapDeleteRoomsTests
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

    private static void FreshNodes() => NodeHandler.SetCurrent(new NodeHandler(autoLoad: false));

    private static Node AddRoom(string area, int x, int y, string desc = "Old desc.")
    {
        // Reuse the test's handler (fresh per test via the finally
        // cleanup): each test owns one world, so LiveRoom asserts are
        // meaningful and order-free. News the handler on first use.
        var nh = NodeHandler.GetCurrent() ?? new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord(area, x, y, 0)) { Desc = desc };
        nh.AddNode(room);
        return room;
    }

    private static Node? LiveRoom(int x, int y) =>
        NodeHandler.GetCurrent()?.GetArea("TestArea")?.GetGrid(0)?.GetNode(x, y);

    private static Dictionary<string, object?> DeleteEntry(int x, int y, int? fx = null, int? fy = null)
    {
        var dict = new Dictionary<string, object?> { ["x"] = x, ["y"] = y };
        if (fx is not null && fy is not null)
            dict["fallback"] = new Dictionary<string, object?> { ["x"] = fx.Value, ["y"] = fy.Value };
        return dict;
    }

    private static Dictionary<string, object?> Payload(List<Dictionary<string, object?>> rooms) =>
        new() { ["rooms"] = new List<object?>(rooms.Cast<object?>()) };

    private static List<string> LinkNames(Node room) =>
        room.GetLinks().Select(l => $"{l.Name}->{l.Coord.X},{l.Coord.Y}").OrderBy(s => s).ToList();

    [Fact]
    public void MapDeleteRooms_Pair_RemovesBothAndAcks()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var west = AddRoom("TestArea", 0, 0);
        var east = AddRoom("TestArea", 1, 0);
        west.AddLink(new NodeLink("east", new Coord("TestArea", 1, 0, 0), ["e"]));
        east.AddLink(new NodeLink("west", new Coord("TestArea", 0, 0, 0), ["w"]));
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(0, 0), DeleteEntry(1, 0)])], []);
            Assert.Null(LiveRoom(0, 0));
            Assert.Null(LiveRoom(1, 0));
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Equal("delete_ok", conn.Sent[1].Cmd);
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
    public void MapDeleteRooms_OccupiedEvacuatesToFallback()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var doomed = AddRoom("TestArea", 0, 0);
        var safe = AddRoom("TestArea", 1, 0);
        var obj = new GameObject();
        ObjectRegistry.AddObject(obj);
        Assert.True(obj.MoveTo(doomed, force: true, announce: false));
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(0, 0, 1, 0)])], []);
            Assert.Equal("delete_ok", conn.Sent[^1].Cmd);
            Assert.Null(LiveRoom(0, 0));
            Assert.Same(safe, LiveRoom(1, 0));
            Assert.Contains(obj, safe.GetContents());
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_OccupiedWithoutFallback_DeniesAndKeepsSession()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var doomed = AddRoom("TestArea", 0, 0);
        var obj = new GameObject();
        ObjectRegistry.AddObject(obj);
        Assert.True(obj.MoveTo(doomed, force: true, announce: false));
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(0, 0)])], []);
            Assert.Equal("delete_denied", conn.Sent[^1].Cmd);
            Assert.Contains("occupied", (conn.Sent[^1].Args[2] as string ?? "").ToLowerInvariant());
            Assert.Same(doomed, LiveRoom(0, 0));
            Assert.Contains(obj, doomed.GetContents());
            // The rotated key recovers: the session survives the deny.
            var rotated = conn.Sent[^1].Args[1] as string;
            Assert.NotNull(rotated);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [rotated, 2, Payload([DeleteEntry(0, 0)])], []);
            Assert.Equal("delete_denied", conn.Sent[^1].Cmd);
            Assert.Same(doomed, LiveRoom(0, 0));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_StripsInboundLinksFromSurvivors()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var target = AddRoom("TestArea", 0, 0);
        var holder = AddRoom("TestArea", 1, 0);
        holder.AddLink(new NodeLink("west", new Coord("TestArea", 0, 0, 0), ["w"]));
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(0, 0)])], []);
            Assert.Equal("delete_ok", conn.Sent[^1].Cmd);
            Assert.Null(LiveRoom(0, 0));
            Assert.Same(holder, LiveRoom(1, 0));
            Assert.Empty(holder.GetLinks());
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_SelfLinkedRoom_DeletesCleanly()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var loop = AddRoom("TestArea", 4, 5);
        loop.AddLink(new NodeLink("loop", new Coord("TestArea", 4, 5, 0), []));
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(4, 5)])], []);
            Assert.Equal("delete_ok", conn.Sent[^1].Cmd);
            Assert.Null(LiveRoom(4, 5));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_MissingRoom_SkipsIdempotently()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            // Nothing at (9, 9): duplicate delivery of an already-undone
            // batch still acks instead of denying.
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(9, 9)])], []);
            Assert.Equal("delete_ok", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_DoorEndpoint_DeniesWithoutMutation()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var west = AddRoom("TestArea", 0, 0);
        var east = AddRoom("TestArea", 1, 0);
        var nh = NodeHandler.GetCurrent()!;
        nh.AddDoor(Door.Create(west.Coord, "east", east.Coord, "west"));
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(0, 0)])], []);
            Assert.Equal("delete_denied", conn.Sent[^1].Cmd);
            Assert.Contains("door", (conn.Sent[^1].Args[2] as string ?? "").ToLowerInvariant());
            Assert.Same(west, LiveRoom(0, 0));
            Assert.Same(east, LiveRoom(1, 0));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_FallbackAlsoDeleted_DeniesWithoutMutation()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var doomed = AddRoom("TestArea", 0, 0);
        var fallback = AddRoom("TestArea", 1, 0);
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([DeleteEntry(0, 0, 1, 0), DeleteEntry(1, 0)])], []);
            Assert.Equal("delete_denied", conn.Sent[^1].Cmd);
            Assert.Contains("fallback", (conn.Sent[^1].Args[2] as string ?? "").ToLowerInvariant());
            Assert.Same(doomed, LiveRoom(0, 0));
            Assert.Same(fallback, LiveRoom(1, 0));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_Malformed_Rejects()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        FreshNodes();
        try
        {
            var conn = Conn();
            var hk = Handshake(conn);
            // Empty batch.
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 1, Payload([])], []);
            Assert.Equal("map_edit_reject", conn.Sent[^1].Cmd);
            // Duplicate coords.
            conn.ClearSent();
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 2, Payload([DeleteEntry(3, 3), DeleteEntry(3, 3)])], []);
            Assert.Equal("map_edit_reject", conn.Sent[^1].Cmd);
            // Fallback with non-integer coords.
            conn.ClearSent();
            var bad = new Dictionary<string, object?> { ["x"] = 4, ["y"] = 4, ["fallback"] = "nowhere" };
            new InputFuncs().MapDeleteRoomsHandler(conn, [hk, 3, Payload([bad])], []);
            Assert.Equal("map_edit_reject", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapDeleteRooms_IsRegisteredUnderBothNames()
    {
        var handlers = new InputFuncs().GetHandlers();
        Assert.True(handlers.ContainsKey("map_delete_rooms"));
        Assert.True(handlers.ContainsKey("MapDeleteRoomsHandler"));
        Assert.Same(handlers["map_delete_rooms"], handlers["MapDeleteRoomsHandler"]);
    }
}
