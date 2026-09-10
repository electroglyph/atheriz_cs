using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The Render fan-out shares one filtered entries list across listeners absent
// from it, while each listener keeps its own grid copy: one listener's
// AtPreMapRender transform must not leak into another listener's view, and a
// listener present in the entries still gets a private filtered copy.
[Collection("Ported")]
public class RenderSharedEntriesTests
{
    private sealed class RecListener : GameObject
    {
        public Func<Dictionary<(int, int), string>, Dictionary<(int, int), string>> PreRenderImpl = g => g;
        public List<(string mapStr, List<(string sym, string desc, (int x, int y) coord)> entries)> Calls = new();
        public override Dictionary<(int X, int Y), string> AtPreMapRender(Dictionary<(int X, int Y), string> g)
            => PreRenderImpl(g.ToDictionary(kv => (kv.Key.Item1, kv.Key.Item2), kv => kv.Value))
                .ToDictionary(kv => (kv.Key.Item1, kv.Key.Item2), kv => kv.Value);
        public override void AtMapUpdate(string s, List<(string sym, string desc, (int x, int y) coord)> e, int minX, int maxY, bool show, string name)
            => Calls.Add((s, e));
    }

    private static GameObject MakePlaced(string name, int id)
    {
        var obj = GameObject.Create(name, isPc: true);
        obj.Id = id;
        obj.Symbol = "@";
        obj.Location = new LocationRef.CoordLocation(new Coord("sharedarea", 0, 0, 0));
        ObjectRegistry.AddObject(obj);
        return obj;
    }

    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    [Fact]
    public void Render_AbsentListeners_ShareEntriesButNotGrids()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mi = new MapInfo();
            mi.PreGrid[(0, 0)] = "X";
            mi.PreRender();
            MakePlaced("placed", 1);
            mi.AddMapable(ObjectRegistry.Get(1)[0]);
            var first = new RecListener();
            first.Id = 101;
            first.MapEnabled = true;
            first.LastMapTime = 0;
            first.PreRenderImpl = g => { g[(9, 9)] = "Z"; return g; };
            var second = new RecListener();
            second.Id = 102;
            second.MapEnabled = true;
            second.LastMapTime = 0;
            mi.AddListener(first);
            mi.AddListener(second);
            mi.Render(force: true);
            Assert.Single(first.Calls);
            Assert.Single(second.Calls);
            // Same filtered entries content, shared instance across absentees.
            Assert.Equal(first.Calls[0].entries, second.Calls[0].entries);
            Assert.NotEmpty(first.Calls[0].entries);
            Assert.Same(first.Calls[0].entries, second.Calls[0].entries);
            // The first listener's grid transform stays private to it.
            Assert.Contains("Z", first.Calls[0].mapStr);
            Assert.DoesNotContain("Z", second.Calls[0].mapStr);
        }
        finally { Reset(); }
    }

    [Fact]
    public void Render_PresentListener_GetsPrivateFilteredCopy()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mi = new MapInfo();
            mi.PreGrid[(0, 0)] = "X";
            mi.PreRender();
            MakePlaced("placedself", 7);
            mi.AddMapable(ObjectRegistry.Get(7)[0]);
            // Same id as the placed object: its own entry is filtered out.
            var self = new RecListener();
            self.Id = 7;
            self.MapEnabled = true;
            self.LastMapTime = 0;
            var other = new RecListener();
            other.Id = 103;
            other.MapEnabled = true;
            other.LastMapTime = 0;
            mi.AddListener(self);
            mi.AddListener(other);
            mi.Render(force: true);
            Assert.Single(self.Calls);
            Assert.Single(other.Calls);
            Assert.Empty(self.Calls[0].entries);
            Assert.NotEmpty(other.Calls[0].entries);
            Assert.NotSame(other.Calls[0].entries, self.Calls[0].entries);
        }
        finally { Reset(); }
    }
}
