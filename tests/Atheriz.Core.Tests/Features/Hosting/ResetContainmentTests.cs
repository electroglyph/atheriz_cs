using System.Reflection;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using CorePathGuards = Atheriz.Core.Utils.PathGuards;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// destructive wipes (reset / overwrite scaffolding) must be contained
// to game save dirs — never a filesystem root or a foreign directory.
[Collection("Ported")]
public class ResetContainmentTests
{
    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    [Fact]
    public void GuardWipePath_RejectsFilesystemRoot()
    {
        Assert.Throws<InvalidOperationException>(() => CorePathGuards.GuardWipePath("/"));
    }

    [Fact]
    public void GuardWipePath_RequiresMarkersForSaveLeaf()
    {
        // a bare `save` leaf is NOT sufficient — `new /tmp
        // --overwrite` must not wipe /tmp/save. Only initialized worlds
        // (markers) pass; there is no force override.
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_wipe_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Need not exist to probe: absent and marker-less leaves refuse.
            Assert.Throws<InvalidOperationException>(() => CorePathGuards.GuardWipePath(Path.Combine(dir, "save")));
            Directory.CreateDirectory(Path.Combine(dir, "save"));
            Assert.Throws<InvalidOperationException>(() => CorePathGuards.GuardWipePath(Path.Combine(dir, "save")));
            // Markers restore the pass.
            File.WriteAllText(Path.Combine(dir, "save", "database.sqlite3"), "x");
            CorePathGuards.GuardWipePath(Path.Combine(dir, "save"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void GuardWipePath_AllowsWorldMarkerDirs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_wipem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pidDir = Path.Combine(dir, "w1");
            Directory.CreateDirectory(pidDir);
            File.WriteAllText(Path.Combine(pidDir, "server.pid"), "1234");
            CorePathGuards.GuardWipePath(pidDir);
            var dbDir = Path.Combine(dir, "w2");
            Directory.CreateDirectory(dbDir);
            File.WriteAllText(Path.Combine(dbDir, "database.sqlite3"), "x");
            CorePathGuards.GuardWipePath(dbDir);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void GuardWipePath_RejectsForeignDir_RequiresMarkers()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_wipef_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var foreign = Path.Combine(root, "foreign");
        Directory.CreateDirectory(foreign);
        var origCwd = Directory.GetCurrentDirectory();
        try
        {
            // Outside any game folder: a markerless dir refuses.
            Directory.SetCurrentDirectory(root);
            Assert.Throws<InvalidOperationException>(() => CorePathGuards.GuardWipePath(foreign));
            // Inside a game folder: still refuses without markers — there
            // is no force override anymore.
            File.WriteAllText(Path.Combine(root, "settings.py"), "");
            File.WriteAllText(Path.Combine(root, "__init__.py"), "");
            Assert.Throws<InvalidOperationException>(() => CorePathGuards.GuardWipePath(foreign));
            // Markers restore the pass.
            File.WriteAllText(Path.Combine(foreign, "database.sqlite3"), "x");
            CorePathGuards.GuardWipePath(foreign);
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task ResetHandler_ForeignSavePath_RefusesWithoutDeleting()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_resetf_" + Guid.NewGuid().ToString("N"));
        var foreign = Path.Combine(root, "foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "keep.txt"), "precious");
        var secret = Path.Combine(root, "secret");
        Directory.CreateDirectory(secret);
        var origCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(root); // not a game folder: no markers
        var settings = new AtherizSettings { SavePath = foreign, SecretPath = secret, WebserverPort = 39397, TelnetEnabled = false };
        SetEffectiveSettings(settings);
        var sb = new StringWriter();
        var origOut = Console.Out;
        var origIn = Console.In;
        Console.SetOut(sb);
        Console.SetIn(new StringReader("y" + Environment.NewLine));
        try
        {
            await ResetHandler.HandleResetAsync(Array.Empty<string>());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetIn(origIn);
            SetEffectiveSettings(null);
            try { AtherizDbContext.ReopenDatabase(); } catch { }
            try { AtherizDbContextFactory.ReopenDatabase(); } catch { }
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
        }
        Assert.Contains("Refusing to wipe", sb.ToString());
        Assert.True(Directory.Exists(foreign));
        Assert.True(File.Exists(Path.Combine(foreign, "keep.txt")));
        try { Directory.Delete(root, true); } catch { }
    }

    private static void WithSuperuserEnv(Action body)
    {
        var oldU = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldP = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", "suadmin");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", "supersecret123");
        try { body(); }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldU);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldP);
        }
    }

    [Fact]
    public void CreateGameFolder_OverwriteConfinesWipeToSaveLeaf()
    {
        // Reconciled boundary (pre-existing BareNameNewOverwrite_CanConnect
        // pins stale.txt removal in a marker-less save/): `new --overwrite`
        // wipes inside the save leaf only — folder top-level foreign files
        // are never deleted.
        var root = Path.Combine(Path.GetTempPath(), "atheriz_newf_" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "foreigngame");
        Directory.CreateDirectory(Path.Combine(target, "save"));
        File.WriteAllText(Path.Combine(target, "top.txt"), "not-yours");
        File.WriteAllText(Path.Combine(target, "save", "foreign.txt"), "stale-save");
        try
        {
            bool ok = false;
            WithSuperuserEnv(() => { ok = GameTemplateGenerator.CreateGameFolder(target, "foreigngame", overwrite: true); });
            Assert.True(ok);
            Assert.True(File.Exists(Path.Combine(target, "top.txt")));
            Assert.False(File.Exists(Path.Combine(target, "save", "foreign.txt")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void CreateGameFolder_OverwriteWorldDir_WipesStaleSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_neww_" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "oldgame");
        Directory.CreateDirectory(Path.Combine(target, "save"));
        File.WriteAllText(Path.Combine(target, "save", "database.sqlite3"), "stale");
        File.WriteAllText(Path.Combine(target, "save", "stale.txt"), "stale");
        try
        {
            bool ok = false;
            WithSuperuserEnv(() => { ok = GameTemplateGenerator.CreateGameFolder(target, "oldgame", overwrite: true); });
            Assert.True(ok);
            Assert.False(File.Exists(Path.Combine(target, "save", "stale.txt")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
