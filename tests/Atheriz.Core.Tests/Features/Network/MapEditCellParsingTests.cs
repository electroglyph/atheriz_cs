using Atheriz.Core.Network;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// MapEdit single-parse validation: any malformed cell anywhere is a silent
// return (no sends), while shape-valid draw and room cells pass validation
// and reach consume (room cells never touch drawing).
[Collection("Ported")]
public sealed class MapEditCellParsingTests
{
    private static void CallMapEdit(TestConnection conn, List<object?> cells) =>
        new InputFuncs().MapEditHandler(conn, ["nope", 1, cells], []);

    [Fact]
    public void MalformedCell_AtAnyPosition_SilentReturn()
    {
        using var env = GlobalTestEnv.Enter();
        var badShapes = new List<List<object?>>
        {
            [], // empty
            [1, 2], // wrong count
            [1, 2, "x", 1, 2, 3, 4], // wrong count
            ["room", 1, 2, 3], // room needs 5
            ["room", 1, 2, 3, "x"], // room coord not int
            [1, "y", "x"], // coord not int
            [1, 2, 5], // symbol not string
            [1, 2, "x", 1, 2, ["blink"]], // bad attrs
        };
        foreach (var bad in badShapes)
        {
            // Bad cell first, middle, and last all fail the same silent way.
            foreach (var cells in new List<List<object?>>
                     {
                         [bad, new List<object?> { 1, 2, "ok" }],
                         [new List<object?> { 1, 2, "ok" }, bad],
                         [new List<object?> { 1, 2, "ok" }, bad, new List<object?> { 3, 4, "ok2" }],
                     })
            {
                var conn = new TestConnection();
                CallMapEdit(conn, cells);
                Assert.Empty(conn.Sent);
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
