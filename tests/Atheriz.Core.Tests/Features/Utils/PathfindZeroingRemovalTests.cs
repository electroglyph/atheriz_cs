// Pins for AStar without the constructor-duplicating G/H/F re-zeroing: fresh
// nodes start at zero by construction, so shortest-path optimality,
// start-equals-end, and unreachable outcomes are unchanged.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

[Collection("Ported")]
public sealed class PathfindZeroingRemovalTests
{
    private static NodeHandler SetupChain(out Node start, out Node end)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("B22Path");
        var grid = new NodeGrid("B22Path", 0);
        var a = new Node(new Coord("B22Path", 0, 0, 0));
        var b = new Node(new Coord("B22Path", 1, 0, 0));
        var c = new Node(new Coord("B22Path", 2, 0, 0));
        var detour = new Node(new Coord("B22Path", 0, 1, 0));
        var detour2 = new Node(new Coord("B22Path", 1, 1, 0));
        var detour3 = new Node(new Coord("B22Path", 2, 1, 0));
        grid.Nodes[(0, 0)] = a;
        grid.Nodes[(1, 0)] = b;
        grid.Nodes[(2, 0)] = c;
        grid.Nodes[(0, 1)] = detour;
        grid.Nodes[(1, 1)] = detour2;
        grid.Nodes[(2, 1)] = detour3;
        a.AddLink(new NodeLink("east", b.Coord, ["e"]));
        b.AddLink(new NodeLink("west", a.Coord, ["w"]));
        b.AddLink(new NodeLink("east", c.Coord, ["e"]));
        c.AddLink(new NodeLink("west", b.Coord, ["w"]));
        a.AddLink(new NodeLink("north", detour.Coord, ["n"]));
        detour.AddLink(new NodeLink("south", a.Coord, ["s"]));
        detour.AddLink(new NodeLink("east", detour2.Coord, ["e"]));
        detour2.AddLink(new NodeLink("west", detour.Coord, ["w"]));
        detour2.AddLink(new NodeLink("east", detour3.Coord, ["e"]));
        detour3.AddLink(new NodeLink("west", detour2.Coord, ["w"]));
        detour3.AddLink(new NodeLink("south", c.Coord, ["s"]));
        c.AddLink(new NodeLink("north", detour3.Coord, ["n"]));
        area.AddGrid(grid);
        nh.AddArea(area);
        start = a;
        end = c;
        return nh;
    }

    [Fact]
    public void AStar_OpenGridWithLongerAlternative_ReturnsShortestPath()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = SetupChain(out var start, out var end);
        try
        {
            var (found, path, _) = Pathfind.AStar(start, end, null, nh);
            Assert.True(found);
            List<Coord> expected = [start.Coord, new Coord("B22Path", 1, 0, 0), end.Coord];
            Assert.Equal(expected, path.Select(n => n.Coord).ToList());
            var coords = Pathfind.FindPath(start.Coord, end.Coord, nh);
            Assert.NotNull(coords);
            Assert.Equal(3, coords!.Count);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
        }
    }

    [Fact]
    public void AStar_StartEqualsEnd_ReturnsSingleNodePath()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = SetupChain(out var start, out _);
        try
        {
            var (found, path, _) = Pathfind.AStar(start, start, null, nh);
            Assert.True(found);
            var single = Assert.Single(path);
            Assert.Same(start, single);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
        }
    }

    [Fact]
    public void AStar_UnreachableNode_ReturnsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = SetupChain(out var start, out _);
        try
        {
            var island = new Node(new Coord("B22Path", 9, 9, 0));
            nh.AddNode(island);
            var (found, path, _) = Pathfind.AStar(start, island, null, nh);
            Assert.False(found);
            Assert.Empty(path);
            Assert.Null(Pathfind.FindPath(start.Coord, island.Coord, nh));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
        }
    }
}
