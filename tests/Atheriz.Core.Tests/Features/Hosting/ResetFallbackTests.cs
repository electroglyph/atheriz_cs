using System.Reflection;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// Reset template-fallback pins: scaffolded games ship no IGameSetup, so a
// clean load with a null setup must still rebuild the template world.
// Only a failed (or vanished) load refuses — after a wipe, refusing on a
// clean load would destroy the world and hand back nothing.
[Collection("Ported")]
public class ResetFallbackTests
{
    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    private static string StageGameDir(string prefix, string name, byte[]? dllBytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj, "<Project></Project>");
        File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(-5));
        if (dllBytes is not null)
        {
            var binDir = Path.Combine(dir, "bin", "Release", "net10.0");
            Directory.CreateDirectory(binDir);
            var dllPath = Path.Combine(binDir, name + ".dll");
            File.WriteAllBytes(dllPath, dllBytes);
            File.SetLastWriteTimeUtc(dllPath, DateTime.UtcNow);
        }
        return dir;
    }

    [Fact]
    public void TemplateFallbackRefused_FailedLoad_Refuses()
    {
        using var env = GlobalTestEnv.Enter();
        var payload = File.ReadAllBytes(typeof(MyGame.GameSettings).Assembly.Location);
        var dir = StageGameDir("atheriz_resetfb_", "setupfree", payload);
        try
        {
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            Assert.True(ResetHandler.TemplateFallbackRefused(settings, failedLoads: 1));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TemplateFallbackRefused_MissingAssembly_Refuses()
    {
        // Project with no built dll: nothing to fall back from — the world
        // must not be rebuilt from the template behind the game's back.
        using var env = GlobalTestEnv.Enter();
        var dir = StageGameDir("atheriz_resetfb_", "nodll", dllBytes: null);
        try
        {
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            Assert.True(ResetHandler.TemplateFallbackRefused(settings, failedLoads: 0));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TemplateFallbackRefused_SetupLessGame_Proceeds()
    {
        // The reported bug: a scaffolded game loads cleanly but provides no
        // world-setup entry. The template world is its intended world.
        using var env = GlobalTestEnv.Enter();
        var payload = File.ReadAllBytes(typeof(MyGame.GameSettings).Assembly.Location);
        var dir = StageGameDir("atheriz_resetfb_", "setupfree", payload);
        try
        {
            var settings = new AtherizSettings { SavePath = Path.Combine(dir, "save") };
            Assert.False(ResetHandler.TemplateFallbackRefused(settings, failedLoads: 0));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task ResetAsync_CorruptGameAssembly_RefusesWithoutCreatingWorld()
    {
        // End to end through the real wipe path: a broken game build still
        // refuses after the wipe, and no template world is layered on top.
        // Returns before any server start, so no ports bind and no daemon
        // spawns. Runs with the staged dir as CWD so it reads as a game
        // folder (the markerless own-save-leaf branch), like `reset` from a
        // real game folder.
        var dir = StageGameDir("atheriz_resetbad_", "brokengame", "not a dll"u8.ToArray());
        File.WriteAllText(Path.Combine(dir, "GameSettings.cs"), "// game");
        File.SetLastWriteTimeUtc(Path.Combine(dir, "GameSettings.cs"), DateTime.UtcNow.AddMinutes(-5));
        var origCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(dir);
        var settings = new AtherizSettings
        {
            SavePath = Path.Combine(dir, "save"),
            SecretPath = Path.Combine(dir, "secret"),
            WebserverPort = 39411,
            TelnetEnabled = false,
        };
        SetEffectiveSettings(settings);
        var sb = new StringWriter();
        var origOut = Console.Out;
        var origErr = Console.Error;
        var origIn = Console.In;
        Console.SetOut(sb);
        Console.SetError(sb);
        Console.SetIn(new StringReader("y" + Environment.NewLine));
        int rc;
        try
        {
            rc = await ResetHandler.ResetAsync(null, null);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            Console.SetIn(origIn);
            SetEffectiveSettings(null);
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            try { AtherizDbContext.ReopenDatabase(); } catch { }
            try { AtherizDbContextFactory.ReopenDatabase(); } catch { }
        }
        try
        {
            Assert.Equal(1, rc);
            Assert.Contains("could not be loaded", sb.ToString());
            Assert.False(File.Exists(Path.Combine(dir, "save", "database.sqlite3")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
