using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Grid value replacement and move application: ReplaceNodeValue swaps by id
// (keeping its Keys snapshot), ApplyMoves relocates nodes and rewrites
// in-grid link targets without a snapshot.
[Collection("Ported")]
public class GridReplaceApplyTests
{
    [Fact]
    public void ReplaceNodeValue_SwapsSameIdInstance()
    {
        var grid = new NodeGrid("rarea", 0);
        var original = new Node(new Coord("rarea", 0, 0, 0));
        grid.AddNode(original);
        var replacement = new Node(new Coord("rarea", 0, 0, 0));
        replacement.Id = original.Id;
        grid.ReplaceNodeValue(replacement);
        Assert.Same(replacement, grid.GetNode((0, 0)));
    }

    [Fact]
    public void ReplaceNodeValue_LeavesOtherIdsAlone()
    {
        var grid = new NodeGrid("rarea", 0);
        var keeper = new Node(new Coord("rarea", 1, 0, 0));
        grid.AddNode(keeper);
        var stranger = new Node(new Coord("rarea", 0, 0, 0));
        grid.AddNode(stranger);
        var replacement = new Node(new Coord("rarea", 9, 9, 0));
        replacement.Id = stranger.Id;
        grid.ReplaceNodeValue(replacement);
        Assert.Same(keeper, grid.GetNode((1, 0)));
        Assert.Same(replacement, grid.GetNode((0, 0)));
    }

    [Fact]
    public void ApplyMoves_RelocatesAndRewritesLinks()
    {
        var grid = new NodeGrid("rarea", 0);
        var mover = new Node(new Coord("rarea", 0, 0, 0));
        var watcher = new Node(new Coord("rarea", 1, 0, 0));
        watcher.AddLink(new NodeLink("west", new Coord("rarea", 0, 0, 0)));
        grid.AddNode(mover);
        grid.AddNode(watcher);
        var failed = grid.ApplyMoves([((0, 0), (0, 1))]);
        Assert.Empty(failed);
        Assert.Null(grid.GetNode((0, 0)));
        Assert.Same(mover, grid.GetNode((0, 1)));
        Assert.Equal(new Coord("rarea", 0, 1, 0), mover.Coord);
        var link = Assert.Single(watcher.GetLinks());
        Assert.Equal(new Coord("rarea", 0, 1, 0), link.Coord);
    }
}
