// Port of atheriz/tests/test_delete_tickable.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDeleteTickableTests
{
    private static HashSet<Delegate> TickerCoros(GameObject obj)
    {
        var ticker = GlobalServices.GetAsyncTicker();
        var slot = ticker.GetSlot(obj.TickSeconds);
        return slot?.CorosSnapshot ?? new HashSet<Delegate>();
    }

    private static void WireTicker()
    {
        var ticker = GlobalServices.GetAsyncTicker();
        // Node uses GlobalTickerHolder, not GlobalServices — wire for test
        var holder = typeof(Node).Assembly.GetType("Atheriz.Core.Objects.GlobalTickerHolder");
        var set = holder?.GetMethod("Set", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        set?.Invoke(null, new object[] { ticker });
    }

    [Fact]
    public void DeleteRemovesTickableFromTicker()
    {
        using var env = GlobalTestEnv.Enter();
        WireTicker();
        var ticker = GlobalServices.GetAsyncTicker();
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        var node = new Node(new Coord("test_tick", 0, 0, 0), desc: "Ticker");
        node.IsTickable = true;
        var slot = ticker.GetSlot(node.TickSeconds);
        Assert.NotNull(slot);
        bool contains = slot!.CorosSnapshot.Any(d => d.Method.Name == "AtTick" && Equals(d.Target, node));
        Assert.True(contains, "tickable should be registered");
        var result = node.Delete(caller);
        Assert.NotNull(result);
        var afterSlot = ticker.GetSlot(node.TickSeconds);
        bool still = afterSlot?.CorosSnapshot.Any(d => Equals(d.Target, node)) ?? false;
        Assert.False(still, "deleted tickable must be unregistered");
    }

    [Fact]
    public void DeleteKeepsOtherTickablesRegistered()
    {
        using var env = GlobalTestEnv.Enter();
        WireTicker();
        var ticker = GlobalServices.GetAsyncTicker();
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        var target = new Node(new Coord("test_tick", 0, 0, 0));
        target.IsTickable = true;
        var survivor = new Node(new Coord("test_tick", 1, 0, 0));
        survivor.IsTickable = true;
        var slot = ticker.GetSlot(target.TickSeconds);
        Assert.NotNull(slot);
        target.Delete(caller);
        Assert.True(slot!.CorosSnapshot.Any(d => Equals(d.Target, survivor)), "survivor must stay registered");
        Assert.False(slot.CorosSnapshot.Any(d => Equals(d.Target, target)), "target must be removed");
    }
}
