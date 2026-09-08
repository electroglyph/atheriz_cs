using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// ServerLifecycle readiness, AssetPathResolver table,
// SyncChecker settings/excludes, GameTemplate assembly/namespace homes.
[Collection("Ported")]
public class InfraRegressionTests
{
    [Fact]
    public void DoShutdown_ClearsStartupSucceeded_ReadyGoesDark()
    {
        // /ready must stop reporting ok once shutdown begins (DoShutdown
        // never cleared the flag).
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 0 };
        try
        {
            ServerLifecycle.DoStartup(settings);
            Assert.True(ServerLifecycle.StartupSucceeded);
            ServerLifecycle.DoShutdown(settings);
            Assert.False(ServerLifecycle.StartupSucceeded);
        }
        finally { try { ServerLifecycle.Reset(); } catch { } }
    }

    [Fact]
    public void ResolveCandidates_FirstExistingWins_Deduped()
    {
        // The resolution table must keep priority order and collapse duplicate
        // rows (appBaseDir/wwwroot appeared twice).
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(env.TempPath, "wwwroot");
        Directory.CreateDirectory(dir);
        var missing = Path.Combine(env.TempPath, "nope");
        var got = AssetPathResolver.ResolveCandidates(new[] { missing, dir, dir });
        Assert.Equal(dir, got);
        Assert.Null(AssetPathResolver.ResolveCandidates(new[] { missing }));
    }

    [Fact]
    public void CheckSync_SettingsParam_DisablesCheck()
    {
        // Explicit settings (not Global) gate the sync check .
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        Directory.CreateDirectory(Path.Combine(game, "web", "templates", "webclient"));
        var off = new AtherizSettings { WebclientSyncCheck = false };
        Assert.Null(WebclientSyncChecker.CheckSync(game, env.TempPath, null, off));
    }

    [Fact]
    public void CheckSync_ExcludeDirNames_SkipsMatchedDirs()
    {
        // Opt-in excludes drop version-control noise; default (null) keeps
        // verbatim Python rglob parity.
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(env.TempPath, "game");
        var eng = Path.Combine(env.TempPath, "eng");
        var gameWc = Path.Combine(game, "web", "templates", "webclient");
        var engWc = Path.Combine(eng, "templates", "webclient");
        Directory.CreateDirectory(gameWc);
        Directory.CreateDirectory(engWc);
        File.WriteAllText(Path.Combine(engWc, "a.js"), "x");
        File.WriteAllText(Path.Combine(gameWc, "a.js"), "x");
        Directory.CreateDirectory(Path.Combine(gameWc, ".git"));
        File.WriteAllText(Path.Combine(gameWc, ".git", "noise"), "n");
        var on = new AtherizSettings { WebclientSyncCheck = true };
        var plain = WebclientSyncChecker.CheckSync(game, eng, null, on, null);
        Assert.NotNull(plain);
        Assert.Contains(".git/noise", plain!["templates"]["extra"]);
        var excl = WebclientSyncChecker.CheckSync(game, eng, null, on, new[] { ".git" });
        Assert.NotNull(excl);
        Assert.DoesNotContain(".git/noise", excl!["templates"]["extra"]);
    }

    [Fact]
    public void GameTemplate_CheckedInSources_CarryAssemblyDescription_AndMyGameNamespace()
    {
        // checked-in template mirrors the scaffold emitters (AI()).
        var asmInfo = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.GameTemplate/AssemblyInfo.cs");
        Assert.Contains("AssemblyDescription", asmInfo);
        var custom = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.GameTemplate/CustomObject.cs");
        Assert.Contains("namespace MyGame;", custom);
    }
}
