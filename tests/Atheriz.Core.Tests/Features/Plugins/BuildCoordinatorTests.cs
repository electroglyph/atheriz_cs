using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Plugins;

// Build-first ordering pins: engine before game (a stale server unifies a
// fresh plugin down to zero replacements), and any failure aborts before
// later builds run. Stubs stand in for the compiler; no real builds run.
[Collection("Ported")]
public class BuildCoordinatorTests
{
    private static string StageEngineRoot(bool stale)
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_coord_" + Guid.NewGuid().ToString("N"));
        var serverDir = Path.Combine(root, "src", "Atheriz.Server");
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(serverDir, "Atheriz.Server.csproj"), "<Project></Project>");
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(serverDir, "Atheriz.Server.csproj"), old);
        if (!stale)
        {
            var binDir = Path.Combine(serverDir, "bin", "Release", "net10.0");
            Directory.CreateDirectory(binDir);
            File.WriteAllBytes(Path.Combine(binDir, "Atheriz.Server.dll"), [1]);
            File.SetLastWriteTimeUtc(Path.Combine(binDir, "Atheriz.Server.dll"),
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        return root;
    }

    private static string StageGameProject(bool stale)
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_coordgame_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, "mygame.csproj");
        File.WriteAllText(csproj, "<Project></Project>");
        var dllDir = Path.Combine(dir, "bin", "Release", "net10.0");
        Directory.CreateDirectory(dllDir);
        var dll = Path.Combine(dllDir, "mygame.dll");
        File.WriteAllBytes(dll, [1]);
        if (stale)
        {
            File.SetLastWriteTimeUtc(dll, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var src = Path.Combine(dir, "code.cs");
            File.WriteAllText(src, "// game");
            File.SetLastWriteTimeUtc(src, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(csproj, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        else
        {
            File.SetLastWriteTimeUtc(dll, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(csproj, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        return csproj;
    }

    [Fact]
    public async Task EnsureAllBuilt_FreshTree_BuildsNothing()
    {
        var engineRoot = StageEngineRoot(stale: false);
        var gameCsproj = StageGameProject(stale: false);
        var gameDir = Path.GetDirectoryName(gameCsproj)!;
        try
        {
            var engineCalls = 0;
            var gameCalls = 0;
            var result = await BuildCoordinator.EnsureAllBuiltAsync(
                new AtherizSettings(),
                engineRoot: engineRoot,
                gameProjects: [gameCsproj],
                engineBuild: (_, _) => { engineCalls++; return Task.FromResult(true); },
                gameBuild: (_, _) => { gameCalls++; return Task.FromResult(true); });
            Assert.True(result.Ok);
            Assert.False(result.EngineBuilt);
            Assert.Equal(0, result.GameBuilt);
            Assert.Equal(0, engineCalls);
            Assert.Equal(0, gameCalls);
        }
        finally
        {
            try { Directory.Delete(engineRoot, true); } catch { }
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }

    [Fact]
    public async Task EnsureAllBuilt_StaleTree_BuildsEngineBeforeGame()
    {
        var engineRoot = StageEngineRoot(stale: true);
        var gameCsproj = StageGameProject(stale: true);
        var gameDir = Path.GetDirectoryName(gameCsproj)!;
        try
        {
            var order = new List<string>();
            var result = await BuildCoordinator.EnsureAllBuiltAsync(
                new AtherizSettings(),
                engineRoot: engineRoot,
                gameProjects: [gameCsproj],
                engineBuild: (_, _) => { order.Add("engine"); return Task.FromResult(true); },
                gameBuild: (_, _) => { order.Add("game"); return Task.FromResult(true); });
            Assert.True(result.Ok);
            Assert.True(result.EngineBuilt);
            Assert.Equal(1, result.GameBuilt);
            Assert.Equal(["engine", "game"], order);
        }
        finally
        {
            try { Directory.Delete(engineRoot, true); } catch { }
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }

    [Fact]
    public async Task EnsureAllBuilt_EngineFailure_SkipsGameBuild()
    {
        var engineRoot = StageEngineRoot(stale: true);
        var gameCsproj = StageGameProject(stale: true);
        var gameDir = Path.GetDirectoryName(gameCsproj)!;
        try
        {
            var gameCalls = 0;
            var result = await BuildCoordinator.EnsureAllBuiltAsync(
                new AtherizSettings(),
                engineRoot: engineRoot,
                gameProjects: [gameCsproj],
                engineBuild: (_, _) => Task.FromResult(false),
                gameBuild: (_, _) => { gameCalls++; return Task.FromResult(true); });
            Assert.False(result.Ok);
            Assert.Equal(0, gameCalls);
        }
        finally
        {
            try { Directory.Delete(engineRoot, true); } catch { }
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }

    [Fact]
    public async Task EnsureAllBuilt_GameFailure_ReturnsNotOk()
    {
        var engineRoot = StageEngineRoot(stale: false);
        var gameCsproj = StageGameProject(stale: true);
        var gameDir = Path.GetDirectoryName(gameCsproj)!;
        try
        {
            var result = await BuildCoordinator.EnsureAllBuiltAsync(
                new AtherizSettings(),
                engineRoot: engineRoot,
                gameProjects: [gameCsproj],
                gameBuild: (_, _) => Task.FromResult(false));
            Assert.False(result.Ok);
            Assert.Equal(0, result.GameBuilt);
        }
        finally
        {
            try { Directory.Delete(engineRoot, true); } catch { }
            try { Directory.Delete(gameDir, true); } catch { }
        }
    }
}
