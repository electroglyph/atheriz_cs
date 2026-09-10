using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The tick path resolves each firing alarm's caller with a single lookup:
// a live caller keeps its repeat alarm, a missing caller prunes it —
// synchronously on the tick, before any pool delivery.
[Collection("Ported")]
public sealed class GameTimeAlarmLookupTests
{
    [Fact]
    public void OnTick_DeadCallerRepeatAlarm_Pruned()
    {
        using var env = GlobalTestEnv.Enter();
        var gt = new GameTime(new AtherizSettings(), autoLoad: false);
        // Created but never registered: no object resolves for this id.
        var ghost = GameObject.Create("ghostalarm");
        gt.AddAlarm("?", "?", ghost.Id, repeat: true);
        Assert.Contains(gt.SnapshotAlarms(), kv => kv.Value.Any(a => a.CallerId == ghost.Id));
        gt.OnTick();
        Assert.DoesNotContain(
            gt.SnapshotAlarms().SelectMany(kv => kv.Value),
            a => a.CallerId == ghost.Id);
    }

    [Fact]
    public void OnTick_LiveCallerRepeatAlarm_Survives()
    {
        using var env = GlobalTestEnv.Enter();
        var gt = new GameTime(new AtherizSettings(), autoLoad: false);
        var caller = GameObject.Create("livealarm");
        ObjectRegistry.AddObject(caller);
        gt.AddAlarm("?", "?", caller, repeat: true);
        gt.OnTick();
        Assert.Contains(
            gt.SnapshotAlarms().SelectMany(kv => kv.Value),
            a => a.CallerId == caller.Id);
    }
}
