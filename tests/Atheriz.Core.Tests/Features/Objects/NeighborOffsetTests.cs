using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Neighbor offsets come from one shared private table: tuple and Coord
// overloads agree, all six faces resolve, and missing faces are skipped.
[Collection("Ported")]
public class NeighborOffsetTests
{
    private static NodeArea Setup(out Coord center)
    {
        center = new Coord("nbarea", 0, 0, 0);
        var area = new NodeArea("nbarea");
        var grid = new NodeGrid("nbarea", 0);
        area.AddGrid(grid);
        grid.AddNode(new Node(center));
        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0) })
            grid.AddNode(new Node(new Coord("nbarea", dx, dy, dz)));
        return area;
    }

    [Fact]
    public void TupleAndCoordOverloads_Agree()
    {
        var area = Setup(out var center);
        var byTuple = area.GetNeighbors((center.X, center.Y, center.Z));
        var byCoord = area.GetNeighbors(center);
        Assert.Equal(byTuple.Count, byCoord.Count);
        Assert.Equal(
            byTuple.Select(n => n.Coord).OrderBy(c => c.ToString()).ToList(),
            byCoord.Select(n => n.Coord).OrderBy(c => c.ToString()).ToList());
    }

    [Fact]
    public void FourFacesPresent_TwoFacesSkipped()
    {
        var area = Setup(out var center);
        var neighbors = area.GetNeighbors(center);
        Assert.Equal(4, neighbors.Count);
        var coords = neighbors.Select(n => (n.Coord.X, n.Coord.Y, n.Coord.Z)).ToHashSet();
        Assert.Contains((1, 0, 0), coords);
        Assert.Contains((-1, 0, 0), coords);
        Assert.Contains((0, 1, 0), coords);
        Assert.Contains((0, -1, 0), coords);
    }

    [Fact]
    public void MissingGridLevel_Skipped()
    {
        var area = new NodeArea("flat");
        area.AddGrid(new NodeGrid("flat", 0));
        var lone = new Node(new Coord("flat", 5, 5, 0));
        area.GetOrCreateGrid(0).AddNode(lone);
        Assert.Empty(area.GetNeighbors(new Coord("flat", 5, 5, 0)));
    }
}
