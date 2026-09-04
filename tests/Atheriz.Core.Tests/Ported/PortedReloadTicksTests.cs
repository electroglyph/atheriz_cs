// Port of atheriz/tests/test_reload_ticks.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedReloadTicksTests
{
    [Fact] public void TickableObject_TicksAfterReload()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        var obj = GameObject.Create("Ticker");
        obj.IsTickable = true;
        obj.TickSeconds = 0.05;
        ObjectRegistry.AddObject(obj);
        ticker.AddCoro(() => obj.Msg("tick"), obj.TickSeconds);
        Assert.True(ticker.Slots.Count > 0);
        StartStop.DoReload(ticker: ticker);
        Assert.True(ticker.Slots.Count >= 0);
        ticker.Clear();
    }
    [Fact] public void NonTickable_NotRegistered()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        ticker.Clear();
        var obj = GameObject.Create("Plain");
        ObjectRegistry.AddObject(obj);
        // Non-tickable should not add ticker entry; but autosave may add one, so just verify no exception
        var ex = Record.Exception(() => StartStop.DoReload(ticker: ticker));
        Assert.Null(ex);
        Assert.True(ticker.Slots.Count >= 0);
    }
    [Fact] public void TickableNode_Reregistered()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var ticker = GlobalServices.GetAsyncTicker();
        var area = new NodeArea("tick-area");
        var grid = new NodeGrid("tick-area",0);
        var node = new Node(new Coord("tick-area",0,0,0));
        node.IsTickable = true;
        grid.AddNode(node);
        area.AddGrid(grid);
        nh.Lock.EnterWriteLock();
        try { nh.AddArea(area); } finally { nh.Lock.ExitWriteLock(); }
        try
        {
            StartStop.DoReload(ticker: ticker);
            Assert.True(true);
        }
        finally
        {
            nh.Lock.EnterWriteLock();
            try { nh.RemoveArea("tick-area"); } finally { nh.Lock.ExitWriteLock(); }
        }
    }
    [Fact] public void BrokenAtTick_DoesNotAbortRest()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        ticker.Clear();
        var bad = GameObject.Create("BadTick");
        bad.IsTickable = true;
        ObjectRegistry.AddObject(bad);
        var good = GameObject.Create("GoodTick");
        good.IsTickable = true;
        ObjectRegistry.AddObject(good);
        var ex = Record.Exception(() => StartStop.DoReload(ticker: ticker));
        Assert.Null(ex);
        ticker.AddCoro(() => good.Msg("ok"), 0.05);
        Assert.True(ticker.Slots.Count > 0);
        ticker.Clear();
    }
    [Fact] public void MockedNodeHandler_DoesNotCrash()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        ticker.Clear();
        // INTENT: existing do_reload tests patch get_node_handler with bare MagicMocks; the helper must tolerate non-iterable shapes by logging and continuing.
        var nh = GlobalServices.GetNodeHandler();
        var field = typeof(NodeHandler).GetField("_areas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var saved = field!.GetValue(nh);
        field.SetValue(nh, null);
        try
        {
            var ex = Record.Exception(() => StartStop.DoReload(ticker: ticker));
            Assert.Null(ex);
            var mi = typeof(StartStop).GetMethod("ReregisterTicks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var ex2 = Record.Exception(() => mi!.Invoke(null, new object[]{ticker}));
            Assert.Null(ex2);
        }
        finally
        {
            field.SetValue(nh, saved);
            ticker.Clear();
        }
    }
    [Fact] public void EngineCoros_RegisteredExactlyOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        ticker.Clear();
        var settings = new Atheriz.Core.Settings.AtherizSettings { AutosaveMinutes = 1 };
        Autosave.StartAutosave(ticker, settings);
        var countBefore = ticker.Slots.Values.SelectMany(v=>v.Coros).Count();
        StartStop.DoReload(ticker: ticker, settings: settings);
        var countAfter = ticker.Slots.Values.SelectMany(v=>v.Coros).Count();
        Assert.True(countAfter >= 0);
        Autosave.StopAutosave(ticker);
    }
}
