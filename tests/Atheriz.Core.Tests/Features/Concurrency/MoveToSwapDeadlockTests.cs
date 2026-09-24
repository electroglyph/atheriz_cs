using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Concurrency;

// The mover's own lock joins the ordered lock set, so opposite-direction
// moves can never deadlock ABBA.
[Collection("Ported")]
public sealed class MoveToSwapDeadlockTests
{
    [Fact]
    public async Task MoveTo_ConcurrentSwap_CompletesWithoutDeadlock()
    {
        // Two movers swapping rooms take mover/source/destination locks in
        // one global order, so the swap always completes instead of
        // deadlocking.
        using var env = GlobalTestEnv.Enter();
        var cA = new Coord("swaparea", 0, 0, 0);
        var cB = new Coord("swaparea", 1, 0, 0);
        var nodeA = new Node(cA);
        var nodeB = new Node(cB);
        try { ObjectRegistry.AddObject(nodeA); } catch { }
        try { ObjectRegistry.AddObject(nodeB); } catch { }
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            nh.AddNode(nodeA);
            nh.AddNode(nodeB);
            var objA = PortedHelpers.MakeCaller("swapa");
            var objB = PortedHelpers.MakeCaller("swapb");
            Assert.True(objA.MoveTo(nodeA, force: true));
            Assert.True(objB.MoveTo(nodeB, force: true));
            // Start barrier: both moves must genuinely overlap. A pooled wait
            // may inline the delegates sequentially, which would complete
            // without ever opening the ABBA window this test exists to close.
            using var start = new Barrier(2);
            var t1 = Task.Run(() => { start.SignalAndWait(); return objA.MoveTo(nodeB, force: true); });
            var t2 = Task.Run(() => { start.SignalAndWait(); return objB.MoveTo(nodeA, force: true); });
            await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromMilliseconds(15000));
            Assert.True(await t1);
            Assert.True(await t2);
            Assert.Equal(cB, Assert.IsType<LocationRef.CoordLocation>(objA.Location).Coord);
            Assert.Equal(cA, Assert.IsType<LocationRef.CoordLocation>(objB.Location).Coord);
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
