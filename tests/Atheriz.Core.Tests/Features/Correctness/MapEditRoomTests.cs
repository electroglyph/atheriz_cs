// Roundtrip pins for the map_edit_room wire message: grant -> handshake ->
// MapEditRoomHandler applies the edit to the live node and acks room_ok.
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class MapEditRoomTests
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

    private static Node AddRoom(string area, int x, int y, string desc)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord(area, x, y, 0)) { Desc = desc };
        nh.AddNode(room);
        return room;
    }

    [Fact]
    public void MapEditRoom_SetsDesc_AcksRoomOk()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["name"] = "Hall", ["desc"] = "A dusty hall." }], []);
            Assert.Equal("A dusty hall.", room.Desc);
            // Plain Node.Name stays coord-derived (pinned no-op setter);
            // the editable room name lands on DisplayName instead.
            Assert.Equal("Hall", room.DisplayName);
            Assert.Equal(room.Coord.ToString(), room.Name);
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Equal("room_ok", conn.Sent[1].Cmd);
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
    public void MapEditRoom_NameOnly_AppliesDisplayNameLeavesDesc()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 4, 5, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 4, ["y"] = 5, ["name"] = "Throne Room" }], []);
            Assert.Equal("Throne Room", room.DisplayName);
            Assert.Equal("Old desc.", room.Desc);
            Assert.Equal(room.Coord.ToString(), room.Name);
            Assert.Equal("room_ok", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditRoom_MissingNode_DeniesAndKeepsSession()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 9, ["y"] = 9, ["desc"] = "Nowhere." }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("room_denied", conn.Sent[0].Cmd);
            Assert.Equal(1, conn.Sent[0].Args[0]);
            var rotated = conn.Sent[0].Args[1] as string;
            Assert.NotNull(rotated);
            Assert.Equal("No room at (9, 9).", conn.Sent[0].Args[2]);
            // The rotated key still works: the session survived the deny.
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [rotated!, 2, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["desc"] = "Recovered." }], []);
            Assert.Equal("Recovered.", room.Desc);
            Assert.Equal("room_ok", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditRoom_BadPayload_RejectsWithoutTouchingNodes()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn, [hk, 1], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid room payload.", conn.Sent[0].Args[0]);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = "2", ["y"] = 3 }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid room coordinates.", conn.Sent[0].Args[0]);
            Assert.Equal("Old desc.", room.Desc);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditRoom_RetryAfterDeny_ReplaysDeny()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            var missing = new Dictionary<string, object?> { ["x"] = 9, ["y"] = 9, ["desc"] = "Nowhere." };
            new InputFuncs().MapEditRoomHandler(conn, [hk, 1, missing], []);
            Assert.Equal("room_denied", conn.Sent[^1].Cmd);
            var rotated = conn.Sent[^1].Args[1] as string;
            Assert.NotNull(rotated);
            // The client missed the deny and resends seq 1 with the spent
            // key: Retry must replay the deny, never a save ack.
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn, [hk, 1, missing], []);
            Assert.Single(conn.Sent);
            Assert.Equal("room_denied", conn.Sent[0].Cmd);
            Assert.Equal(rotated, conn.Sent[0].Args[1]);
            Assert.Equal("Old desc.", room.Desc);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditRoom_RetryAfterSuccess_ReplaysAck()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            var edit = new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["desc"] = "A dusty hall." };
            new InputFuncs().MapEditRoomHandler(conn, [hk, 1, edit], []);
            Assert.Equal("room_ok", conn.Sent[^1].Cmd);
            // Duplicate delivery of seq 1 with the spent key: the verdict
            // replays (re-applying identical values) with map_ack + room_ok.
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn, [hk, 1, edit], []);
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Equal("room_ok", conn.Sent[1].Cmd);
            Assert.Equal("A dusty hall.", room.Desc);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditRoom_BadNameOrDesc_Rejects()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["name"] = 7 }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid room name.", conn.Sent[0].Args[0]);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["desc"] = 7 }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid room desc.", conn.Sent[0].Args[0]);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["name"] = new string('n', 201) }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["desc"] = new string('d', 8001) }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            // A null desc means "leave unchanged", not "clear".
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["desc"] = null }], []);
            Assert.Equal("room_ok", conn.Sent[^1].Cmd);
            Assert.Equal("Old desc.", room.Desc);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditRoom_JsonWireForm_Applies()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            using var doc = System.Text.Json.JsonDocument.Parse(@"{""x"":2,""y"":3,""name"":""Hall"",""desc"":""Wired.""}");
            new InputFuncs().MapEditRoomHandler(conn, [hk, 1, doc.RootElement], []);
            Assert.Equal("Wired.", room.Desc);
            Assert.Equal("room_ok", conn.Sent[^1].Cmd);
            // Long coords in range apply through the same path.
            var rotated = conn.Sent[^1].Args[1] as string;
            Assert.NotNull(rotated);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                [rotated!, 2, new Dictionary<string, object?> { ["x"] = 2L, ["y"] = 3L, ["desc"] = "Long way." }], []);
            Assert.Equal("Long way.", room.Desc);
            Assert.Equal("room_ok", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditRoom_IsRegisteredUnderBothNames()
    {
        var handlers = new InputFuncs().GetHandlers();
        Assert.True(handlers.ContainsKey("map_edit_room"));
        Assert.True(handlers.ContainsKey("MapEditRoomHandler"));
        Assert.Same(handlers["map_edit_room"], handlers["MapEditRoomHandler"]);
    }

    [Fact]
    public void MapEditRoom_UnknownKey_Rejects()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, "Old desc.");
            var conn = Conn();
            Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditRoomHandler(conn,
                ["nope", 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["desc"] = "Intruder." }], []);
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("unknown_key", conn.Sent[0].Args[0]);
            Assert.Equal("Old desc.", room.Desc);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }
}
