using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
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
}
