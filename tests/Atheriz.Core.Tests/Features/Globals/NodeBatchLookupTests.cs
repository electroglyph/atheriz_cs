using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The single-hold batch lookup preserves GetNodes order and null-skip:
// results follow the input order and unknown coords are dropped.
[Collection("Ported")]
public class NodeBatchLookupTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    [Fact]
    public void GetNodes_PreservesOrderAndSkipsMissing()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var n1 = new Node(new Coord("batcharea", 1, 1, 0));
            var n2 = new Node(new Coord("batcharea", 2, 2, 0));
            nh.AddNode(n1);
            nh.AddNode(n2);
            var got = nh.GetNodes([
                new Coord("batcharea", 2, 2, 0),
                new Coord("batcharea", 9, 9, 0),
                new Coord("batcharea", 1, 1, 0),
            ]);
            Assert.Equal(2, got.Count);
            Assert.Same(n2, got[0]);
            Assert.Same(n1, got[1]);
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    [Fact]
    public void GetNodes_EmptyInput_ReturnsEmpty()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            Assert.Empty(nh.GetNodes([]));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // The batch lookups take the most general sequence type: a lazily
    // generated coord stream binds with no List allocation at the call site.
    [Fact]
    public void GetNodes_AcceptsLazyEnumerable()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var n1 = new Node(new Coord("lazyarea", 1, 1, 0));
            nh.AddNode(n1);
            static IEnumerable<Coord> LazyCoords()
            {
                yield return new Coord("lazyarea", 1, 1, 0);
                yield return new Coord("lazyarea", 9, 9, 0);
            }
            var got = nh.GetNodes(LazyCoords());
            Assert.Single(got);
            Assert.Same(n1, got[0]);
            var area = nh.GetArea("lazyarea");
            Assert.NotNull(area);
            static IEnumerable<(int X, int Y, int Z)> LazyTuples()
            {
                yield return (1, 1, 0);
                yield return (9, 9, 0);
            }
            var gotArea = area.GetNodes(LazyTuples());
            Assert.Single(gotArea);
            Assert.Same(n1, gotArea[0]);
            static IEnumerable<Coord> LazyCoordObjs()
            {
                yield return new Coord("lazyarea", 1, 1, 0);
            }
            Assert.Single(area.GetNodes(LazyCoordObjs()));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }
}
