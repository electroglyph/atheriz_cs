using System.Reflection;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Correctness;

// Fourth residual batch for the audit10 re-verification: two production
// gaps the first pass left behind, plus behavioral edge pins for fixes
// that were previously covered by shape scans or happy-path-only tests.
[Collection("Ported")]
public sealed class Audit10ResidualBatch4Tests
{
    [Fact]
    public void BootLoad_AssignsLoaderOnlyAfterSuccessfulLoad()
    {
        // Finding 11 sibling: LoadGameAssembliesAtBoot unloaded the live
        // loader before the next candidate was known to load, so one bad
        // boot candidate discarded the previous generation and left a
        // loader with no assembly (the next reload's eviction key gone).
        // The hot path (ReloadCoreAsync) already loads into a local first;
        // the boot loop must do the same. Neuter (assign-then-load) brings
        // back `_loader = new PluginLoader();` and fails the last guard.
        var src = SourceScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        var region = SourceScan.Region(src, "public static int LoadGameAssembliesAtBoot(AtherizSettings? settings, out IGameSetup? gameSetup)");
        Assert.Contains("var newLoader = new PluginLoader();", region, StringComparison.Ordinal);
        Assert.Contains("_loader = newLoader;", region, StringComparison.Ordinal);
        Assert.DoesNotContain("_loader = new PluginLoader();", region, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreClosedDoor_ForcedReclose_MarksDoorsModified()
    {
        // Finding 25 follow-on: the fallback did a raw `Closed = true`
        // under an explicit write hold (MarkAfterWrite early-returns while
        // the hold is taken) plus MapClose, but never sent the
        // doors-modified mark — unlike ForceClose — so the revert never
        // checkpointed. Deny `close` on an open door, force the fallback,
        // and require both the state and the generation bump.
        using var _ = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var from = new Coord("pinrestore", 0, 0, 0);
        var to = new Coord("pinrestore", 0, 1, 0);
        var door = Door.Create(from, "north", to, "south", closed: false, locked: false);
        nh.AddDoor(door);
        var pc = GameObject.Create("pinrestore-pc");
        Assert.False(pc.IsSuperUser);
        door.AddLock("close", _ => false);
        var genField = typeof(NodeHandler).GetField("_doorGen", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(genField);
        long gen0 = (long)genField.GetValue(nh)!;

        LoggedInExitCommand.RestoreClosedDoor(door, pc);

        Assert.True(door.Closed);
        Assert.True((long)genField.GetValue(nh)! > gen0);
    }

    [Fact]
    public void LoadSettingsFor_ReturnsGameFolderValues()
    {
        // Finding 9 behavioral half: the CLI must read the game folder's
        // own appsettings.json (ports, name, paths). The no-BaseDirectory
        // shape pin rules out the engine override; this pins that the game
        // file is actually honored. Atheriz.Server grants no
        // InternalsVisibleTo, so this goes through reflection (free in tests).
        var asm = typeof(Atheriz.Server.Hosting.AdminAuthServices).Assembly;
        var type = asm.GetType("Atheriz.Server.Cli.StopHandler");
        Assert.NotNull(type);
        var method = type.GetMethod("LoadSettingsFor", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var gameDir = Path.Combine(Path.GetTempPath(), "atheriz_cli9_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameDir);
        try
        {
            File.WriteAllText(Path.Combine(gameDir, "appsettings.json"),
                """{"Atheriz":{"SavePath":"mysave","ServerName":"MyGame","TelnetPort":5555,"WebserverPort":13003}}""");
            var s = (AtherizSettings)method.Invoke(null, [gameDir])!;
            Assert.Equal(13003, s.WebserverPort);
            Assert.Equal(5555, s.TelnetPort);
            Assert.Equal("MyGame", s.ServerName);
            Assert.Equal("mysave", s.SavePath);
        }
        finally { try { Directory.Delete(gameDir, true); } catch { } }
    }

    [Fact]
    public void SaveLoad_Roundtrip_UnderEnvOverride_UsesOneDatabase()
    {
        // Finding 15 behavioral half: with ATHERIZ_SAVE_PATH set, the
        // checkpoint write and the startup read must hit the same file —
        // otherwise everything saved under the override is lost on boot.
        using var env = GlobalTestEnv.Enter();
        var dirSettings = Path.Combine(env.TempPath, "f15-settings");
        var dirEnv = Path.Combine(env.TempPath, "f15-env");
        Directory.CreateDirectory(dirSettings);
        Directory.CreateDirectory(dirEnv);
        var settings = new AtherizSettings { SavePath = dirSettings, SecretPath = dirSettings };
        var orig = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", dirEnv);
        try
        {
            var obj = GameObject.Create("f15-roundtrip", isItem: true);
            ObjectRegistry.AddObject(obj);
            ObjectRegistry.SaveObjects(AtherizDbContextFactory.ResolveSavePath(settings), force: true);
            Assert.True(File.Exists(Path.Combine(dirEnv, "database.sqlite3")));
            Assert.False(File.Exists(Path.Combine(dirSettings, "database.sqlite3")));

            ObjectRegistry.ClearAll();
            ObjectRegistry.LoadObjects(AtherizDbContextFactory.ResolveSavePath(settings));
            Assert.NotEmpty(ObjectRegistry.Get(obj.Id));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", orig);
        }
    }

    [Fact]
    public void EvalDepth30_Evaluates_While100Echoes()
    {
        // Finding 17 boundary: 30 nesting levels sit under both caps (32)
        // and must still evaluate; 100 must echo instead of burning
        // O(depth x len) CPU. Pins the cap is a bound, not a shutdown.
        using var _ = GlobalTestEnv.Enter();
        var pc = GameObject.Create("pindepth30pc", isPc: true, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(pc);
        var receiver = GameObject.Create("pindepth30recv");
        ObjectRegistry.AddObject(receiver);
        var mapping = new Dictionary<string, object?>(StringComparer.Ordinal) { ["you"] = pc };

        var shallow = new string('(', 30) + new string(')', 30);
        Assert.NotEqual(shallow, FuncParser.Parse("$eval(" + shallow + ")", pc, receiver, mapping));

        var deep = new string('(', 100) + new string(')', 100);
        Assert.Equal(deep, FuncParser.Parse("$eval(" + deep + ")", pc, receiver, mapping));
    }
}
