// Roundtrip pins for the map_edit_exits wire message: grant -> handshake ->
// MapEditExitsHandler replaces the room's full exit list and acks exits_ok.
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class MapEditExitsTests
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

    private static Node AddRoom(string area, int x, int y, List<NodeLink>? links = null)
    {
        // Fresh handler on first call, then reuse: AddNode grafts each
        // room into the shared area/grid the handler looks up.
        var nh = NodeHandler.GetCurrent() ?? new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord(area, x, y, 0), links: links);
        nh.AddNode(room);
        return room;
    }

    private static Dictionary<string, object?> Exit(string name, int x, int y, string area = "TestArea", int z = 0, List<string>? aliases = null)
    {
        var dict = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["coord"] = new List<object?> { area, x, y, z },
        };
        if (aliases is not null) dict["aliases"] = new List<object?>(aliases.Cast<object?>());
        return dict;
    }

    private static List<string> LinkNames(Node room) =>
        room.GetLinks().Select(l => $"{l.Name}->{l.Coord.X},{l.Coord.Y}").OrderBy(s => s).ToList();

    [Fact]
    public void MapEditExits_Replace_RoundtripAppliesAndAcks()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, [new NodeLink("north", new Coord("TestArea", 2, 4, 0))]);
            AddRoom("TestArea", 2, 4);
            AddRoom("TestArea", 5, 5);
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditExitsHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["exits"] = new List<object?> { Exit("east", 5, 5, aliases: ["e"]) } }], []);
            Assert.Equal(["east->5,5"], LinkNames(room));
            Assert.Equal(["e"], room.GetLinks()[0].Aliases);
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Equal("exits_ok", conn.Sent[1].Cmd);
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
    public void MapEditExits_MissingRoom_DeniesKeepsSession()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3);
            AddRoom("TestArea", 5, 5);
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditExitsHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 9, ["y"] = 9, ["exits"] = new List<object?> { Exit("east", 5, 5) } }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("exits_denied", conn.Sent[0].Cmd);
            Assert.Equal("No room at (9, 9).", conn.Sent[0].Args[2]);
            var rotated = conn.Sent[0].Args[1] as string;
            Assert.NotNull(rotated);
            conn.ClearSent();
            new InputFuncs().MapEditExitsHandler(conn,
                [rotated!, 2, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["exits"] = new List<object?> { Exit("east", 5, 5) } }], []);
            Assert.Equal(["east->5,5"], LinkNames(room));
            Assert.Equal("exits_ok", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditExits_MissingTarget_DeniesWithoutPartialApply()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, [new NodeLink("north", new Coord("TestArea", 2, 4, 0))]);
            AddRoom("TestArea", 2, 4);
            AddRoom("TestArea", 5, 5);
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            new InputFuncs().MapEditExitsHandler(conn,
                [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["exits"] = new List<object?> { Exit("east", 5, 5), Exit("doom", 9, 9) } }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("exits_denied", conn.Sent[0].Cmd);
            Assert.Contains("doom", conn.Sent[0].Args[2] as string ?? "");
            // No partial apply: the old link list is intact.
            Assert.Equal(["north->2,4"], LinkNames(room));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditExits_BadShape_RejectsWithoutTouchingLinks()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3, [new NodeLink("north", new Coord("TestArea", 2, 4, 0))]);
            AddRoom("TestArea", 2, 4);
            var conn = Conn();
            var hk = Handshake(conn);

            void SendReject(object? exits, string expected)
            {
                conn.ClearSent();
                new InputFuncs().MapEditExitsHandler(conn,
                    [hk, 1, new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["exits"] = exits }], []);
                Assert.Single(conn.Sent);
                Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
                Assert.Equal(expected, conn.Sent[0].Args[0]);
            }

            // Rejects never consume the key, so every send reuses [hk, 1].
            SendReject(new List<object?> { Exit("east", 2, 4), Exit("EAST", 2, 4) }, "Duplicate exit name 'EAST'.");
            SendReject(new List<object?> { Exit("", 2, 4) }, "Invalid exit name at index 0.");
            SendReject(new List<object?> { new Dictionary<string, object?> { ["name"] = "east", ["coord"] = new List<object?> { "TestArea", "5", 5, 0 } } }, "Invalid exit coord at index 0.");
            SendReject(new List<object?> { new Dictionary<string, object?> { ["name"] = "east" } }, "Invalid exit coord at index 0.");
            var many = Enumerable.Range(0, 65).Select(i => (object?)Exit($"e{i}", 2, 4)).ToList();
            SendReject(many, "Too many exits (max 64).");
            Assert.Equal(["north->2,4"], LinkNames(room));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditExits_RetryAfterDeny_ReplaysDeny()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            AddRoom("TestArea", 2, 3);
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            var missing = new Dictionary<string, object?> { ["x"] = 9, ["y"] = 9, ["exits"] = new List<object?> { Exit("east", 2, 3) } };
            new InputFuncs().MapEditExitsHandler(conn, [hk, 1, missing], []);
            Assert.Equal("exits_denied", conn.Sent[^1].Cmd);
            var rotated = conn.Sent[^1].Args[1] as string;
            Assert.NotNull(rotated);
            conn.ClearSent();
            new InputFuncs().MapEditExitsHandler(conn, [hk, 1, missing], []);
            Assert.Single(conn.Sent);
            Assert.Equal("exits_denied", conn.Sent[0].Cmd);
            Assert.Equal(rotated, conn.Sent[0].Args[1]);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditExits_RetryAfterSuccess_ReplaysAck()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3);
            AddRoom("TestArea", 5, 5);
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            var edit = new Dictionary<string, object?> { ["x"] = 2, ["y"] = 3, ["exits"] = new List<object?> { Exit("east", 5, 5) } };
            new InputFuncs().MapEditExitsHandler(conn, [hk, 1, edit], []);
            Assert.Equal("exits_ok", conn.Sent[^1].Cmd);
            conn.ClearSent();
            new InputFuncs().MapEditExitsHandler(conn, [hk, 1, edit], []);
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Equal("exits_ok", conn.Sent[1].Cmd);
            Assert.Equal(["east->5,5"], LinkNames(room));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditExits_JsonWireForm_Applies()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var room = AddRoom("TestArea", 2, 3);
            AddRoom("TestArea", 5, 5);
            var conn = Conn();
            var hk = Handshake(conn);
            conn.ClearSent();
            using var doc = System.Text.Json.JsonDocument.Parse(@"{""x"":2,""y"":3,""exits"":[{""name"":""east"",""aliases"":[""e""],""coord"":[""TestArea"",5,5,0]}]}");
            new InputFuncs().MapEditExitsHandler(conn, [hk, 1, doc.RootElement], []);
            Assert.Equal(["east->5,5"], LinkNames(room));
            Assert.Equal("exits_ok", conn.Sent[^1].Cmd);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ResetChains();
        }
    }

    [Fact]
    public void MapEditExits_IsRegisteredUnderBothNames()
    {
        var handlers = new InputFuncs().GetHandlers();
        Assert.True(handlers.ContainsKey("map_edit_exits"));
        Assert.True(handlers.ContainsKey("MapEditExitsHandler"));
        Assert.Same(handlers["map_edit_exits"], handlers["MapEditExitsHandler"]);
    }
}
