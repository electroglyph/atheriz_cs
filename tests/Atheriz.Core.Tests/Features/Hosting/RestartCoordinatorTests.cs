using Atheriz.Core.Network;
using Atheriz.Core.Persistence;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Server.Hosting;
using Microsoft.Extensions.Hosting;

namespace Atheriz.Core.Tests.Features.Hosting;

// In-game restart orchestration pins: any failed leg aborts with the old
// server still running (no spawn, no stop), and overlapping restarts refuse
// instead of double-spawning. Seams stand in for builds, saves, and spawns.
[Collection("Ported")]
public class RestartCoordinatorTests
{
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public bool StopCalled;
        public void StopApplication() => StopCalled = true;
    }

    private static AtherizSettings TempSettings(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "atheriz_rcoord_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new AtherizSettings { SavePath = dir };
    }

    private static Task<BuildCoordinator.BuildAllResult> OkBuild() =>
        Task.FromResult(new BuildCoordinator.BuildAllResult(true, false, 0, "ok"));

    private static Task<BuildCoordinator.BuildAllResult> FailedBuild() =>
        Task.FromResult(new BuildCoordinator.BuildAllResult(false, false, 0, "build broke"));

    [Fact]
    public async Task Restart_BuildFailure_AbortsBeforeCheckpoint()
    {
        var settings = TempSettings(out var dir);
        var lifetime = new FakeLifetime();
        try
        {
            var checkpoints = 0;
            var spawns = 0;
            var outcome = await RestartCoordinator.RestartAsync(
                settings, lifetime,
                buildAll: (_, _) => FailedBuild(),
                checkpoint: _ => { checkpoints++; return []; },
                spawn: _ => { spawns++; return null; });
            Assert.False(outcome.Ok);
            Assert.Equal(0, checkpoints);
            Assert.Equal(0, spawns);
            Assert.False(lifetime.StopCalled);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Restart_CheckpointFailure_AbortsBeforeSpawn()
    {
        var settings = TempSettings(out var dir);
        var lifetime = new FakeLifetime();
        try
        {
            var spawns = 0;
            var outcome = await RestartCoordinator.RestartAsync(
                settings, lifetime,
                buildAll: (_, _) => OkBuild(),
                checkpoint: _ => [new CheckpointWriter.SectionFailure("db", "boom")],
                spawn: _ => { spawns++; return null; });
            Assert.False(outcome.Ok);
            Assert.Equal(0, spawns);
            Assert.False(lifetime.StopCalled);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Restart_SpawnThrows_AbortsWithoutStopping()
    {
        var settings = TempSettings(out var dir);
        var lifetime = new FakeLifetime();
        try
        {
            var outcome = await RestartCoordinator.RestartAsync(
                settings, lifetime,
                buildAll: (_, _) => OkBuild(),
                checkpoint: _ => [],
                spawn: _ => throw new InvalidOperationException("no process"));
            Assert.False(outcome.Ok);
            Assert.False(lifetime.StopCalled);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Restart_OverlappingCalls_SecondRefuses()
    {
        var settings = TempSettings(out var dir);
        var lifetime = new FakeLifetime();
        var gate = new TaskCompletionSource<bool>();
        try
        {
            var first = RestartCoordinator.RestartAsync(
                settings, lifetime,
                buildAll: async (_, _) => { await gate.Task; return (await OkBuild()); },
                checkpoint: _ => [],
                spawn: _ => null);
            var second = await RestartCoordinator.RestartAsync(
                settings, lifetime,
                buildAll: (_, _) => OkBuild(),
                checkpoint: _ => [],
                spawn: _ => null);
            Assert.False(second.Ok);
            Assert.Contains("already in progress", second.Message);
            gate.SetResult(true);
            // The first call proceeds to a null spawn and aborts loudly too.
            Assert.False((await first).Ok);
            Assert.False(lifetime.StopCalled);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void AnnounceRestart_SendsRestartingCommandAndTextNoticeToEveryConnection()
    {
        var previous = ConnectionManager.GlobalInstance;
        var manager = new ConnectionManager();
        ConnectionManager.GlobalInstance = manager;
        try
        {
            var first = new TestConnection("restart-a");
            var second = new TestConnection("restart-b");
            Assert.True(manager.RegisterConnection("restart-a", first));
            Assert.True(manager.RegisterConnection("restart-b", second));

            RestartCoordinator.AnnounceRestart();

            // The wire frame is what the webclient waits on; the text frame
            // is the readable notice for plain clients. Content is the
            // sender's business — presence of each frame is the contract.
            foreach (var conn in new[] { first, second })
            {
                Assert.Contains(conn.Sent, s => s.Cmd == "restarting");
                Assert.Contains(conn.Sent, s => s.Cmd == "text");
            }
        }
        finally { ConnectionManager.GlobalInstance = previous; }
    }

    [Fact]
    public void AnnounceRestart_WithoutManager_IsSilentNoOp()
    {
        var previous = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = null;
        try
        {
            Assert.Null(Record.Exception(() => RestartCoordinator.AnnounceRestart()));
        }
        finally { ConnectionManager.GlobalInstance = previous; }
    }
}
