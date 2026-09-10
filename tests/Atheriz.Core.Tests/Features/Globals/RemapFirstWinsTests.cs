using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The shared first-wins helper preserves both remap policies: a relocated
// entry landing on an occupied destination is dropped loudly (warning kept),
// and disjoint names merge.
[Collection("Ported")]
public class RemapFirstWinsTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    [Fact]
    public void RemapTransitions_CollisionKeepsPreexistingAndLogs()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var f = new Coord("remaptr", 0, 0, 0);
            var b = new Coord("remaptr", 1, 1, 0);
            var e = new Coord("remaptr", 2, 2, 0);
            var mover = new Transition(f, b, "mover");
            var keeper = new Transition(f, e, "keeper");
            nh.AddTransition(mover);
            nh.AddTransition(keeper);
            using var cap = new CaptureAtherizLog();
            nh.RemapTransitions(new Dictionary<Coord, Coord> { [b] = e });
            // mover (f->b) relocates onto keeper's (f->e): first-wins drops it loudly.
            var res = nh.FindTransitions(fromArea: "remaptr");
            Assert.Single(res);
            Assert.Same(keeper, res[0]);
            Assert.Contains("RemapTransitions", cap.Read());
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    [Fact]
    public void RemapDoors_DisjointNamesMerge()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var a = new Coord("remapmerge", 0, 0, 0);
            var d = new Coord("remapmerge", 5, 5, 0);
            var doorA = new Door(a, new Coord("remapmerge", 9, 9, 0), "east", "west", closed: false, locked: false);
            var doorD = new Door(d, new Coord("remapmerge", 8, 8, 0), "north", "south", closed: false, locked: false);
            nh.AddDoor(doorA);
            nh.AddDoor(doorD);
            nh.RemapDoors(new Dictionary<Coord, Coord> { [a] = d });
            // No name overlap: the pre-existing door stays and the newcomer merges in.
            var kept = nh.GetDoors(d);
            Assert.NotNull(kept);
            Assert.Same(doorD, kept!["north"]);
            Assert.Same(doorA, kept!["east"]);
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }
}
