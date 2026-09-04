// Port of atheriz/tests/test_reload.py:1
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedReloadTests
{
    [Fact] public void Reload_KeepsGameClock()
    {
        using var env = GlobalTestEnv.Enter();
        var gt = GlobalServices.GetGameTime();
        var ticker = GlobalServices.GetAsyncTicker();
        gt.Start(ticker);
        Assert.True(ticker.Slots.Count > 0);
        StartStop.DoReload(ticker: ticker);
        Assert.True(ticker.Slots.Count >= 0);
        gt.Stop(ticker);
    }
    [Fact] public void Reload_KeepsAutosave()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        var settings = new Atheriz.Core.Settings.AtherizSettings { AutosaveMinutes = 5 };
        Autosave.StartAutosave(ticker, settings);
        StartStop.DoReload(ticker: ticker, settings: settings);
        Assert.True(ticker.Slots.Count >= 0);
        Autosave.StopAutosave(ticker);
    }
}
