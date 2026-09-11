using System.Text.Json;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The tick collects exact, hour-wildcard, minute-wildcard and full-wildcard
// alarms from one time snapshot: every matching alarm fires, misses stay queued.
[Collection("Ported")]
public class GameTimeWildcardAlarmTests
{
    private sealed class AlarmProbe : GameObject
    {
        public volatile bool Delivered;
        public override void AtAlarm(GameTime.GameTimeInfo time, Dictionary<string, JsonElement>? data)
        {
            Delivered = true;
        }
    }

    private static AlarmProbe RegisterProbe(string name)
    {
        var probe = new AlarmProbe();
        probe.Id = IdGenerator.GetUniqueId();
        probe.Name = name;
        ObjectRegistry.AddObject(probe);
        return probe;
    }

    private static (string Hour, string Minute) PostTickTime(AtherizSettings settings)
    {
        var twin = new GameTime(settings, autoLoad: false);
        twin.Ticks = 1;
        var at = twin.GetTime();
        return (at.Hour.ToString(), at.Minute.ToString());
    }

    [Fact]
    public void OnTick_WildcardArms_AllMatchingAlarmsDelivered()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000, reliefLimit: 0);
        var gt = new GameTime(settings, ticker: null, pool, autoLoad: false);
        gt.Ticks = 0;
        var (hour, minute) = PostTickTime(settings);

        var exact = RegisterProbe("wild-exact");
        var hourWild = RegisterProbe("wild-hour");
        var minuteWild = RegisterProbe("wild-minute");
        var fullWild = RegisterProbe("wild-full");
        var miss = RegisterProbe("wild-miss");
        gt.AddAlarm(hour, minute, exact.Id, repeat: true);
        gt.AddAlarm("?", minute, hourWild.Id, repeat: true);
        gt.AddAlarm(hour, "?", minuteWild.Id, repeat: true);
        gt.AddAlarm("?", "?", fullWild.Id, repeat: true);
        gt.AddAlarm("9999", "9999", miss.Id, repeat: true);

        gt.OnTick();

        for (int i = 0; i < 100 && !(exact.Delivered && hourWild.Delivered && minuteWild.Delivered && fullWild.Delivered); i++)
            Thread.Sleep(50);
        Assert.True(exact.Delivered, "exact alarm not delivered");
        Assert.True(hourWild.Delivered, "?-minute alarm not delivered");
        Assert.True(minuteWild.Delivered, "hour-? alarm not delivered");
        Assert.True(fullWild.Delivered, "?-? alarm not delivered");
        Assert.False(miss.Delivered, "non-matching alarm must not fire");
        Assert.Contains(
            gt.SnapshotAlarms().SelectMany(kv => kv.Value),
            a => a.CallerId == miss.Id);
    }

    [Fact]
    public void OnTick_HourWildcardNonRepeatAlarm_RemovedAfterTick()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var gt = new GameTime(settings, ticker: null, pool, autoLoad: false);
        gt.Ticks = 0;
        var (hour, _) = PostTickTime(settings);
        var caller = GameObject.Create("hourwild");
        ObjectRegistry.AddObject(caller);
        gt.AddAlarm(hour, "?", caller, repeat: false);
        gt.OnTick();
        Assert.DoesNotContain(
            gt.SnapshotAlarms().SelectMany(kv => kv.Value),
            a => a.CallerId == caller.Id);
    }
}
