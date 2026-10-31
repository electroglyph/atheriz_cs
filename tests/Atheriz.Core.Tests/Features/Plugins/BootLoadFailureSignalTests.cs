using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Plugins;

// Load-failure signal pins: a null game setup is ambiguous (a clean
// template-derived game versus a broken build), so the boot load reports
// how many discovered candidates failed. Reset relies on the count to
// decide whether creating the template world is safe.
[Collection("Ported")]
public class BootLoadFailureSignalTests
{
    // Stages a game dir: <tmp>/<name>.csproj (backdated, so fresh) +
    // bin/Release/net10.0/<name>.dll with the given bytes (fresh).
    private static string StageGameDir(string name, byte[] dllBytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_bootfail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj, "<Project></Project>");
        File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(-5));
        var binDir = Path.Combine(dir, "bin", "Release", "net10.0");
        Directory.CreateDirectory(binDir);
        var dllPath = Path.Combine(binDir, name + ".dll");
        File.WriteAllBytes(dllPath, dllBytes);
        File.SetLastWriteTimeUtc(dllPath, DateTime.UtcNow);
        return dir;
    }

    [Fact]
    public void LoadGameAssembliesAtBoot_SetupLessAssembly_ReturnsNullGameWithZeroFailures()
    {
        // Template-shaped game: a real assembly that loads cleanly but
        // provides no IGameSetup entry. Discovery must see it (so the
        // zero-failure result means "loaded, nothing to dispatch", not
        // "skipped as stale").
        using var env = GlobalTestEnv.Enter();
        var payload = File.ReadAllBytes(typeof(MyGame.GameSettings).Assembly.Location);
        var dir = StageGameDir("setupfree", payload);
        try
        {
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            Assert.True(PluginReloader.WorldCanBeRebuilt(settings, out _));
            var patched = PluginReloader.LoadGameAssembliesAtBoot(settings, out var game, out var failed);
            Assert.Equal(0, failed);
            Assert.Null(game);
            Assert.Equal(0, patched);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void LoadGameAssembliesAtBoot_CorruptAssembly_ReturnsNullGameWithFailureCount()
    {
        // Broken build: discovery still sees the dll, but the load throws.
        // The boot loader must report the failure instead of swallowing it
        // into an ambiguous null.
        using var env = GlobalTestEnv.Enter();
        var dir = StageGameDir("brokengame", "not a dll"u8.ToArray());
        try
        {
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            Assert.True(PluginReloader.WorldCanBeRebuilt(settings, out _));
            var patched = PluginReloader.LoadGameAssembliesAtBoot(settings, out var game, out var failed);
            Assert.Equal(1, failed);
            Assert.Null(game);
            Assert.Equal(0, patched);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
