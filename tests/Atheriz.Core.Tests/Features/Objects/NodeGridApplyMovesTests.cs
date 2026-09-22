using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Objects;

// Grid moves carry member CoordLocations along with the node, so contents
// are never left pointing at the old square.
[Collection("Ported")]
public sealed class NodeGridApplyMovesTests
{
    [Fact]
    public void ApplyMoves_RemapsMembersToNewCoord()
    {
        // A moved node's members must resolve at the new coord — otherwise
        // lookups by the new coord ghost them (empty look, stale exits).
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("applyarea", 0);
        var nodeA = new Node(new Coord("applyarea", 0, 0, 0));
        try { ObjectRegistry.AddObject(nodeA); } catch { }
        grid.AddNode(nodeA);
        var member = PortedHelpers.MakeCaller("applym");
        Assert.True(member.MoveTo(nodeA, force: true));
        var failed = grid.ApplyMoves([((0, 0), (1, 1))]);
        Assert.Empty(failed);
        Assert.Equal(new Coord("applyarea", 1, 1, 0),
            Assert.IsType<LocationRef.CoordLocation>(member.Location).Coord);
    }
}
