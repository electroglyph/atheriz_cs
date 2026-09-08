using System.Reflection;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// Plugin loader/reloader regression pins.

// Unassignable replacement on a DISTINCT base (Channel) so a missing skip can't
// hide behind dictionary key collision: string is not a Channel — Load must skip.
[EntityReplacement(typeof(Channel), typeof(string))]
internal sealed class BadReplacementProbe { }

// Valid replacement marker for the test-assembly-as-plugin load path.
[EntityReplacement(typeof(GameObject), typeof(GoodProbeReplacement))]
internal sealed class GoodProbeMarker { }

public sealed class GoodProbeReplacement : GameObject { }

internal sealed class NeverRegisteredMarker : GameObject { }

[Collection("Ported")]
public class PluginReloadRegressionTests
{
    private static string CopyTestAssembly()
    {
        var src = typeof(PluginReloadRegressionTests).Assembly.Location;
        var dst = Path.Combine(Path.GetTempPath(), "atheriz_plugin_" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(src, dst);
        return dst;
    }

    private static int CountCoros(AsyncTicker ticker) => ticker.Slots.Values.Sum(s => s.Coros.Count);

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFound_AndNotLoaded()
    {
        using var loader = new PluginLoader();
        Assert.Throws<FileNotFoundException>(() => loader.Load("/tmp/atheriz_missing_plugin_xyz.dll"));
        Assert.False(loader.IsLoaded);
    }

    [Fact]
    public void Load_ExcludedAssembly_SkipsWithoutLoading()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_plug_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fake = Path.Combine(dir, "System.FakePlugin.dll");
        File.WriteAllBytes(fake, new byte[] { 0x4D, 0x5A });
        try
        {
            using var loader = new PluginLoader();
            loader.Load(fake); // excluded by name before any load — must not throw, must not load
            Assert.False(loader.IsLoaded);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void IsValidReplacement_Contract()
    {
        var m = typeof(PluginLoader).GetMethod("IsValidReplacement", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? Call(Type? b, Type? r) => m.Invoke(null, new object?[] { b, r });
        Assert.True((bool)Call(typeof(GameObject), typeof(GoodProbeReplacement))!);
        Assert.True((bool)Call(typeof(GameObject), typeof(GameObject))!);
        Assert.False((bool)Call(typeof(GameObject), typeof(string))!);
        Assert.False((bool)Call(null, typeof(string))!);
        Assert.False((bool)Call(typeof(GameObject), null)!);
    }

    [Fact]
    public void Load_TestAssembly_SkipsUnassignable_RegistersValid()
    {
        var copy = CopyTestAssembly();
        try
        {
            using var loader = new PluginLoader();
            loader.Load(copy);
            Assert.True(loader.IsLoaded);
            Assert.DoesNotContain(typeof(string), loader.Replacements.Values);
            Assert.DoesNotContain(typeof(Channel), loader.Replacements.Keys);
            Assert.True(loader.Replacements.TryGetValue(typeof(GameObject), out var repl));
            // NOTE: compared by name, not Type identity — the replacement Type lives
            // in the plugin ALC (a distinct Type instance by design; base types unify
            // via the default context, which is why TryGetValue above succeeds).
            Assert.Equal(typeof(GoodProbeReplacement).FullName, repl!.FullName);
        }
        finally { try { File.Delete(copy); } catch { } }
    }

    [Fact]
    public async Task ReloadAsync_MissingFile_ReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        var pool = GlobalServices.GetAsyncThreadPool();
        Assert.False(await PluginReloader.ReloadAsync("/tmp/atheriz_missing_xyz.dll", ticker, pool));
    }

    [Fact]
    public async Task ReloadAsync_ExcludedAssembly_ReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        var pool = GlobalServices.GetAsyncThreadPool();
        Assert.False(await PluginReloader.ReloadAsync("System.Fake.dll", ticker, pool));
    }

    [Fact]
    public void DiscoverGameAssembly_NoCandidates_ReturnsZero()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_plugdisc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            var outList = new List<string>();
            Assert.Equal(0, PluginReloader.DiscoverGameAssembly(settings, outList));
            Assert.Empty(outList);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void DiscoverGameAssembly_CsprojWithoutDll_SkipsWithoutBuilding()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_plugdisc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "fakegame.csproj"), "<Project></Project>");
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            var outList = new List<string>();
            // Reload never shells a compiler: csproj with no up-to-date dll is skipped.
            Assert.Equal(0, PluginReloader.DiscoverGameAssembly(settings, outList));
            Assert.Empty(outList);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReregisterTicks_IsIdempotent_NoDuplicateCoros()
    {
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        try
        {
            var o = new GoodProbeReplacement { IsTickable = true, TickSeconds = 60 };
            ObjectRegistry.AddObject(o);
            Assert.Equal(0, CountCoros(ticker));
            PluginReloader.ReregisterTicks(ticker);
            Assert.Equal(1, CountCoros(ticker));
            PluginReloader.ReregisterTicks(ticker);
            Assert.Equal(1, CountCoros(ticker));
            // Static-lambda miss path: Target-null delegates are left alone, no crash.
            ticker.AddCoro(static () => { }, TimeSpan.FromSeconds(60));
            PluginReloader.ReregisterTicks(ticker);
            Assert.Equal(2, CountCoros(ticker));
        }
        finally { ticker.Clear(); pool.Stop(); }
    }

    [Fact]
    public void PatchLiveObjects_NoLiveInstances_ReturnsZero()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Equal(0, PluginReloader.PatchLiveObjects(typeof(NeverRegisteredMarker), typeof(string)));
    }

    [Fact]
    public void PatchLiveObjects_IncompatibleNewType_PatchesZeroKeepsOld()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new GoodProbeReplacement();
        ObjectRegistry.AddObject(o);
        Assert.Equal(0, PluginReloader.PatchLiveObjects(typeof(GoodProbeReplacement), typeof(string)));
        Assert.Contains(o, ObjectRegistry.FilterBy(x => ReferenceEquals(x, o)));
    }
}
