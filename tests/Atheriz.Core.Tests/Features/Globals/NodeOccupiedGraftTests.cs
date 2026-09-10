using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The collapsed load-graft branch preserves the occupied-cell arm: a
// live-modified node whose cell still exists in the fresh row is grafted
// (newer in-memory edits win), not clobbered by the stale row.
[Collection("Ported")]
public class NodeOccupiedGraftTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    [Fact]
    public void Load_OccupiedCellGraft_KeepsLiveModifiedNode()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var area = new NodeArea("graftoccupied");
            area.AddGrid(new NodeGrid("graftoccupied", 0));
            nh.AddArea(area);
            var node = new Node(new Coord("graftoccupied", 0, 0, 0));
            nh.AddNode(node);
            nh.Save(force: true);
            // Live-only edit after the save: the next Load's fresh row still
            // carries the old desc at the same occupied cell. (Node.Name is
            // coord-derived with a no-op setter, so the edit goes through
            // Desc, which marks IsModified.)
            node.Desc = "live-edited";
            Assert.True(node.IsModified);
            nh.Load();
            var got = nh.GetNode(new Coord("graftoccupied", 0, 0, 0));
            Assert.NotNull(got);
            Assert.Same(node, got);
            Assert.Equal("live-edited", got!.Desc);
            Assert.NotEmpty(ObjectRegistry.Get(node.Id));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }
}
