using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for audit6 findings F25-F29.
[Collection("Ported")]
public sealed class Audit6BatchETests
{
    private sealed class TickProbe : GameObject { }

    private static int CountCoros(AsyncTicker ticker) => ticker.Slots.Values.Sum(s => s.Coros.Count);

    // F25: a never-completing async coro must not pin its slot forever — the
    // pending hold expires and the slot ticks the coro again.
    [Fact]
    public async Task TickerSlot_StuckAsyncCoro_PendingHoldExpiresAndTicksAgain()
    {
        using var env = GlobalTestEnv.Enter();
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        try
        {
            var interval = TimeSpan.FromMilliseconds(50);
            int ticks = 0;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task> stuck = async () => { Interlocked.Increment(ref ticks); await gate.Task; };
            ticker.AddCoro(stuck, interval);
            var slot = ticker.GetSlot(interval.TotalSeconds);
            Assert.NotNull(slot);
            slot!.PendingHoldTimeout = TimeSpan.FromMilliseconds(200);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref ticks) < 2 && sw.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(20);
            Assert.True(Volatile.Read(ref ticks) >= 2, "stuck coro was never re-ticked; pending hold never expired");
        }
        finally { ticker.Clear(); pool.Stop(); }
    }

    // F26: a tick delegate targeting a deleted object is a zombie — the sweep
    // evicts it even though its id matches no live tickable.
    [Fact]
    public void ReregisterTicks_EvictsDelegateTargetingDeletedTickable()
    {
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

    // F27: Stop after a mid-run interval change removes the coro from the
    // START interval's slot instead of orphaning it there.
    [Fact]
    public void GameTime_StopAfterIntervalChange_RemovesStartIntervalCoro()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, TimeUpdateSeconds = 2 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        var gameTime = new GameTime(settings, ticker, pool, autoLoad: false);
        try
        {
            gameTime.Start(ticker);
            Assert.NotNull(ticker.GetSlot(2));
            settings.TimeUpdateSeconds = 5;
            gameTime.Stop(ticker);
            var slot2 = ticker.GetSlot(2);
            Assert.NotNull(slot2);
            Assert.Empty(slot2!.Coros);
            Assert.Null(ticker.GetSlot(5));
        }
        finally { try { ticker.Stop(); } catch { } }
    }

    // F27: a non-positive interval is clamped to the 1s default, never
    // registered verbatim (which would spin a zero-delay hot loop).
    [Fact]
    public void GameTime_StartWithNonPositiveInterval_ClampsToOneSecond()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, TimeUpdateSeconds = 0 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        var gameTime = new GameTime(settings, ticker, pool, autoLoad: false);
        try
        {
            gameTime.Start(ticker);
            var slot = ticker.GetSlot(1);
            Assert.NotNull(slot);
            Assert.Single(slot!.Coros);
        }
        finally { try { gameTime.Stop(ticker); } catch { } try { ticker.Stop(); } catch { } }
    }

    // F27: the validator rejects non-positive tick intervals at startup.
    [Fact]
    public void SettingsValidator_NonPositiveTickInterval_Fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new AtherizSettings { SavePath = dir, SecretPath = dir, TimeUpdateSeconds = 0 };
            var message = new AtherizSettingsValidator().Validate(null, options).FailureMessage ?? "";
            Assert.Contains("TimeUpdateSeconds must be >0 (was 0).", message);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // F28: a key file without a certificate is a config error even when the
    // key file exists — it can never be used.
    [Fact]
    public void SettingsValidator_KeyWithoutCert_FailsEvenWhenKeyExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var keyFile = Path.Combine(dir, "key.pem");
            File.WriteAllText(keyFile, "fake-key");
            var options = new AtherizSettings { SavePath = dir, SecretPath = dir, SslKeyFile = keyFile, SslCertFile = "" };
            var result = new AtherizSettingsValidator().Validate(null, options);
            Assert.True(result.Failed);
            Assert.Contains("SslKeyFile is set but SslCertFile is empty; a key without a certificate cannot be used.",
                result.FailureMessage ?? "");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // F29: locking/unlocking a keyed door without the key refuses; carrying
    // the key (in contents) allows both. Null KeyId doors are unaffected
    // (pinned by the existing announce tests).
    [Fact]
    public void Door_KeyedLock_RequiresKeyInContents()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            room.AddObject(caller);
            var stranger = GameObject.Create("stranger");
            ObjectRegistry.AddObject(stranger);
            room.AddObject(stranger);
            var key = GameObject.Create("brass key");
            ObjectRegistry.AddObject(key);
            var door = new Door(new Coord("limbo", 0, 0, 0), new Coord("limbo", 0, 1, 0), "north", "south")
            {
                KeyId = key.Id,
            };

            Assert.False(door.TryLock(caller));
            Assert.False(door.Locked);
            caller.AddObject(key);
            Assert.True(door.TryLock(caller));
            Assert.True(door.Locked);
            Assert.False(door.TryUnlock(stranger));
            Assert.True(door.Locked);
            Assert.True(door.TryUnlock(caller));
            Assert.False(door.Locked);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // F29: DoorDesc flavor renders in Desc when set, and the bare desc keeps
    // its exact shape when empty.
    [Fact]
    public void Door_Desc_AppendsDoorDescWhenSet()
    {
        var from = new Coord("limbo", 0, 0, 0);
        var to = new Coord("limbo", 0, 1, 0);
        var plain = new Door(from, to, "north", "south");
        Assert.Equal("A closed door leading north", plain.Desc(from));
        var fancy = new Door(from, to, "north", "south") { DoorDesc = "Oak, bound in iron." };
        Assert.Equal("A closed door leading north Oak, bound in iron.", fancy.Desc(from));
    }
}
