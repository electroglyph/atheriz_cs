using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The shared tick-registration helper keeps both reload sweeps working: a
// reload re-registers registry tickables and node-grid tickables on the tick
// ticker (the node-grid loop runs in addition to the registry sweep, which
// already sees nodes as GameObjects).
[Collection("Ported")]
public class TickReregistrationTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    [Fact]
    public void DoReload_ReregistersObjectAndNodeTicks()
    {
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        var ticker = new AsyncTicker();
        try
        {
            nh = new NodeHandler(autoLoad: false);
            GlobalServices.SetNodeHandler(nh);
            var obj = GameObject.Create("tickobj", isTickable: true);
            ObjectRegistry.AddObject(obj);
            var node = new Node(new Coord("tickarea", 0, 0, 0));
            node.IsTickable = true;
            nh.AddNode(node);
            var settings = new AtherizSettings { TimeSystemEnabled = false, AutosaveMinutes = 0 };
            StartStop.DoReload(settings, ticker);
            int total = ticker.Slots.Values.Sum(s => s.Coros.Count);
            Assert.Equal(3, total);
        }
        finally
        {
            try { ticker.Stop(); } catch (Exception) { }
            NodeHandler.SetCurrent(null);
            StartStop.Reset();
            Reset();
        }
    }

    private sealed class TickProbe : GameObject { }

    private static int CountCoros(AsyncTicker ticker) => ticker.Slots.Values.Sum(s => s.Coros.Count);

    [Fact]
    public void ReregisterTicks_EvictsDelegateTargetingDeletedTickable()
    {
        // A tick delegate targeting a deleted object is a zombie — the sweep
        // evicts it even though its id matches no live tickable.
        using var env = GlobalTestEnv.Enter();
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        try
        {
            var o = new TickProbe { IsTickable = true, TickSeconds = 60 };
            ObjectRegistry.AddObject(o);
            PluginReloader.ReregisterTicks(ticker);
            Assert.Equal(1, CountCoros(ticker));
            ObjectRegistry.RemoveObject(o);
            PluginReloader.ReregisterTicks(ticker);
            Assert.Equal(0, CountCoros(ticker));
        }
        finally { ticker.Clear(); pool.Stop(); }
    }
}
