using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// Shadow-field probe: mirrors a real game type declaring its own `_flags`
// with an incompatible meaning. The patch must pair the old value with the
// compatible base slot, never the shadow.
[EntityReplacement(typeof(Node), typeof(ShadowNodeProbe))]
public sealed class ShadowNodeProbe : Node
{
    private ulong _flags;
    public ShadowNodeProbe() : base(new Coord("limbo", 0, 0, 0)) { _flags = 0; }
    public ulong ReadShadowFlagsForTests() => _flags;
}

// Boot-time game load pins: a fresh start must discover + patch the game
// assembly exactly like a hot reload, instead of booting engine-only.
[Collection("Ported")]
public class PluginBootLoadTests
{
    // Stages a fake game dir: <tmp>/bootgame_<guid>/bootgame.csproj (written
    // first, so older) + bin/Release/net10.0/bootgame.dll (a copy of this test
    // assembly, which carries the GoodProbeReplacement marker). Returns the
    // game dir; the caller owns cleanup.
    private static string StageFakeGame()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_bootgame_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, "bootgame.csproj");
        File.WriteAllText(csproj, "<Project></Project>");
        // Discovery treats a dll older than its csproj as stale (never builds):
        // backdate the csproj and stamp the copied dll as fresh.
        File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(-5));
        var binDir = Path.Combine(dir, "bin", "Release", "net10.0");
        Directory.CreateDirectory(binDir);
        var dllPath = Path.Combine(binDir, "bootgame.dll");
        File.Copy(typeof(PluginReloadRegressionTests).Assembly.Location, dllPath);
        File.SetLastWriteTimeUtc(dllPath, DateTime.UtcNow);
        return dir;
    }

    [Fact]
    public void BootLoad_StagedGame_PatchesLiveObjects()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = StageFakeGame();
        try
        {
            var o = new GameObject();
            ObjectRegistry.AddObject(o);
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            var patched = PluginReloader.LoadGameAssembliesAtBoot(settings);
            Assert.True(patched >= 1);
            // Exact-type engine instance converted to the replacement type.
            // Compared by name: the replacement Type lives in the plugin ALC
            // (a distinct Type instance by design).
            Assert.DoesNotContain(o, ObjectRegistry.FilterBy(x => ReferenceEquals(x, o)));
            Assert.Contains(ObjectRegistry.FilterBy(x => true),
                x => x.GetType().FullName == typeof(GoodProbeReplacement).FullName);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BootLoad_NoGameAssembly_ReturnsZeroKeepsEngineObjects()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_bootempty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var o = new GameObject();
            ObjectRegistry.AddObject(o);
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            Assert.Equal(0, PluginReloader.LoadGameAssembliesAtBoot(settings));
            Assert.Contains(o, ObjectRegistry.FilterBy(x => ReferenceEquals(x, o)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BootLoad_NullSettings_DoesNotThrow()
    {
        using var env = GlobalTestEnv.Enter();
        var before = ObjectRegistry.Count;
        var patched = PluginReloader.LoadGameAssembliesAtBoot(null);
        Assert.True(patched >= 0);
        Assert.Equal(before, ObjectRegistry.Count);
    }

    [Fact]
    public void BootLoad_ShadowField_PairsCompatibleSlotKeepsFlagsAlive()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = StageFakeGame();
        try
        {
            var o = new Node(new Coord("limbo", 0, 0, 0)) { IsPc = true };
            ObjectRegistry.AddObject(o);
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            var patched = PluginReloader.LoadGameAssembliesAtBoot(settings);
            Assert.True(patched >= 1);
            // Compared by name: the replacement Type lives in the plugin ALC
            // (a distinct Type instance by design).
            var repl = ObjectRegistry.FilterBy(x => x.GetType().FullName == typeof(ShadowNodeProbe).FullName);
            Assert.NotEmpty(repl);
            // Base-slot flag state survived the patch and reads don't throw
            // (production NRE: patched instances had null _flags).
            Assert.True(repl[0].IsPc);
            Assert.True(repl[0].IsNode);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BootLoad_NewerObjAndTestSources_StillDiscoversDll()
    {
        // Live-boot regression: generated outputs (obj/bin) and test sources
        // never feed the game dll, so newer stamps there must not mark it
        // stale (which booted engine-only with zero game code, unhealable by
        // an incremental game-only rebuild).
        using var env = GlobalTestEnv.Enter();
        var dir = StageFakeGame();
        try
        {
            var objDir = Path.Combine(dir, "obj", "Release", "net10.0");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "bootgame.AssemblyInfo.cs"), "// generated");
            var testsDir = Path.Combine(dir, "bootgametests");
            Directory.CreateDirectory(testsDir);
            File.WriteAllText(Path.Combine(testsDir, "ProbeTests.cs"), "// test");
            foreach (var f in Directory.GetFiles(objDir, "*.cs"))
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(5));
            foreach (var f in Directory.GetFiles(testsDir, "*.cs"))
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(5));
            var o = new GameObject();
            ObjectRegistry.AddObject(o);
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            var patched = PluginReloader.LoadGameAssembliesAtBoot(settings);
            Assert.True(patched >= 1);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BootLoad_NewerGameSource_SkipsStaleDll()
    {
        // The other direction stays loud: a genuinely newer GAME source still
        // marks the dll stale instead of loading outdated game code.
        using var env = GlobalTestEnv.Enter();
        var dir = StageFakeGame();
        try
        {
            File.WriteAllText(Path.Combine(dir, "GameCode.cs"), "// game");
            var o = new GameObject();
            ObjectRegistry.AddObject(o);
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            var patched = PluginReloader.LoadGameAssembliesAtBoot(settings);
            Assert.Equal(0, patched);
            Assert.Contains(o, ObjectRegistry.FilterBy(x => ReferenceEquals(x, o)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
