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

    [Fact]
    public void ConcurrentTickSecondsSwap_LeavesSingleRegistration()
    {
        // The TickSeconds swap spanned separate locks, so racing swaps each
        // removed their own stale-read old interval and added their own —
        // stale intervals kept firing alongside the fresh one. Predicate and
        // mutation share one write hold now.
        using var env = GlobalTestEnv.Enter();
        WireTicker();
        var ticker = GlobalServices.GetAsyncTicker();
        var node = new Node(new Coord("test_tickswap", 0, 0, 0));
        node.IsTickable = true;
        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            node.TickSeconds = (i % 2 == 0) ? 2.0 : 3.0;
        })).ToArray();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)));
        double v = node.TickSeconds;
        Assert.True(v == 2.0 || v == 3.0);
        var slot = ticker.GetSlot(v);
        Assert.NotNull(slot);
        Assert.Contains(slot!.CorosSnapshot, d => d.Method.Name == "AtTick" && Equals(d.Target, node));
        foreach (double stale in new[] { 1.0, 2.0, 3.0 }.Where(x => x != v))
        {
            var s = ticker.GetSlot(stale);
            Assert.True(s == null || !s.CorosSnapshot.Any(d => d.Method.Name == "AtTick" && Equals(d.Target, node)),
                $"stale interval {stale} must not retain the ticker registration");
        }
    }
}
