using Atheriz.Core.Network;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// MapEdit single-parse validation: any malformed cell anywhere rejects
// loudly with its index (no silent drops), while shape-valid draw and
// room cells pass validation and reach consume (room cells never touch
// drawing).
[Collection("Ported")]
public sealed class MapEditCellParsingTests
{
    private static void CallMapEdit(TestConnection conn, List<object?> cells) =>
        new InputFuncs().MapEditHandler(conn, ["nope", 1, cells], []);

    [Fact]
    public void MalformedCell_AtAnyPosition_RejectsWithIndex()
    {
        using var env = GlobalTestEnv.Enter();
        var badShapes = new List<List<object?>>
        {
            new List<object?>(), // empty
            new List<object?> { 1, 2 }, // wrong count
            new List<object?> { 1, 2, "x", 1, 2, 3, 4 }, // wrong count
            new List<object?> { "room", 1, 2, 3 }, // room needs 5
            new List<object?> { "room", 1, 2, 3, "x" }, // room coord not int
            new List<object?> { 1, "y", "x" }, // coord not int
            new List<object?> { 1, 2, 5 }, // symbol not string
            new List<object?> { 1, 2, "x", 1, 2, new List<object?> { "blink" } }, // bad attrs
        };
        foreach (var bad in badShapes)
        {
            // Bad cell first, middle, and last all reject with the bad index.
            var arrangements = new List<(List<object?> cells, int badIndex)>
            {
                (new List<object?> { bad, new List<object?> { 1, 2, "ok" } }, 0),
                (new List<object?> { new List<object?> { 1, 2, "ok" }, bad }, 1),
                (new List<object?> { new List<object?> { 1, 2, "ok" }, bad, new List<object?> { 3, 4, "ok2" } }, 1),
            };
            foreach (var (cells, badIndex) in arrangements)
            {
                var conn = new TestConnection();
                CallMapEdit(conn, cells);
                var sent = Assert.Single(conn.Sent);
                Assert.Equal("map_edit_reject", sent.Cmd);
                Assert.Equal($"Invalid map edit cell at index {badIndex}.", sent.Args[0]);
            }
        }
    }

    [Fact]
    public void ValidDrawAndRoomCells_PassValidation_ReachingConsume()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        CallMapEdit(conn, [
            new List<object?> { 1, 2, "x" },
            new List<object?> { "room", 1, 2, 3, 4 },
            new List<object?> { 5, 6, "y", new List<object?> { 1, 2, 3 }, new List<object?> { -1, -1, -1 }, new List<object?> { "bold" } },
        ]);
        // Validation passed, so consume ran and rejected the unknown key
        // (instead of the silent validation return).
        Assert.Single(conn.Sent);
        Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
        Assert.Equal("unknown_key", conn.Sent[0].Args[0]);
    }

    [Fact]
    public void EmptySymbolCell_PassesValidation()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        CallMapEdit(conn, [new List<object?> { 1, 2, "" }]);
        Assert.Single(conn.Sent);
        Assert.Equal("map_edit_reject", conn.Sent[0].Cmd);
    }
}
