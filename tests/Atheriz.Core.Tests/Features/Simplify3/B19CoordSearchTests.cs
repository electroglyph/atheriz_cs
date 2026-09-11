// Pins for simplify3 §4 coord-search index path: grafted nodes resolve
// through the handler index and ungrafted nodes through the scan fallback
// with the same single-or-empty result shape.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B19CoordSearchTests
{
    [Fact]
    public void SearchWithFallback_GraftedNode_FindsViaIndex()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var coord = new Coord("b19graft", 1, 2, 3);
            var node = new Node(coord);
            nh.AddNode(node);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);

            var found = CommandHelpers.SearchWithFallback(caller, "(b19graft,1,2,3)");

            Assert.Single(found);
            Assert.Same(node, found[0]);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void SearchWithFallback_UngraftedNode_FindsViaFallbackScan()
    {
        using var env = GlobalTestEnv.Enter();
        NodeHandler.SetCurrent(null);
        var coord = new Coord("b19lone", 4, 5, 6);
        var node = new Node(coord);
        ObjectRegistry.AddObject(node);
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);

        var found = CommandHelpers.SearchWithFallback(caller, "(b19lone,4,5,6)");

        Assert.Single(found);
        Assert.Same(node, found[0]);
    }

    [Fact]
    public void SearchWithFallback_UnknownCoord_ReturnsEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("b19known", 0, 0, 0));
            nh.AddNode(node);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);

            Assert.Empty(CommandHelpers.SearchWithFallback(caller, "(b19known,9,9,9)"));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void SearchWithFallback_ParenSpacingVariants_FindSameNode()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var coord = new Coord("b19space", 7, 8, 9);
            var node = new Node(coord);
            nh.AddNode(node);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);

            var tight = CommandHelpers.SearchWithFallback(caller, "(b19space,7,8,9)");
            var spaced = CommandHelpers.SearchWithFallback(caller, "( b19space , 7 , 8 , 9 )");

            Assert.Single(tight);
            Assert.Same(node, tight[0]);
            Assert.Single(spaced);
            Assert.Same(node, spaced[0]);
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
