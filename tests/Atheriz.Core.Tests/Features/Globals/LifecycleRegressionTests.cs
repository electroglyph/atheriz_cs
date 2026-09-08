using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// Lifecycle regression pins.
[Collection("Ported")]
public class LifecycleRegressionTests
{
    private static void Reset() => Autosave.Reset();
    private static bool HoldsTick(AsyncTicker ticker) => ticker.Slots.Any(kv => kv.Value.Coros.Any(d => d.Method.Name.Contains("AutosaveTick")));

    // Port of autosave.py `if not settings.AUTOSAVE_MINUTES`: falsy covers
    // negatives — a negative interval must not register a coro.
    [Fact]
    public void Autosave_NegativeMinutes_Disabled()
    {
        Reset();
        var s = new AtherizSettings { AutosaveMinutes = -5 };
        var ticker = new AsyncTicker(new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
        try
        {
            Autosave.StartAutosave(ticker, s);
            Assert.Empty(ticker.Slots);
            Assert.False(Autosave.AutosaveStarted);
        }
        finally { Reset(); ticker.Clear(); }
    }

    // Parameterless StopAutosave must reach a coro registered via the
    // explicit-ticker StartAutosave overload (Python always uses the global
    // ticker, so it needs no handle; here the handle must be remembered).
    [Fact]
    public void Autosave_ParameterlessStop_ReachesExplicitTickerStart()
    {
        Reset();
        var s = new AtherizSettings { AutosaveMinutes = 5 };
        var ticker = new AsyncTicker(new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
        try
        {
            Autosave.StartAutosave(ticker, s);
            Assert.True(HoldsTick(ticker));
            Autosave.StopAutosave();
            Assert.False(HoldsTick(ticker));
            Assert.False(Autosave.AutosaveStarted);
        }
        finally { Reset(); ticker.Clear(); }
    }

    // Per-path salt cache: two secret dirs must not share one slot's salt,
    // and explicit-path reads must not poison the default slot.
    [Fact]
    public void Salt_ExplicitPaths_AreIsolatedPerPath()
    {
        using var env = GlobalTestEnv.Enter();
        var dirA = Path.Combine(env.TempPath, "secA");
        var dirB = Path.Combine(env.TempPath, "secB");
        try
        {
            SaltProvider.Clear();
            var a = SaltProvider.GetSalt(dirA);
            var b = SaltProvider.GetSalt(dirB);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotEqual(a, b);
            Assert.Equal(a, File.ReadAllText(Path.Combine(dirA, "salt.txt")).Trim());
            Assert.Equal(b, File.ReadAllText(Path.Combine(dirB, "salt.txt")).Trim());
            // Repeat reads are stable per path.
            Assert.Equal(a, SaltProvider.GetSalt(dirA));
            Assert.Equal(b, SaltProvider.GetSalt(dirB));
        }
        finally { SaltProvider.Clear(); }
    }

    // Port of time.py Fraction(str(TICK_MINUTES)): with a fractional
    // TickMinutes (0.1) one hour is exactly 600 ticks; binary-double math
    // makes tph 599.999... and leaks a phantom "0 minutes" into the desc.
    [Fact]
    public void GameTime_FractionalTickMinutes_TimespanIsExact()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TickMinutes = 0.1 };
        var gt = new GameTime(settings, autoLoad: false);
        var span = gt.GetTimespan(600);
        Assert.Equal(1, span.Hours);
        Assert.Equal("1 hour ago", span.Desc);
    }

    // Missing-caller prune: a repeat alarm for a dead id is removed (warn
    // once) instead of warning on every tick forever like Python.
    [Fact]
    public void GameTime_MissingCallerRepeatAlarm_Pruned()
    {
        using var env = GlobalTestEnv.Enter();
        var gt = new GameTime(new AtherizSettings(), autoLoad: false);
        gt.AddAlarm("?", "?", 987654321, repeat: true);
        Assert.Single(gt.SnapshotAlarms()[("?", "?")]);
        gt.OnTick();
        // Entry pruned (the key row stays, as in Python's remove_alarm —
        // only the dead caller entry is gone).
        Assert.Empty(gt.SnapshotAlarms()[("?", "?")]);
    }
}
