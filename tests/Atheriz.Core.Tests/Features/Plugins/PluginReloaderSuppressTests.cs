// Pins for the shared PluginReloader.Suppress helper: convertible catch sites
// keep their byte-identical op-prefixed debug text through real reload paths,
// while the ExitGate semaphore catch and the Console error channel stay in
// their original spellings.
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Plugins;

[Collection("Ported")]
public sealed class PluginReloaderSuppressTests
{
    private sealed class OldReloaderProbe : GameObject { }

    private sealed class NewReloaderProbe : GameObject
    {
        public override void ResolveRelations() => throw new InvalidOperationException("rel-boom");
    }

    private sealed class TickingReloaderProbe : GameObject
    {
        public override void AtTick() => throw new InvalidOperationException("tick-boom");
    }

    [Fact]
    public void PatchLiveObjects_ThrowingResolveRelations_LogsPatchLiveObjectsOpPrefix()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        try
        {
            ObjectRegistry.ClearAll();
            // Distinct ids: AddObject keys by Id, so two default-constructed
            // probes would collide and the second would replace the first,
            // leaving no old-type instance for PatchLiveObjects to find.
            var oldProbe = new OldReloaderProbe { Id = 61001 };
            var newProbe = new NewReloaderProbe { Id = 61002 };
            ObjectRegistry.AddObject(oldProbe);
            ObjectRegistry.AddObject(newProbe);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                PluginReloader.PatchLiveObjects(typeof(OldReloaderProbe), typeof(NewReloaderProbe));
                log = cap.Read();
            }
            Assert.Contains("Suppressed PluginReloader.PatchLiveObjects: rel-boom", log);
        }
        finally
        {
            ObjectRegistry.ClearAll();
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }

    [Fact]
    public void ReregisterTicks_ThrowingAtTick_LogsReregisterTicksOpPrefix()
    {
        using var env = GlobalTestEnv.Enter();
        AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = env.TempPath });
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        try
        {
            ObjectRegistry.ClearAll();
            ObjectRegistry.AddObject(new TickingReloaderProbe { IsTickable = true, TickSeconds = 60 });
            PluginReloader.ReregisterTicks(ticker);
            List<Delegate> coros = [];
            foreach (var slot in ticker.Slots.Values)
                coros.AddRange(slot.Coros.ToList());
            Assert.NotEmpty(coros);
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                foreach (var d in coros)
                    if (d is Action act)
                        act();
                log = cap.Read();
            }
            Assert.Contains("Suppressed PluginReloader.ReregisterTicks: tick-boom", log);
        }
        finally
        {
            ticker.Clear();
            pool.Stop();
            ObjectRegistry.ClearAll();
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
        }
    }

    [Fact]
    public void Suppress_ExitGateAndErrorChannel_StaysAsIs()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        Assert.Contains("private static void Suppress(string op, Exception ex)", src);
        Assert.Contains("Suppressed PluginReloader.ExitGate: ", src);
        Assert.Contains("Console.Error.WriteLine", src);
    }
}
