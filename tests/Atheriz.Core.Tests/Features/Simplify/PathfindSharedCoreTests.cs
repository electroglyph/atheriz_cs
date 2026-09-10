using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared link snapshot, iteration-cap resolution, and folded child loop:
// optimal paths are preserved and the shared cap override is honored.
[Collection("Ported")]
public class PathfindSharedCoreTests
{
    private static (Coord a, Coord b, NodeHandler nh) MakeLinkedPair()
    {
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var coordA = new Coord("simplify", 0, 0, 0);
        var coordB = new Coord("simplify", 2, 0, 0);
        var a = new Node(coordA);
        var b = new Node(coordB);
        if (ObjectRegistry.Get(a.Id).Count == 0) ObjectRegistry.AddObject(a);
        if (ObjectRegistry.Get(b.Id).Count == 0) ObjectRegistry.AddObject(b);
        a.AddLink(new NodeLink("east", coordB, new List<string> { "e" }));
        b.AddLink(new NodeLink("west", coordA, new List<string> { "w" }));
        nh.AddNode(a);
        nh.AddNode(b);
        return (coordA, coordB, nh);
    }

    [Fact]
    public void AStar_DirectLink_ReturnsOptimalTwoNodePath()
    {
        var (coordA, coordB, nh) = MakeLinkedPair();
        try
        {
            var start = nh.GetNode(coordA)!;
            var end = nh.GetNode(coordB)!;
            var (found, path, _) = Pathfind.AStar(start, end, null, nh);
            Assert.True(found);
            Assert.Equal(new[] { coordA, coordB }, path.Select(n => n.Coord).ToArray());
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void FindPath_AgreesWithAStar_OnSharedSnapshot()
    {
        var (coordA, coordB, nh) = MakeLinkedPair();
        try
        {
            Assert.Equal(new[] { coordA, coordB }, Pathfind.FindPath(coordA, coordB, nh));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void IterationCapOverride_ZeroDeniesEveryPath()
    {
        var (coordA, coordB, nh) = MakeLinkedPair();
        try
        {
            Assert.Null(Pathfind.FindPath(coordA, coordB, nh, 0));
            var start = nh.GetNode(coordA)!;
            var end = nh.GetNode(coordB)!;
            Assert.False(Pathfind.AStar(start, end, null, nh, 0).Found);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
