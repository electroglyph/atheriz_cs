// Pins: RemoveByTag atomic eviction, loud cell reject with the failing
// index, long legend coords.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class CmdSetAndMapEditTests
{
    private sealed class TaggedCommand : Command
    {
        private readonly string _key;
        public TaggedCommand(string key) => _key = key;
        public override string Key => _key;
        public override void Run(IMessageTarget caller, object? args) => caller.Msg("ok");
    }

    [Fact]
    public void RemoveByTag_RemovesTagged_KeepsUntagged()
    {
        var cs = new CmdSet();
        var a = new TaggedCommand("gone_a");
        var b = new TaggedCommand("gone_b");
        var plain = new TaggedCommand("plain");
        cs.Add(a, tag: "gone");
        cs.Add(b, tag: "gone");
        cs.Add(plain);
        cs.RemoveByTag("gone");
        Assert.Null(cs.Get("gone_a"));
        Assert.Null(cs.Get("gone_b"));
        Assert.Same(plain, cs.Get("plain"));
        Assert.DoesNotContain("gone_a", cs.GetSortedKeys());
    }

    [Fact]
    public async Task RemoveByTag_RacingAdds_ConvergesWithNoLeftovers()
    {
        var cs = new CmdSet();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var writers = Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
                cs.Add(new TaggedCommand($"race_{w}_{i}"), tag: "gone");
        }, cts.Token)).ToArray();
        while (!writers.All(t => t.IsCompleted))
            cs.RemoveByTag("gone");
        await Task.WhenAll(writers).WaitAsync(cts.Token);
        cs.RemoveByTag("gone");
        Assert.DoesNotContain(cs.GetSortedKeys(), k => k.StartsWith("race_", StringComparison.Ordinal));
    }

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

    [Fact]
    public void MapEditHandler_ScalarFgStyledCell_RejectsLoudly()
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
            conn.ClearSent();
            var cells = new List<object?>
            {
                new List<object?> { 0, 0, "@", 200, new List<object?> { 1, 2, 3 }, new List<object?>() },
            };
            new InputFuncs().MapEditHandler(conn, [hk, 1, cells], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
            Assert.Equal("Invalid map edit cell at index 0.", conn.Sent[0].Args[0]);
            Assert.Empty(mi.PreGrid);
        }
        finally { ResetChains(); }
    }

    [Fact]
    public void MapEditLegendHandler_LongCoord_Accepts()
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
            var key = MapEdit.Grant("10.0.0.1", "TestArea", 0);
            new InputFuncs().MapEditLegendHandler(conn, [key, 0, new List<object?>()], []);
            var hk = conn.Sent[^1].Args[1] as string ?? throw new InvalidOperationException("no handshake key");
            conn.ClearSent();
            var legend = new List<object?>
            {
                new Dictionary<string, object?> { ["symbol"] = "@", ["coord"] = new List<object?> { 2L, 3L } },
            };
            new InputFuncs().MapEditLegendHandler(conn, [hk, 1, legend], []);
            Assert.Equal("legend_ok", conn.Sent[^1].Cmd);
            Assert.Equal((2, 3), mi.LegendEntries[0].Coord);
        }
        finally { ResetChains(); }
    }
}
