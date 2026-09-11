// Pins for the legend single-parse entry: the handler validates the whole
// payload before consuming the edit chain, so payload rejects win over chain
// rejects, entries fail in index order, and a retry ack carries no legend_ok.
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class LegendRejectPrecedenceTests
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

    private static Dictionary<string, object?> Entry(string symbol) =>
        new() { ["symbol"] = symbol };

    [Fact]
    public void MapEditLegendHandler_InvalidEntryWithUnknownKey_ReportsEntryErrorFirst()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var conn = Conn();
            new InputFuncs().MapEditLegendHandler(conn,
                ["bogus-key", 1, new List<object?> { Entry("") }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid legend entry at index 0.", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_SecondBadEntry_ReportsItsIndex()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var conn = Conn();
            new InputFuncs().MapEditLegendHandler(conn,
                ["bogus-key", 1, new List<object?> { Entry("@"), Entry("") }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid legend entry at index 1.", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_ValidPayloadWithUnknownKey_ReportsChainError()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var conn = Conn();
            new InputFuncs().MapEditLegendHandler(conn,
                ["bogus-key", 1, new List<object?> { Entry("@") }], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("unknown_key", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_TooManyEntries_RejectsBeforeConsume()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var many = new List<object?>();
            for (int i = 0; i < 201; i++) many.Add(Entry("@"));
            var conn = Conn();
            new InputFuncs().MapEditLegendHandler(conn, ["bogus-key", 1, many], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Too many legend entries (max 200).", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_TwoHundredEntries_PassesCountCheck()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var many = new List<object?>();
            for (int i = 0; i < 200; i++) many.Add(Entry("@"));
            var conn = Conn();
            new InputFuncs().MapEditLegendHandler(conn, ["bogus-key", 1, many], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("unknown_key", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_ShortArgs_RejectsPayload()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var conn = Conn();
            new InputFuncs().MapEditLegendHandler(conn, ["k", 1], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid legend payload.", conn.Sent[0].Args[0]);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_JsonElementEntries_ApplyOnceAndRetryAcksOnly()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        try
        {
            var mh = GlobalServices.GetMapHandler();
            var mi = new MapInfo("TestArea");
            mh.SetMapInfo("TestArea", 0, mi);
            InputFuncs.MapHandlerFactory = () => mh;
            var key = MapEdit.Grant("10.0.0.1", "TestArea", 0);
            var conn = Conn();
            new InputFuncs().MapEditLegendHandler(conn, [key, 0, new List<object?>()], []);
            var hk = conn.Sent[0].Args[1] as string;
            Assert.NotNull(hk);
            conn.ClearSent();
            var legend = new List<object?> { Je("{\"symbol\":\"@\",\"desc\":\"hero\",\"coord\":[2,3],\"show\":true}") };
            new InputFuncs().MapEditLegendHandler(conn, [hk!, 1, legend], []);
            Assert.Equal(2, conn.Sent.Count);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Equal(1, conn.Sent[0].Args[0]);
            Assert.Equal("legend_ok", conn.Sent[1].Cmd);
            Assert.Single(mi.LegendEntries);
            Assert.Equal("@", mi.LegendEntries[0].Symbol);
            Assert.Equal("hero", mi.LegendEntries[0].Desc);
            Assert.Equal((2, 3), mi.LegendEntries[0].Coord);
            // Replaying the same key+seq is a retry: ack only, no re-apply.
            conn.ClearSent();
            new InputFuncs().MapEditLegendHandler(conn, [hk!, 1, legend], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_ack", conn.Sent[0].Cmd);
            Assert.Single(mi.LegendEntries);
        }
        finally { ResetChains(); }
    }
}
