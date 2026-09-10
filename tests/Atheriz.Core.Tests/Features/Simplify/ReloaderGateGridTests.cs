using System.Diagnostics;
using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Dedicated reload gate + shared grid walk: a reload in progress makes a second
// reload skip (never block), and tick re-registration discovers grid nodes
// through the unified snapshot walk.
[Collection("Ported")]
public class ReloaderGateGridTests
{
    private static MethodInfo GateMethod(string name) =>
        typeof(PluginReloader).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public async Task ReloadAsync_GateHeldOnOtherFlow_SkipsWithoutBlocking()
    {
        using var env = GlobalTestEnv.Enter();
        var tryEnter = GateMethod("TryEnterGate");
        var exitGate = GateMethod("ExitGate");
        Assert.NotNull(tryEnter);
        Assert.NotNull(exitGate);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        bool holderTookGate = false;
        var holder = new Thread(() =>
        {
            try
            {
                holderTookGate = (bool)tryEnter.Invoke(null, null)!;
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                exitGate.Invoke(null, null);
            }
            catch { entered.Set(); }
        });
        // Suppress the ExecutionContext flow so the holder thread starts with a
        // fresh gate view instead of inheriting this flow's recursion count.
        var flow = ExecutionContext.SuppressFlow();
        try { holder.Start(); }
        finally { flow.Undo(); }
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(holderTookGate);
        try
        {
            using var cap = new CaptureAtherizLog();
            var ticker = GlobalServices.GetAsyncTicker();
            var pool = GlobalServices.GetAsyncThreadPool();
            var sw = Stopwatch.StartNew();
            Assert.False(await PluginReloader.ReloadAsync("/tmp/atheriz_gate_probe_xyz.dll", ticker, pool));
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Contains("already in progress", cap.Read());
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void ReregisterTicks_DiscoversGridOnlyTickableNode()
    {
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            var area = new NodeArea("gategrid");
            var grid = new NodeGrid("gategrid", 0);
            var node = new Node(new Coord("gategrid", 0, 0, 0)) { IsTickable = true, TickSeconds = 60 };
            // Grid-only membership: the unified walk must find the node even
            // though it is not published to the object registry.
            try { ObjectRegistry.RemoveObject(node); } catch { }
            grid.AddNode(node);
            area.AddGrid(grid);
            nh.AddArea(area);
            GlobalServices.SetNodeHandler(nh);
            Assert.Empty(ObjectRegistry.FilterBy(o => o.IsTickable));
            PluginReloader.ReregisterTicks(ticker);
            Assert.Equal(1, ticker.Slots.Values.Sum(s => s.Coros.Count));
        }
        finally { ticker.Clear(); pool.Stop(); }
    }
}
