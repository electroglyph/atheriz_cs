using System.Reflection;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// reset/restart build-first pins: a failed build aborts before anything is
// deleted, stopped, or started. The compiler is always a stub here; these
// tests prove the ordering, not the build itself.
[Collection("Ported")]
public class ResetRestartBuildTests
{
    private static void SetEffectiveSettings(AtherizSettings? settings)
    {
        var f = typeof(StopHandler).GetField("_effectiveCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);
        f!.SetValue(null, settings);
    }

    [Fact]
    public async Task Reset_BuildFailure_LeavesWorldUntouched()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), "atheriz_resetbuild_" + Guid.NewGuid().ToString("N"));
        var saveDir = Path.Combine(gameDir, "save");
        Directory.CreateDirectory(saveDir);
        var sentinel = Path.Combine(saveDir, "sentinel.txt");
        File.WriteAllText(sentinel, "world");
        var origIn = Console.In;
        Console.SetIn(new StringReader("y" + Environment.NewLine));
        SetEffectiveSettings(new AtherizSettings { SavePath = saveDir, SecretPath = Path.Combine(gameDir, "secret") });
        try
        {
            AtherizSettings? seen = null;
            var rc = await ResetHandler.ResetAsync(null, null, foreground: true,
                buildAll: (s, _) =>
                {
                    seen = s;
                    return Task.FromResult(new BuildCoordinator.BuildAllResult(false, false, 0, "build broke"));
                });
            Assert.Equal(1, rc);
            Assert.Equal(saveDir, seen?.SavePath);
            Assert.True(File.Exists(sentinel));
            Assert.False(File.Exists(Path.Combine(saveDir, "world.db")));
        }
        finally
        {
            Console.SetIn(origIn);
            SetEffectiveSettings(null);
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Reset_BuildOkButStillNotRebuildable_RefusesWithoutWiping()
    {
        // A game project with no discoverable assembly even after a
        // successful build must still refuse — never template over it.
        var gameDir = Path.Combine(Path.GetTempPath(), "atheriz_resetbuild_" + Guid.NewGuid().ToString("N"));
        var saveDir = Path.Combine(gameDir, "save");
        Directory.CreateDirectory(saveDir);
        File.WriteAllText(Path.Combine(gameDir, "mygame.csproj"), "<Project></Project>");
        var sentinel = Path.Combine(saveDir, "sentinel.txt");
        File.WriteAllText(sentinel, "world");
        var origIn = Console.In;
        Console.SetIn(new StringReader("y" + Environment.NewLine));
        SetEffectiveSettings(new AtherizSettings { SavePath = saveDir, SecretPath = Path.Combine(gameDir, "secret") });
        try
        {
            var rc = await ResetHandler.ResetAsync(null, null, foreground: true,
                buildAll: (_, _) => Task.FromResult(new BuildCoordinator.BuildAllResult(true, false, 0, "ok")));
            Assert.Equal(1, rc);
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            Console.SetIn(origIn);
            SetEffectiveSettings(null);
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Restart_BuildFailure_LeavesServerAlone()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), "atheriz_restartbuild_" + Guid.NewGuid().ToString("N"));
        var saveDir = Path.Combine(gameDir, "save");
        Directory.CreateDirectory(saveDir);
        SetEffectiveSettings(new AtherizSettings { SavePath = saveDir, SecretPath = Path.Combine(gameDir, "secret") });
        try
        {
            // Build-first means the failing stub returns before any stop or
            // start: no pid file may appear and nothing listens.
            var rc = await RestartHandler.RestartAsync(null, null, null, foreground: true,
                buildAll: (_, _) => Task.FromResult(new BuildCoordinator.BuildAllResult(false, false, 0, "build broke")));
            Assert.Equal(1, rc);
            Assert.False(File.Exists(Path.Combine(saveDir, "server.pid")));
        }
        finally
        {
            SetEffectiveSettings(null);
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }
}
