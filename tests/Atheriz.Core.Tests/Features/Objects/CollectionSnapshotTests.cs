// Pins for the snapshot-collection work: live collection properties on Node,
// NodeGrid, NodeArea and MapInfo return copies, so a reader holding a copy
// can never see a concurrent mutation (or corrupt the inside by writing to
// the copy). Setters copy their input for the same reason.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class CollectionSnapshotTests
{
    private static Node MakeNode(string area = "snap", int x = 0, int y = 0)
        => new(new Coord(area, x, y, 0));

    [Fact]
    public void Links_Getter_ReturnsCopyNotLive()
    {
        using var env = GlobalTestEnv.Enter();
        var node = MakeNode();
        node.AddLink(new NodeLink("north", new Coord("snap", 0, 1, 0)));

        var snap = node.Links;
        snap.Clear();
        snap.Add(new NodeLink("bogus", new Coord("snap", 9, 9, 0)));

        Assert.Single(node.Links);
        Assert.Equal("north", node.GetLinks()[0].Name);
    }

    [Fact]
    public void Links_Setter_CopiesInput()
    {
        using var env = GlobalTestEnv.Enter();
        var node = MakeNode();
        var input = new List<NodeLink> { new("north", new Coord("snap", 0, 1, 0)) };
        node.Links = input;
        input.Clear();

        Assert.Single(node.Links);
    }

    [Fact]
    public void Nouns_Getter_ReturnsCopyNotLive()
    {
        using var env = GlobalTestEnv.Enter();
        var node = MakeNode();
        node.AddNoun("rock", "a mossy rock");

        var snap = node.Nouns;
        snap.Remove("rock");
        snap["bogus"] = "nope";

        Assert.True(node.Nouns.ContainsKey("rock"));
        Assert.Single(node.Nouns);
    }

    [Fact]
    public void Nouns_Setter_CopiesInput()
    {
        using var env = GlobalTestEnv.Enter();
        var node = MakeNode();
        var input = new Dictionary<string, string> { ["rock"] = "a mossy rock" };
        node.Nouns = input;
        input.Clear();

        Assert.Equal("a mossy rock", node.Nouns["rock"]);
    }

    [Fact]
    public void Links_ConcurrentEnumerateAndMutate_DoesNotThrow()
    {
        using var env = GlobalTestEnv.Enter();
        var node = MakeNode();
        node.AddLink(new NodeLink("seed", new Coord("snap", 0, 1, 0)));

        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 8, i =>
        {
            try
            {
                if (i % 2 == 0)
                {
                    for (var k = 0; k < 50; k++)
                        foreach (var _ in node.Links) { }
                }
                else
                {
                    for (var k = 0; k < 50; k++)
                        node.AddLink(new NodeLink($"d{i}_{k}", new Coord("snap", k, k, 0)));
                }
            }
            catch (Exception ex) { errors.Add(ex); }
        });

        Assert.Empty(errors);
        Assert.Equal(1 + 4 * 50, node.Links.Count);
    }

    [Fact]
    public void NodeLink_Aliases_Getter_ReturnsCopy()
    {
        var link = new NodeLink("north", new Coord("snap", 0, 1, 0), ["n"]);
        link.Aliases.Add("bogus");
        Assert.Single(link.Aliases);
    }

    [Fact]
    public void NodeLink_Aliases_Setter_CopiesInput()
    {
        var link = new NodeLink("north", new Coord("snap", 0, 1, 0));
        var input = new List<string> { "n" };
        link.Aliases = input;
        input.Clear();
        Assert.Single(link.Aliases);
    }

    [Fact]
    public void ExitCommand_Aliases_ReturnsCopyAndSetterCopies()
    {
        var cmd = new ExitCommand();
        var input = new List<string> { "n" };
        cmd.SetAliases(input);
        input.Clear();
        Assert.Single(cmd.Aliases);
        var snap = cmd.Aliases.ToList();
        snap.Add("bogus");
        Assert.Single(cmd.Aliases);
    }

    [Fact]
    public void Nodes_Getter_ReturnsCopyNotLive()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("snap", 0);
        grid.AddNode(MakeNode(x: 1, y: 2));

        var snap = grid.Nodes;
        snap.Clear();

        Assert.NotNull(grid.GetNode(1, 2));
        Assert.Equal(1, grid.Count);
    }

    [Fact]
    public void AddNodeRaw_InsertsRetrievableNode()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("snap", 0);
        grid.AddNodeRaw(MakeNode(x: 3, y: 4));
        Assert.NotNull(grid.GetNode(3, 4));
    }

    [Fact]
    public void Grids_Getter_ReturnsCopyNotLive()
    {
        using var env = GlobalTestEnv.Enter();
        var area = new NodeArea("snap");
        area.AddGrid(new NodeGrid("snap", 0));

        var snap = area.Grids;
        snap.Clear();

        Assert.NotNull(area.GetGrid(0));
    }

    [Fact]
    public void AddGridRaw_SetsAreaAndInserts()
    {
        using var env = GlobalTestEnv.Enter();
        var area = new NodeArea("snap");
        var grid = new NodeGrid("elsewhere", 7);
        area.AddGridRaw(grid);
        Assert.Same(grid, area.GetGrid(7));
        Assert.Equal("snap", grid.Area);
    }

    [Fact]
    public void PreGrid_Getter_ReturnsCopyNotLive()
    {
        var mi = new MapInfo("snap");
        mi.SetPreCell((0, 0), "#");

        var snap = mi.PreGrid;
        snap[(0, 0)] = "MUT";
        snap[(9, 9)] = "MUT";

        Assert.Equal("#", mi.PreGrid[(0, 0)]);
        Assert.False(mi.PreGrid.ContainsKey((9, 9)));
    }

    [Fact]
    public void SetPreCell_RemovePreCell_RoundTrip()
    {
        var mi = new MapInfo("snap");
        mi.SetPreCell((1, 1), "X");
        Assert.Equal("X", mi.PreGrid[(1, 1)]);
        mi.RemovePreCell((1, 1));
        Assert.False(mi.PreGrid.ContainsKey((1, 1)));
    }

    [Fact]
    public void PaintSymbol_PopulatedPreGrid_WritesBothAndDirties()
    {
        var mi = new MapInfo("snap");
        mi.SetPreCell((0, 0), "#");
        mi.MapChanged = false;
        mi.PaintSymbol((1, 1), "D");
        Assert.Equal("D", mi.PostGrid[(1, 1)]);
        Assert.Equal("D", mi.PreGrid[(1, 1)]);
        Assert.True(mi.MapChanged);
    }

    [Fact]
    public void PaintSymbol_EmptyPreGrid_WritesPostOnly()
    {
        var mi = new MapInfo("snap");
        mi.MapChanged = false;
        mi.PaintSymbol((1, 1), "D");
        Assert.Equal("D", mi.PostGrid[(1, 1)]);
        Assert.False(mi.PreGrid.ContainsKey((1, 1)));
        Assert.False(mi.MapChanged);
    }

    [Fact]
    public void ReplaceLegendEntries_SwapsList()
    {
        var mi = new MapInfo("snap");
        mi.AddLegendEntry(new LegendEntry("@", "me", (0, 0)));
        var replacement = new List<LegendEntry> { new("#", "wall", (1, 1)) };
        mi.MapChanged = false;
        mi.ReplaceLegendEntries(replacement);
        var snap = mi.LegendEntries;
        Assert.Single(snap);
        Assert.Equal("#", snap[0].Symbol);
        Assert.True(mi.MapChanged);
    }

    [Fact]
    public void ReplaceEntry_SwapsStaleInstances()
    {
        using var env = GlobalTestEnv.Enter();
        var mi = new MapInfo("snap");
        var stale = GameObject.Create("stale", isPc: true);
        stale.Id = 41;
        ObjectRegistry.AddObject(stale);
        mi.AddListener(stale, notify: false);
        mi.AddMapable(stale, notify: false);

        var fresh = GameObject.Create("fresh", isPc: true);
        fresh.Id = 41;
        ObjectRegistry.AddObject(fresh);
        mi.ReplaceEntry(41, fresh);

        Assert.Same(fresh, mi.Listeners[41]);
        Assert.Same(fresh, mi.Objects[41]);
    }

    [Fact]
    public void AddListenerAndMapable_LandsBoth()
    {
        using var env = GlobalTestEnv.Enter();
        var mi = new MapInfo("snap");
        var obj = GameObject.Create("both", isPc: true);
        obj.Id = 42;
        ObjectRegistry.AddObject(obj);

        mi.AddListenerAndMapable(obj);

        Assert.Same(obj, mi.Listeners[42]);
        Assert.Same(obj, mi.Objects[42]);
    }

    [Fact]
    public void PreGrid_ConcurrentEnumerateAndMutate_DoesNotThrow()
    {
        var mi = new MapInfo("snap");
        mi.SetPreCell((0, 0), "#");
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 8, i =>
        {
            try
            {
                if (i % 2 == 0)
                {
                    for (var k = 0; k < 50; k++)
                        foreach (var _ in mi.PreGrid) { }
                }
                else
                {
                    for (var k = 0; k < 50; k++)
                        mi.SetPreCell((i, k), "#");
                }
            }
            catch (Exception ex) { errors.Add(ex); }
        });
        Assert.Empty(errors);
        Assert.Equal(1 + 4 * 50, mi.PreGrid.Count);
    }
}
