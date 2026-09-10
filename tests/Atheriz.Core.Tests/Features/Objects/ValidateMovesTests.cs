using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Move validation without the redundant Take(i) scan: duplicate sources fail,
// unknown sources fail, blocked destinations fail, and destinations vacated
// by a fellow move stay legal.
[Collection("Ported")]
public class ValidateMovesTests
{
    private static NodeGrid Setup(out Node a, out Node b)
    {
        var grid = new NodeGrid("mvarea", 0);
        a = new Node(new Coord("mvarea", 0, 0, 0));
        b = new Node(new Coord("mvarea", 1, 0, 0));
        grid.AddNode(a);
        grid.AddNode(b);
        return grid;
    }

    [Fact]
    public void SingleLegalMove_Passes()
    {
        var grid = Setup(out _, out _);
        var failed = grid.CheckMoves([((0, 0), (5, 5))]);
        Assert.Empty(failed);
    }

    [Fact]
    public void DuplicateSource_BothFail()
    {
        // The old Take(i) arm and the count arm agreed here; the count arm
        // alone decides the same outcome.
        var grid = Setup(out _, out _);
        var failed = grid.CheckMoves([((0, 0), (5, 5)), ((0, 0), (6, 6))]);
        Assert.Equal([0, 1], failed.OrderBy(i => i).ToList());
    }

    [Fact]
    public void UnknownSource_Fails()
    {
        var grid = Setup(out _, out _);
        var failed = grid.CheckMoves([((9, 9), (5, 5))]);
        Assert.Equal([0], failed.OrderBy(i => i).ToList());
    }

    [Fact]
    public void BlockedDestination_Fails()
    {
        var grid = Setup(out _, out _);
        var blocker = new Node(new Coord("mvarea", 3, 3, 0));
        grid.AddNode(blocker);
        var failed = grid.CheckMoves([((0, 0), (3, 3))]);
        Assert.Equal([0], failed.OrderBy(i => i).ToList());
    }

    [Fact]
    public void DestinationVacatedByFellowMove_Passes()
    {
        // (1,0) is occupied but is itself a move source, so the dst check
        // (backed by the kept sourceSet) lets the chain through.
        var grid = Setup(out _, out _);
        var failed = grid.CheckMoves([((0, 0), (1, 0)), ((1, 0), (2, 0))]);
        Assert.Empty(failed);
    }
}
