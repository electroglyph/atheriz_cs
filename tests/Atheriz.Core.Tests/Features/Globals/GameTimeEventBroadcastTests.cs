using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Globals;

// The shared lunar/solar broadcast helper preserves delivery: the receiver
// fallback, the filter predicate, and both messages behave as before.
[Collection("Ported")]
public class GameTimeEventBroadcastTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
    }

    [Fact]
    public void OnTick_SunsetChange_DeliversSolarMessage()
    {
        // Last tick of hour 17 (sun up) rolling into hour 18 (sun down).
        Reset();
        try
        {
            var settings = new AtherizSettings();
            var gt = new GameTime(settings, autoLoad: false);
            var watcher = GameObject.Create("sunwatcher", isPc: true);
            ObjectRegistry.AddObject(watcher);
            watcher.IsConnected = true;
            watcher.ClearMessages();
            var mob = GameObject.Create("sunmob");
            ObjectRegistry.AddObject(mob);
            mob.ClearMessages();
            gt.Ticks = 18 * 60 - 1;
            gt.OnTick();
            Assert.Contains(watcher.PeekMessages(), m => m.Contains(settings.SunsetMessage));
            Assert.Empty(mob.PeekMessages());
        }
        finally { Reset(); }
    }

    [Fact]
    public void OnTick_PhaseChange_DeliversLunarMessage()
    {
        // Last tick of day 6 (waxing crescent) rolling into day 7 (first quarter).
        Reset();
        try
        {
            var settings = new AtherizSettings();
            var gt = new GameTime(settings, autoLoad: false);
            var watcher = GameObject.Create("moonwatcher", isPc: true);
            ObjectRegistry.AddObject(watcher);
            watcher.IsConnected = true;
            watcher.ClearMessages();
            gt.Ticks = 7 * 1440 - 1;
            gt.OnTick();
            Assert.Contains(watcher.PeekMessages(), m => m.Contains("first quarter"));
        }
        finally { Reset(); }
    }

    [Fact]
    public void OnTick_ThrowingReceiverLambda_FallsBackToPcConnected()
    {
        // A throwing settings picker degrades to the IsPc && IsConnected
        // fallback instead of dropping the broadcast.
        Reset();
        try
        {
            var settings = new AtherizSettings
            {
                LunarReceiverLambda = _ => throw new InvalidOperationException("boom"),
            };
            var gt = new GameTime(settings, autoLoad: false);
            var watcher = GameObject.Create("fallbackwatcher", isPc: true);
            ObjectRegistry.AddObject(watcher);
            watcher.IsConnected = true;
            watcher.ClearMessages();
            gt.Ticks = 7 * 1440 - 1;
            gt.OnTick();
            Assert.Contains(watcher.PeekMessages(), m => m.Contains("first quarter"));
        }
        finally { Reset(); }
    }
}
