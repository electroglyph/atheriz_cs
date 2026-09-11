using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// RenderLegend fans out one shared entries list like Render: absentees share
// the instance, a present listener gets a private copy, and a hook mutation
// leaks to later listeners (same assumption Render accepts).
[Collection("Ported")]
public sealed class B15LegendShareTests
{
    private sealed class RecLegendListener : GameObject
    {
        public readonly List<List<(string sym, string desc, (int x, int y) coord)>> Calls = new();
        public override void AtLegendUpdate(List<(string sym, string desc, (int x, int y) coord)> entries, bool show, string area)
            => Calls.Add(entries);
    }

    private sealed class MutatingLegendListener : GameObject
    {
        public override void AtLegendUpdate(List<(string sym, string desc, (int x, int y) coord)> entries, bool show, string area)
            => entries.Add(("MUT", "mut", (9, 9)));
    }

    private sealed class ObservingLegendListener : GameObject
    {
        public List<(string sym, string desc, (int x, int y) coord)>? Seen;
        public override void AtLegendUpdate(List<(string sym, string desc, (int x, int y) coord)> entries, bool show, string area)
            => Seen = entries;
    }

    private static GameObject MakePlaced(string name, int id, string area)
    {
        var obj = GameObject.Create(name, isPc: true);
        obj.Id = id;
        obj.Symbol = "@";
        obj.Location = new LocationRef.CoordLocation(new Coord(area, 0, 0, 0));
        ObjectRegistry.AddObject(obj);
        return obj;
    }

    [Fact]
    public void RenderLegend_AbsentListeners_ShareEntriesInstance()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            ObjectRegistry.ClearAll();
            var mi = new MapInfo { Settings = new Atheriz.Core.Settings.AtherizSettings() };
            var placed = MakePlaced("placed", 1, "legendA");
            mi.Objects[placed.Id] = placed;
            mi.LegendEntries.Add(new LegendEntry("@", "tree", (1, 2)));
            mi.LegendEntries.Add(new LegendEntry("?", "hidden", null));
            var first = new RecLegendListener { Id = 101 };
            var second = new RecLegendListener { Id = 102 };
            mi.AddListener(first);
            mi.AddListener(second);

            mi.RenderLegend();

            Assert.Single(first.Calls);
            Assert.Single(second.Calls);
            Assert.Equal(first.Calls[0], second.Calls[0]);
            Assert.Same(first.Calls[0], second.Calls[0]);
            Assert.Contains(first.Calls[0], e => e.coord == (1, 2) && e.desc == "tree");
            Assert.DoesNotContain(first.Calls[0], e => e.desc == "hidden");
        }
        finally
        {
            ObjectRegistry.ClearAll();
        }
    }

    [Fact]
    public void RenderLegend_PresentListener_GetsPrivateFilteredCopy()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            ObjectRegistry.ClearAll();
            var mi = new MapInfo { Settings = new Atheriz.Core.Settings.AtherizSettings() };
            var placed = MakePlaced("placedself", 7, "legendB");
            mi.Objects[placed.Id] = placed;
            var self = new RecLegendListener { Id = 7 };
            var other = new RecLegendListener { Id = 103 };
            mi.AddListener(self);
            mi.AddListener(other);

            mi.RenderLegend();

            Assert.Single(self.Calls);
            Assert.Single(other.Calls);
            Assert.Empty(self.Calls[0]);
            Assert.NotEmpty(other.Calls[0]);
            Assert.NotSame(self.Calls[0], other.Calls[0]);
        }
        finally
        {
            ObjectRegistry.ClearAll();
        }
    }

    [Fact]
    public void RenderLegend_HookMutation_LeaksToLaterListeners()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            ObjectRegistry.ClearAll();
            var mi = new MapInfo { Settings = new Atheriz.Core.Settings.AtherizSettings() };
            var placed = MakePlaced("placed", 11, "legendC");
            mi.Objects[placed.Id] = placed;
            var mutator = new MutatingLegendListener { Id = 201 };
            var observer = new ObservingLegendListener { Id = 202 };
            mi.AddListener(mutator);
            mi.AddListener(observer);

            mi.RenderLegend();

            Assert.NotNull(observer.Seen);
            Assert.Contains(observer.Seen!, e => e.sym == "MUT" && e.coord == (9, 9));
        }
        finally
        {
            ObjectRegistry.ClearAll();
        }
    }
}
