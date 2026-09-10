// Pins for the maze backtrack rewrite (RemoveAt instead of Take().ToList()):
// same paths, fewer allocs — including the degenerate 1x1 grid where the old
// Take(-1) terminated and a bare RemoveAt would throw.
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class MazeBacktrackTests
{
    [Fact]
    public void CreateMaze_DegenerateSizes_Terminate()
    {
        using var env = GlobalTestEnv.Enter();
        var tiny = MazeCommand.CreateMaze(1, 1);
        Assert.Empty(tiny);
        var small = MazeCommand.CreateMaze(2, 2);
        Assert.NotNull(small);
    }

    [Fact]
    public void CreateMaze_EdgesAreInBoundsAndAdjacent()
    {
        using var env = GlobalTestEnv.Enter();
        const int w = 6;
        const int h = 6;
        var maze = MazeCommand.CreateMaze(w, h);
        Assert.NotEmpty(maze);
        foreach (var (cell, neighbors) in maze)
        {
            Assert.InRange(cell.Item1, 0, w - 1);
            Assert.InRange(cell.Item2, 0, h - 1);
            foreach (var next in neighbors)
            {
                Assert.InRange(next.Item1, 0, w - 1);
                Assert.InRange(next.Item2, 0, h - 1);
                Assert.Equal(1, Math.Abs(next.Item1 - cell.Item1) + Math.Abs(next.Item2 - cell.Item2));
            }
        }
    }

    [Fact]
    public void GenMapAndGrid_NodeCountMatchesMaze_LinkTargetsInBounds()
    {
        using var env = GlobalTestEnv.Enter();
        const int w = 4;
        const int h = 4;
        var (map, grid) = MazeCommand.GenMapAndGrid(w, h, "backtrack-sym");
        // One node per maze key (dead-end cells carry no outgoing edge and
        // get no node — the backtrack change must not alter that shape).
        Assert.Equal(map.Count, grid.Nodes.Count);
        foreach (var kv in grid.Nodes.ToList())
        {
            var (x, y) = kv.Key;
            var node = grid.GetNode(x, y);
            Assert.NotNull(node);
            foreach (var link in node.GetLinks())
            {
                Assert.InRange(link.Coord.X, 0, w - 1);
                Assert.InRange(link.Coord.Y, 0, h - 1);
            }
        }
    }
}
