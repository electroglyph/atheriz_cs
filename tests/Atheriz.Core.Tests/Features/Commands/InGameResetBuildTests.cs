using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Commands;

// In-game reset build parity: the verb runs the same build-first pass as CLI
// reset (engine when stale, then game projects) and aborts before the
// checkpoint when the build fails. Stubs stand in for the compiler; the
// checkpoint runs against the fixture database.
[Collection("Ported")]
public class InGameResetBuildTests
{
    private static string StageEngineRoot(bool stale)
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_igreset_" + Guid.NewGuid().ToString("N"));
        var serverDir = Path.Combine(root, "src", "Atheriz.Server");
        Directory.CreateDirectory(serverDir);
        var csproj = Path.Combine(serverDir, "Atheriz.Server.csproj");
        File.WriteAllText(csproj, "<Project></Project>");
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(csproj, old);
        if (!stale)
        {
            var binDir = Path.Combine(serverDir, "bin", "Release", "net10.0");
            Directory.CreateDirectory(binDir);
            var dll = Path.Combine(binDir, "Atheriz.Server.dll");
            File.WriteAllBytes(dll, [1]);
            File.SetLastWriteTimeUtc(dll, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        return root;
    }

    [Fact]
    public async Task ResetCore_BuildFailure_AbortsBeforeCheckpoint()
    {
        using var env = GlobalTestEnv.Enter();
        var engineRoot = StageEngineRoot(stale: true);
        // A save path with nothing in it: a checkpoint would create files,
        // so an empty-or-missing directory afterwards proves none ran.
        var probeSave = Path.Combine(env.TempPath, "nosave");
        var settings = new AtherizSettings { SavePath = probeSave };
        var lines = new List<string>();
        try
        {
            await ResetCommand.RunResetCoreAsync(
                settings,
                engineBuild: (_, _) => Task.FromResult(false),
                gameBuild: null,
                say: lines.Add,
                cancellationToken: CancellationToken.None,
                engineRoot: engineRoot,
                gameProjects: []);
            Assert.Contains(lines, m => m.Contains("borted"));
            Assert.DoesNotContain(lines, m => m.Contains("Reset complete"));
            var leftovers = Directory.Exists(probeSave)
                ? Directory.GetFileSystemEntries(probeSave)
                : [];
            Assert.Empty(leftovers);
        }
        finally { try { Directory.Delete(engineRoot, true); } catch { } }
    }

    [Fact]
    public async Task ResetCore_FreshTree_CompletesWithoutBuilding()
    {
        using var env = GlobalTestEnv.Enter();
        var engineRoot = StageEngineRoot(stale: false);
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var lines = new List<string>();
        var engineCalls = 0;
        try
        {
            await ResetCommand.RunResetCoreAsync(
                settings,
                engineBuild: (_, _) => { engineCalls++; return Task.FromResult(true); },
                gameBuild: null,
                say: lines.Add,
                cancellationToken: CancellationToken.None,
                engineRoot: engineRoot,
                gameProjects: []);
            Assert.Equal(0, engineCalls);
            Assert.Contains(lines, m => m.Contains("Reset complete"));
            Assert.DoesNotContain(lines, m => m.Contains("borted"));
        }
        finally { try { Directory.Delete(engineRoot, true); } catch { } }
    }

    [Fact]
    public async Task ResetCore_EngineRebuilt_HintsRestart()
    {
        // A rebuilt engine cannot swap in-process: the outcome must say so.
        using var env = GlobalTestEnv.Enter();
        var engineRoot = StageEngineRoot(stale: true);
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var lines = new List<string>();
        try
        {
            await ResetCommand.RunResetCoreAsync(
                settings,
                engineBuild: (_, _) => Task.FromResult(true),
                gameBuild: null,
                say: lines.Add,
                cancellationToken: CancellationToken.None,
                engineRoot: engineRoot,
                gameProjects: []);
            Assert.Contains(lines, m => m.Contains("Reset complete"));
            Assert.Contains(lines, m => m.Contains("restart"));
        }
        finally { try { Directory.Delete(engineRoot, true); } catch { } }
    }
}
