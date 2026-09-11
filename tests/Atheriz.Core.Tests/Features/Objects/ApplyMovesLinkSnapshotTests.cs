// Pins for the ApplyMoves cross-area link snapshot (NodeGrid.cs): a
// concurrent link add mid-ApplyMoves must not break enumeration, and the
// grid stays consistent.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class ApplyMovesLinkSnapshotTests
{
    private static Node MakeNode(string area, int x, int y)
    {
        var node = new Node(new Coord(area, x, y, 0));
        ObjectRegistry.AddObject(node);
        return node;
    }

    [Fact]
    public void ApplyMoves_RelocatesNode_AndRewritesLinks()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("snapmove", 0);
        var a = MakeNode("snapmove", 0, 0);
        var b = MakeNode("snapmove", 1, 0);
        grid.AddNode(a);
        grid.AddNode(b);
        a.AddLink(new NodeLink("east", new Coord("snapmove", 1, 0, 0)));

        var failed = grid.ApplyMoves([((0, 0), (2, 0))]);

        Assert.Empty(failed);
        Assert.Equal(new Coord("snapmove", 2, 0, 0), a.Coord);
        Assert.Contains(a.GetLinks(), l => l.Coord.Equals(new Coord("snapmove", 1, 0, 0)));
    }

    [Fact]
    public void ApplyMoves_ConcurrentLinkAdd_CompletesWithoutEnumerationError()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("snaprace", 0);
        var a = MakeNode("snaprace", 0, 0);
        var b = MakeNode("snaprace", 1, 0);
        grid.AddNode(a);
        grid.AddNode(b);
        a.AddLink(new NodeLink("east", new Coord("snaprace", 1, 0, 0)));
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        using var start = new ManualResetEventSlim(false);

        var mover = Task.Run(() =>
        {
            start.Wait(TimeSpan.FromSeconds(30));
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    if (i % 2 == 0) grid.ApplyMoves([((0, 0), (5, 5))]);
                    else grid.ApplyMoves([((5, 5), (0, 0))]);
                }
            }
            catch (Exception ex) { errors.Add(ex); }
        });
        var linker = Task.Run(() =>
        {
            start.Wait(TimeSpan.FromSeconds(30));
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    try { a.AddLink(new NodeLink($"churn-{i}", new Coord("snaprace", 9, 9, 0))); } catch { }
                    try { a.RemoveLink($"churn-{i}"); } catch { }
                }
            }
            catch (Exception ex) { errors.Add(ex); }
        });
        start.Set();

        Assert.True(Task.WaitAll([mover, linker], TimeSpan.FromSeconds(60)));
        Assert.Empty(errors);
        Assert.True(grid.Nodes.ContainsKey((0, 0)) || grid.Nodes.ContainsKey((5, 5)));
    }
}
