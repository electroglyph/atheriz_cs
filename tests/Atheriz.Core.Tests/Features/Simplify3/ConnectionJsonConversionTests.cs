// Pins for the hot-path LINQ-to-loop sweep: JSON conversion and the styled
// map_edit decode behave identically for list and JsonElement shapes,
// validate-moves feeds CheckMoves the same tuples either way, and Dispatch
// still strips into fresh lists.
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class ConnectionJsonConversionTests
{
    private static JsonElement Je(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static TestConnection Conn(string host = "10.0.0.1")
    {
        var c = new TestConnection();
        c.ClientHost = host;
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

    private static string LegendHandshake(TestConnection conn, MapInfo mi)
    {
        var mh = GlobalServices.GetMapHandler();
        mh.SetMapInfo("TestArea", 0, mi);
        InputFuncs.MapHandlerFactory = () => mh;
        var key = MapEdit.Grant("10.0.0.1", "TestArea", 0);
        new InputFuncs().MapEditLegendHandler(conn, [key, 0, new List<object?>()], []);
        return conn.Sent[^1].Args[1] as string ?? throw new InvalidOperationException("no handshake key");
    }

    [Fact]
    public void MapEditHandler_StyledCellWithJsonElementColors_AppliesWrapped()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var mh = GlobalServices.GetMapHandler();
            var mi = new MapInfo("TestArea");
            mh.SetMapInfo("TestArea", 0, mi);
            InputFuncs.MapHandlerFactory = () => mh;
            var conn = Conn();
            var hk = Handshake(conn);
            var cell = new List<object?> { 1, 2, "B", Je("[255,0,0]"), Je("[-1,-1,-1]"), Je("[\"bold\"]") };
            new InputFuncs().MapEditHandler(conn, [hk, 1, new List<object?> { cell }], []);
            var expected = GameUtils.WrapRgb("B", (255, 0, 0), null, bold: true, italic: false, underline: false);
            Assert.Equal(expected, mi.PreGrid[(1, 2)]);
            Assert.Equal("map_ack", conn.Sent[^1].Cmd);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditHandler_StyledCellWithJsonElementAttrsArray_AppliesFlags()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var mh = GlobalServices.GetMapHandler();
            var mi = new MapInfo("TestArea");
            mh.SetMapInfo("TestArea", 0, mi);
            InputFuncs.MapHandlerFactory = () => mh;
            var conn = Conn();
            var hk = Handshake(conn);
            var cell = new List<object?>
            {
                4, 4, "C",
                new List<object?> { 10, 20, 30 },
                new List<object?> { 1, 2, 3 },
                Je("[\"italic\",\"underline\"]"),
            };
            new InputFuncs().MapEditHandler(conn, [hk, 1, new List<object?> { cell }], []);
            var expected = GameUtils.WrapRgb("C", (10, 20, 30), (1, 2, 3), bold: false, italic: true, underline: true);
            Assert.Equal(expected, mi.PreGrid[(4, 4)]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_ListColorTriple_AcceptsWithDefaultFg()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var mi = new MapInfo("TestArea");
            var conn = Conn();
            var hk = LegendHandshake(conn, mi);
            conn.ClearSent();
            var legend = new List<object?>
            {
                new Dictionary<string, object?> { ["symbol"] = "@", ["fg"] = new List<object?> { 1, 2, 3 } },
            };
            new InputFuncs().MapEditLegendHandler(conn, [hk, 1, legend], []);
            Assert.Equal("legend_ok", conn.Sent[^1].Cmd);
            Assert.Single(mi.LegendEntries);
            Assert.Equal(170.0, mi.LegendEntries[0].Fg);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_NonIntColorTriple_RejectsAtIndex()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var conn = Conn();
            var legend = new List<object?>
            {
                new Dictionary<string, object?> { ["symbol"] = "@", ["fg"] = new List<object?> { 1, 2, "x" } },
            };
            new InputFuncs().MapEditLegendHandler(conn, ["bogus-key", 1, legend], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid legend entry at index 0.", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_NonIntCoordList_RejectsAtIndex()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var conn = Conn();
            var legend = new List<object?>
            {
                new Dictionary<string, object?> { ["symbol"] = "@", ["coord"] = new List<object?> { 1, "x" } },
            };
            new InputFuncs().MapEditLegendHandler(conn, ["bogus-key", 1, legend], []);
            Assert.Single(conn.Sent);
            Assert.Equal("Invalid legend entry at index 0.", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_JsonElementCoordValue_ResolvesCoord()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var mi = new MapInfo("TestArea");
            var conn = Conn();
            var hk = LegendHandshake(conn, mi);
            conn.ClearSent();
            var legend = new List<object?>
            {
                new Dictionary<string, object?> { ["symbol"] = "@", ["coord"] = Je("[5,6]"), ["fg"] = Je("3") },
            };
            new InputFuncs().MapEditLegendHandler(conn, [hk, 1, legend], []);
            Assert.Equal("legend_ok", conn.Sent[^1].Cmd);
            Assert.Single(mi.LegendEntries);
            Assert.Equal((5, 6), mi.LegendEntries[0].Coord);
            Assert.Equal(3.0, mi.LegendEntries[0].Fg);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_NonIntJsonCoord_RejectsAtIndex()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var conn = Conn();
            var legend = new List<object?>
            {
                new Dictionary<string, object?> { ["symbol"] = "@", ["coord"] = Je("[1,\"x\"]") },
            };
            new InputFuncs().MapEditLegendHandler(conn, ["bogus-key", 1, legend], []);
            Assert.Single(conn.Sent);
            Assert.Equal("Invalid legend entry at index 0.", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapValidateMovesHandler_ListAndJsonMoves_GiveSameVerdict()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var grid = new NodeGrid("TestArea", 0);
            grid.Nodes[(3, 3)] = new Node(new Coord("TestArea", 3, 3, 0));
            var area = new NodeArea("TestArea");
            area.AddGrid(grid);
            var nh = new NodeHandler(autoLoad: false);
            nh.AddArea(area);
            NodeHandler.SetCurrent(nh);
            InputFuncs.NodeHandlerFactory = () => nh;

            var conn = Conn();
            var hk = Handshake(conn);
            new InputFuncs().MapValidateMovesHandler(conn,
                [hk, 1, new List<object?> { new List<object?> { 3, 3, 4, 3 } }], []);
            var listVerdict = conn.Sent[^1];

            var conn2 = Conn();
            var hk2 = Handshake(conn2);
            new InputFuncs().MapValidateMovesHandler(conn2, [hk2, 1, Je("[[3,3,4,3]]")], []);
            var jsonVerdict = conn2.Sent[^1];

            Assert.Equal(listVerdict.Cmd, jsonVerdict.Cmd);
            Assert.Equal(Denied(listVerdict), Denied(jsonVerdict));
        }
        finally { ResetChains(); }
    }

    private static List<int> Denied((string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs) sent)
    {
        if (sent.Args.Count > 2 && sent.Args[2] is List<int> ints) return ints;
        if (sent.Args.Count > 2 && sent.Args[2] is List<object?> objs) return objs.Select(o => Convert.ToInt32(o)).ToList();
        return [];
    }

    [Fact]
    public void Dispatch_StripEnabled_StripsNestedListsIntoFreshLists()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            var captured = new List<(List<object?> Args, Dictionary<string, object?> Kwargs)>();
            var gate = new object();
            mgr.RegisterHandler("strip_probe",
                new Action<BaseConnection, List<object?>, Dictionary<string, object?>>((c, a, k) =>
                {
                    lock (gate) captured.Add((a, k));
                }));
            var conn = new TestConnection();
            string esc = ((char)27).ToString();
            var nested = new List<object?> { esc + "[1mx" + esc + "[0m" };
            var args = new List<object?> { esc + "[31mhi" + esc + "[0m", nested };
            var kwargs = new Dictionary<string, object?> { ["k"] = esc + "[3my" + esc + "[0m" };
            mgr.Dispatch(conn, "strip_probe", args, kwargs);
            Assert.True(PortedHelpers.WaitFor(() =>
            {
                lock (gate) return captured.Count > 0;
            }, 5000));
            List<object?> gotArgs;
            Dictionary<string, object?> gotKwargs;
            lock (gate) (gotArgs, gotKwargs) = captured[0];
            Assert.Equal("hi", gotArgs[0]);
            var gotNested = Assert.IsType<List<object?>>(gotArgs[1]);
            Assert.Equal("x", gotNested[0]);
            Assert.Equal("y", gotKwargs["k"]);
            Assert.NotSame(args, gotArgs);
            Assert.NotSame(nested, gotNested);
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }
}
