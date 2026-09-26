using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Plugins;

namespace Atheriz.Core.Tests.Features.Plugins;

// Audit 10 finding 11: a failed plugin load must not displace the live
// loader. The old code assigned _loader before the load was known to
// succeed, so one bad deploy left LoadedAssembly == null and the next
// reload's EvictStaleCommands(null) no-op'd, keeping stale command
// instances alive. Neuter (assign-then-load) replaces the loader with an
// empty one and both identity assertions fail.
[Collection("Ported")]
public sealed class PluginFailedReloadTests
{
    private static string CopyTestAssembly()
    {
        var src = typeof(PluginFailedReloadTests).Assembly.Location;
        var dst = Path.Combine(Path.GetTempPath(), "atheriz_failedreload_" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(src, dst);
        return dst;
    }

    [Fact]
    public async Task ReloadAsync_FailedLoad_KeepsOldLoaderForEviction()
    {
        using var env = GlobalTestEnv.Enter();
        var copy = CopyTestAssembly();
        var loaderField = typeof(PluginReloader).GetField("_loader", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(loaderField);
        var prev = loaderField.GetValue(null);
        PluginLoader? ours = null;
        try
        {
            ours = new PluginLoader();
            ours.Load(copy);
            Assert.NotNull(ours.LoadedAssembly);
            loaderField.SetValue(null, ours);
            var bad = Path.Combine(Path.GetTempPath(), "atheriz_corrupt_" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(bad, "not a managed assembly"u8.ToArray());
            try
            {
                var ticker = GlobalServices.GetAsyncTicker();
                var pool = GlobalServices.GetAsyncThreadPool();
                Assert.False(await PluginReloader.ReloadAsync(bad, ticker, pool));
                // The live slot still names the previous generation, so the
                // next reload has an oldAssembly to evict against.
                Assert.Same(ours, loaderField.GetValue(null));
                Assert.NotNull(ours.LoadedAssembly);
            }
            finally { try { File.Delete(bad); } catch { } }
        }
        finally
        {
            loaderField.SetValue(null, prev);
            try { ours?.Unload(); } catch { }
            try { File.Delete(copy); } catch { }
        }
    }
}
