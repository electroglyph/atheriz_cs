// Maze visited set: generation stays valid with the HashSet-visited shape,
// and every neighbor cell is still claimed exactly once.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class MazeVisitedSetTests
{
    [Fact]
    public void CreateMaze_GenerationValid_NonEmptyWithAdjacentEdges()
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
    public void CreateMaze_VisitedSemantics_NeighborCellsClaimedOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var maze = MazeCommand.CreateMaze(6, 6);
        var seen = new HashSet<(int, int)>();
        foreach (var neighbors in maze.Values)
            foreach (var next in neighbors)
                Assert.True(seen.Add(next), $"cell {next} claimed twice");
    }
}
