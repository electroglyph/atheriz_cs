using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Objects;

// The post-hook origin guard compares locations by id, so a hook that
// grafts a fresh instance (same id) is still the same location.
[Collection("Ported")]
public sealed class MoveToGraftedOriginTests
{
    private sealed class GraftNode : Node
    {
        public GraftNode(Coord c) : base(c) { }
        public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
        {
            // Simulate an instance graft mid-move: the node at our coord is
            // replaced by a fresh instance carrying the same id.
            ObjectRegistry.RemoveObject(this);
            var graft = new GraftNode(Coord);
            try { ObjectRegistry.RemoveObject(graft); } catch { }
            graft.SetIdRaw(Id);
            ObjectRegistry.AddObject(graft);
            return base.AtPreObjectLeave(destination, toExit);
        }
    }

    [Fact]
    public void MoveTo_GraftedNodeSameId_CompletesMove()
    {
        // An origin grafted mid-move (fresh instance, same id) must not
        // abort the move — the guard compares by id, not instance.
        using var env = GlobalTestEnv.Enter();
        NodeHandler.SetCurrent(null);
        var c1 = new Coord("graftarea", 0, 0, 0);
        var cD = new Coord("graftarea", 1, 0, 0);
        var n1 = new GraftNode(c1);
        try { ObjectRegistry.AddObject(n1); } catch { }
        var dest = new Node(cD);
        try { ObjectRegistry.AddObject(dest); } catch { }
        var obj = PortedHelpers.MakeCaller("graftm");
        Assert.True(obj.MoveTo(n1, force: true));
        Assert.True(obj.MoveTo(dest, force: true));
        Assert.Equal(cD, Assert.IsType<LocationRef.CoordLocation>(obj.Location).Coord);
    }
}
