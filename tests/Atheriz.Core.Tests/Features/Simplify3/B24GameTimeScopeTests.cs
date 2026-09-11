// Pins for U1 (GameTime identical-lock scope conversion): the converted alarm,
// tick, and time-read paths keep their behavior under the shared scopes —
// concurrent alarm mutation, single-alarm removal, caller-wide clearing, and
// tick/time consistency on the real GameTime engine path.
using System.Collections.Concurrent;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B24GameTimeScopeTests
{
    private static GameTime NewGameTime(string savePath)
        => new(new AtherizSettings { SavePath = savePath }, autoLoad: false);

    [Fact]
    public void AddAlarm_ConcurrentAdds_AllEntriesPresent()
    {
        using var env = GlobalTestEnv.Enter();
        var gameTime = NewGameTime(env.TempPath);
        var owner = GameObject.Create("ScopeOwner");
        ObjectRegistry.AddObject(owner);
        const int writers = 8;
        using var barrier = new Barrier(writers + 1);
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, writers).Select(i =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    gameTime.AddAlarm("1", i.ToString(), owner.Id);
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "writer did not finish; possible deadlock");
        Assert.Empty(errors);
        var snap = gameTime.SnapshotAlarms();
        Assert.Equal(writers, snap.Values.Sum(l => l.Count));
    }

    [Fact]
    public void RemoveAlarm_AddThenRemove_EntryGone()
    {
        using var env = GlobalTestEnv.Enter();
        var gameTime = NewGameTime(env.TempPath);
        var owner = GameObject.Create("ScopeOwner");
        ObjectRegistry.AddObject(owner);
        gameTime.AddAlarm("2", "30", owner.Id);
        gameTime.RemoveAlarm("2", "30", owner.Id);
        var snap = gameTime.SnapshotAlarms();
        Assert.True(!snap.TryGetValue(("2", "30"), out var list) || list.Count == 0);
    }

    [Fact]
    public void RemoveAlarmsByCaller_ClearsOnlyThatCaller()
    {
        using var env = GlobalTestEnv.Enter();
        var gameTime = NewGameTime(env.TempPath);
        var owner = GameObject.Create("ScopeOwner");
        var other = GameObject.Create("ScopeOther");
        ObjectRegistry.AddObject(owner);
        ObjectRegistry.AddObject(other);
        gameTime.AddAlarm("3", "15", owner.Id);
        gameTime.AddAlarm("3", "15", other.Id);
        gameTime.RemoveAlarmsByCaller(owner.Id);
        var snap = gameTime.SnapshotAlarms();
        Assert.True(snap.TryGetValue(("3", "15"), out var list));
        Assert.Single(list);
        Assert.Equal(other.Id, list[0].CallerId);
    }

    [Fact]
    public void GetTime_ReflectsTicksAfterOnTick()
    {
        using var env = GlobalTestEnv.Enter();
        var gameTime = NewGameTime(env.TempPath);
        long before = gameTime.Ticks;
        gameTime.OnTick();
        Assert.Equal(before + 1, gameTime.Ticks);
        Assert.Equal(before + 1, gameTime.GetTime().Ticks);
    }
}
