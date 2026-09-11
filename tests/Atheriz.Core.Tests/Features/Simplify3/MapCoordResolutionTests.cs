// Pins for the MapHandler single-fetch pair: legend coord resolution through
// the location reference (node target, coord-carrying carrier fallback,
// missing target) and mapable/listener coord extraction with the node-self
// fallback preserved.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class MapCoordResolutionTests
{
    private sealed class LegendListener : GameObject
    {
        public List<(string sym, string desc, (int x, int y) coord)> SeenEntries = new();
        public override void AtLegendUpdate(List<(string sym, string desc, (int x, int y) coord)> e, bool show, string area)
            => SeenEntries.AddRange(e);
    }

    private static MapInfo RenderWith(params GameObject[] placed)
    {
        var mi = new MapInfo();
        foreach (var o in placed)
            mi.Objects[o.Id] = o;
        var listener = new LegendListener { Id = 77 };
        mi.AddListener(listener);
        mi.RenderLegend();
        return mi;
    }

    private static LegendListener SoleListener(MapInfo mi)
        => Assert.IsType<LegendListener>(Assert.Single(mi.Listeners.Values));

    [Fact]
    public void RenderLegend_ObjectLocationOfNode_EmitsNodeCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("area1", 3, 4, 0));
        ObjectRegistry.AddObject(node);
        var prop = GameObject.Create("prop", isItem: true);
        prop.Symbol = "@";
        ObjectRegistry.AddObject(prop);
        prop.Location = new LocationRef.ObjectLocation(node.Id);

        var mi = RenderWith(prop);
        Assert.Contains(SoleListener(mi).SeenEntries, e => e.coord == (3, 4) && e.desc == "prop");
    }

    [Fact]
    public void RenderLegend_ObjectLocationOfCoordCarrier_UsesCarrierCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var carrier = GameObject.Create("carrier", isItem: true);
        ObjectRegistry.AddObject(carrier);
        carrier.Location = new LocationRef.CoordLocation(new Coord("area1", 7, 8, 0));
        var rider = GameObject.Create("rider", isItem: true);
        rider.Symbol = "@";
        ObjectRegistry.AddObject(rider);
        rider.Location = new LocationRef.ObjectLocation(carrier.Id);

        // The carrier is not a node, so the entry falls back to the
        // carrier's own coord location instead of resolving node coords.
        var mi = RenderWith(rider);
        Assert.Contains(SoleListener(mi).SeenEntries, e => e.coord == (7, 8) && e.desc == "rider");
    }

    [Fact]
    public void RenderLegend_MissingTarget_EmitsNoEntry()
    {
        using var env = GlobalTestEnv.Enter();
        var prop = GameObject.Create("prop", isItem: true);
        prop.Symbol = "@";
        ObjectRegistry.AddObject(prop);
        prop.Location = new LocationRef.ObjectLocation(-999);

        var mi = RenderWith(prop);
        Assert.Empty(SoleListener(mi).SeenEntries);
    }

    [Fact]
    public void AddMapable_ObjectLocationOfNode_ResolvesNodeCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad: false);
        var node = new Node(new Coord("area1", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var obj = GameObject.Create("a");
        ObjectRegistry.AddObject(obj);
        obj.Location = new LocationRef.ObjectLocation(node.Id);

        handler.AddMapable(obj);
        var mi = handler.GetMapInfo("area1", 0);
        Assert.NotNull(mi);
        Assert.Equal(obj, mi!.Objects[obj.Id]);
    }

    [Fact]
    public void AddMapable_NodeSelfWithStaleObjectLocation_UsesOwnCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad: false);
        var node = new Node(new Coord("area9", 1, 2, 0));
        ObjectRegistry.AddObject(node);
        node.Location = new LocationRef.ObjectLocation(-999);

        // No registry target: extraction falls back to the node's own coord.
        handler.AddMapable(node);
        Assert.True(handler.Snapshot().ContainsKey(("area9", 0)));
    }

    [Fact]
    public void AddMapable_MissingTargetNonNode_IsNoOp()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad: false);
        var obj = GameObject.Create("a");
        ObjectRegistry.AddObject(obj);
        obj.Location = new LocationRef.ObjectLocation(-999);

        handler.AddMapable(obj);
        Assert.Empty(handler.Snapshot());
    }
}
