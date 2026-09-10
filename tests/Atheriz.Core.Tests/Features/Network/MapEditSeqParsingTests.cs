using System.Text.Json;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// Seq coercion across the map_edit family: int/long/JsonElement numbers
// parse identically, while each command keeps its own failure path
// (map_edit and map_validate_moves stay silent; map_edit_legend rejects).
[Collection("Ported")]
public sealed class MapEditSeqParsingTests
{
    private static JsonElement Je(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static List<object?> DrawCells() =>
        [new List<object?> { 1, 2, "x" }];

    [Fact]
    public void MapEdit_AcceptsIntLongAndJsonSeq_SilentlyParses()
    {
        using var env = GlobalTestEnv.Enter();
        var funcs = new InputFuncs();
        // Accepted seqs pass validation and reach consume, which rejects the
        // unknown key (proving the seq parsed instead of silent-returning).
        foreach (object? seq in new object?[] { 1, (long)2, Je("3") })
        {
            var conn = new TestConnection();
            funcs.MapEditHandler(conn, ["nope", seq, DrawCells()], []);
            Assert.Single(conn.Sent);
            Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
        }
    }

    [Fact]
    public void MapEdit_BadSeq_SilentReturn()
    {
        using var env = GlobalTestEnv.Enter();
        var funcs = new InputFuncs();
        foreach (object? seq in new object?[] { "3", 3.5, Je("\"3\""), null })
        {
            var conn = new TestConnection();
            funcs.MapEditHandler(conn, ["nope", seq, DrawCells()], []);
            Assert.Empty(conn.Sent);
        }
    }

    [Fact]
    public void MapValidateMoves_SeqParses_SilentOnBad()
    {
        using var env = GlobalTestEnv.Enter();
        var funcs = new InputFuncs();
        List<object?> Moves() => [new List<object?> { 0, 0, 1, 1 }];
        var ok = new TestConnection();
        funcs.MapValidateMovesHandler(ok, ["nope", 1, Moves()], []);
        Assert.Single(ok.Sent);
        Assert.Equal("map_edit_reject", ok.Sent[0].Cmd);
        var bad = new TestConnection();
        funcs.MapValidateMovesHandler(bad, ["nope", "1", Moves()], []);
        Assert.Empty(bad.Sent);
    }

    [Fact]
    public void MapEditLegend_BadSeq_RejectsInvalidPayload()
    {
        using var env = GlobalTestEnv.Enter();
        var funcs = new InputFuncs();
        var conn = new TestConnection();
        funcs.MapEditLegendHandler(conn, ["k", "1", new List<object?>()], []);
        Assert.Single(conn.Sent);
        Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
        Assert.Equal("Invalid legend payload.", conn.Sent[0].Args[0]);
    }

    [Fact]
    public void MapEditLegend_GoodSeq_ReachesConsume()
    {
        using var env = GlobalTestEnv.Enter();
        var funcs = new InputFuncs();
        // Empty legend is shape-valid, so a good seq reaches consume and the
        // unknown key (not the payload) is the reject reason.
        var conn = new TestConnection();
        funcs.MapEditLegendHandler(conn, ["nope", Je("7"), new List<object?>()], []);
        Assert.Single(conn.Sent);
        Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
        Assert.Equal("unknown_key", conn.Sent[0].Args[0]);
    }
}
