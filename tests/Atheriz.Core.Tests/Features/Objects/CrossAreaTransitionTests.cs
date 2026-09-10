using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Cross-area transition sync keeps direction: node adds publish transitions
// for cross-area links, node removals withdraw them, same-area links never do.
[Collection("Ported")]
public class CrossAreaTransitionTests
{
    private static (NodeHandler nh, NodeGrid gridA) Setup()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var gridA = new NodeGrid("crossA", 0);
        return (nh, gridA);
    }

    [Fact]
    public void AddNode_PublishesCrossAreaTransition()
    {
        var (nh, gridA) = Setup();
        try
        {
            var node = new Node(new Coord("crossA", 0, 0, 0));
            node.AddLink(new NodeLink("east", new Coord("crossB", 3, 0, 0)));
            gridA.AddNode(node);
            var edges = nh.FindTransitions(fromArea: "crossA");
            Assert.Single(edges);
            Assert.Equal(new Coord("crossA", 0, 0, 0), edges[0].FromCoord);
            Assert.Equal(new Coord("crossB", 3, 0, 0), edges[0].ToCoord);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void RemoveNode_WithdrawsCrossAreaTransition()
    {
        var (nh, gridA) = Setup();
        try
        {
            var node = new Node(new Coord("crossA", 0, 0, 0));
            node.AddLink(new NodeLink("east", new Coord("crossB", 3, 0, 0)));
            gridA.AddNode(node);
            Assert.Single(nh.FindTransitions(fromArea: "crossA"));
            gridA.RemoveNode((0, 0));
            Assert.Empty(nh.FindTransitions(fromArea: "crossA"));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void SameAreaLink_PublishesNothing()
    {
        var (nh, gridA) = Setup();
        try
        {
            var node = new Node(new Coord("crossA", 0, 0, 0));
            node.AddLink(new NodeLink("east", new Coord("crossA", 1, 0, 0)));
            gridA.AddNode(node);
            Assert.Empty(nh.FindTransitions(fromArea: "crossA"));
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
