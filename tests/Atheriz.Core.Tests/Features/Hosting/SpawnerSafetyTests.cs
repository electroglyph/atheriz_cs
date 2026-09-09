using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// A failed spawn must not leave a pid claim behind pointing at a dead CLI:
// the spawner reports failure and the starter releases its claim.
[Collection("Ported")]
public class SpawnerSafetyTests
{
    [Fact]
    public async Task SpawnDaemon_InvalidHost_ReportsFailureWithoutSpawning()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(await DaemonSpawner.SpawnDaemonAsync(new[] { "--host", "bad host!" }, dir));
            Assert.False(File.Exists(Path.Combine(dir, "save", "server.log")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void StartPath_ReleasesClaimOnSpawnFailure()
    {
        // Structural pin: the start path keeps its claim handle (no `out _`)
        // and releases it when the spawn reports failure.
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Program.cs");
        Assert.Contains("out var spawnClaim", src);
        Assert.Contains("spawnClaim?.Release()", src);
    }

    [Fact]
    public async Task SpawnDaemon_InvalidFolder_ReportsFailure()
    {
        // Any spawn failure (here: an impossible working folder) reports
        // false so the caller releases its pid claim — never a success.
        Assert.False(await DaemonSpawner.SpawnDaemonAsync(new[] { "--host", "0.0.0.0" }, "/nonexistent\0bad"));
    }

    [Fact]
    public void SpawnDaemon_HasNoLogPumpFallback()
    {
        // Structural pin: the readerless-pipe pump fallback is gone — on a
        // bash-less host the spawner fails loudly instead of spawning a
        // time-bomb daemon.
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Cli/DaemonSpawner.cs");
        Assert.DoesNotContain("BeginOutputReadLine", src);
        Assert.Contains("use --foreground instead", src);
    }

    [Fact]
    public void SpawnDaemon_DisplayUsesExplicitFlagsAndDefaults()
    {
        // Structural pin: the status lines derive from the explicit spawn
        // flags plus the shipped defaults (the child's baseline) — never the
        // parent's settings, which describe a different folder's config.
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Cli/DaemonSpawner.cs");
        Assert.DoesNotContain("StopHandler.EffectiveSettingsValue", src);
        Assert.Contains("shippedDefaults", src);
    }
}
