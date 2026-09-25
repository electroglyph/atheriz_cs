using Atheriz.Core.Network;
using Atheriz.Core.Tests;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: pid reclaim survival, wipe-lock exclusion,
// telnet dispose join, startup generation commit, kill re-verification,
// telnet-port wait, liveness re-verification, child-pid readiness,
// cwd-per-call snapshot. CLI/host paths stage no live servers here, so
// the in-process races pin behaviorally and the sequenced paths pin the
// exact guard structurally (sensitive to revert).
[Collection("Ported")]
public sealed class ThreadSafetyRegression10Tests
{
    private sealed class RecordWriter : ITelnetWriter, IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(false);
        public readonly ManualResetEventSlim Entered = new(false);
        private readonly List<string> _writes = new();
        private readonly object _lock = new();
        public bool Disposed { get; private set; }
        public IReadOnlyList<string> Writes { get { lock (_lock) return _writes.ToList(); } }
        public void Open() => _gate.Set();
        public void Write(string text)
        {
            Entered.Set();
            _gate.Wait(TimeSpan.FromSeconds(10));
            lock (_lock) _writes.Add(text);
        }
        public void Iac(byte cmd, byte opt) { }
        public void Close() { }
        public int? GetWriteBufferSize() => null;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "127.0.0.1";
        public void Dispose() { Disposed = true; }
    }

    // A successor claim racing a stale release always survives.
    [Fact]
    public async Task ReleaseIfOwner_ConcurrentReclaim_SuccessorSurvives()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_f7_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pidPath = Path.Combine(dir, "server.pid");
            int oldPid = 100001;
            int newPid = 100002;
            for (int round = 0; round < 200; round++)
            {
                try { File.Delete(pidPath + $".releasing-{Environment.ProcessId}"); } catch { }
                File.WriteAllText(pidPath, oldPid.ToString());
                using var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); PidFile.ReleaseIfOwner(pidPath, oldPid); });
                var t2 = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    try { File.Delete(pidPath); } catch { }
                    File.WriteAllText(pidPath, newPid.ToString());
                });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(File.Exists(pidPath), $"round {round}: successor file deleted");
                Assert.Equal(newPid.ToString(), File.ReadAllText(pidPath).Trim());
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // A held wipe lock refuses pid claims; release restores them.
    [Fact]
    public void WipeLock_HeldWhileClaiming_PidClaimRefused()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_wipe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var wipe = PidFile.TryAcquireWipeLock(dir))
            {
                Assert.NotNull(wipe);
                Assert.True(PidFile.IsWipeLocked(dir));
                bool ok = PidFile.TryAcquire(dir, out var stolen, out var reason, 9999);
                Assert.False(ok);
                Assert.Contains("wipe", reason ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.Null(stolen);
            }
            // Clean release deletes the lock so the next start is not refused.
            PidFile.ReleaseWipeLock(null, dir);
            Assert.False(PidFile.IsWipeLocked(dir));
            // A stale lock (crashed wiper) does not block starters forever.
            File.WriteAllText(PidFile.WipeLockPath(dir), "stale");
            File.SetLastWriteTimeUtc(PidFile.WipeLockPath(dir), DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
            Assert.False(PidFile.IsWipeLocked(dir));
            bool ok2 = PidFile.TryAcquire(dir, out var mine, out _);
            try { Assert.True(ok2); }
            finally { try { mine?.Dispose(); } catch { } }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // Writes past teardown are refused; the join observes the drain.
    [Fact]
    public async Task TelnetConnection_DisposeJoinsBeforeWriterTeardown()
    {
        using var env = GlobalTestEnv.Enter();
        var writer = new RecordWriter();
        var conn = await Task.Run(() => new TelnetConnection(new object(), writer, "f19join"));
        conn.SendCommand("text", new List<object?> { "gated" }, new Dictionary<string, object?>());
        Assert.True(writer.Entered.Wait(TimeSpan.FromSeconds(5)), "offloaded write never started");
        var disposeTask = Task.Run(() => conn.Dispose());
        await Task.Delay(100);
        Assert.False(disposeTask.IsCompleted, "Dispose completed while a write was still in flight");
        writer.Open();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(writer.Disposed, "Dispose did not release the writer");
        Assert.Contains("gated", string.Join("\n", writer.Writes));
        conn.SendCommand("text", new List<object?> { "late" }, new Dictionary<string, object?>());
        await Task.Delay(300);
        Assert.DoesNotContain("late", writer.Writes);
    }

    [Fact]
    public void ServerLifecycle_StartupCommitsByGeneration()
    {
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "ServerLifecycle.cs");
        Assert.Contains("_startupGen", src);
        Assert.Contains("gen == Volatile.Read(ref _startupGen) && !_shutdownCompleted", src);
    }

    [Fact]
    public void StopHandler_KillReverifiesBeforeSignal()
    {
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Contains("Terminal re-verify", src);
        Assert.Contains("PidFile.TryReadPid(pidFilePath) == pid && PidFile.IsServerProcess(pid)", src);
    }

    [Fact]
    public void RestartHandler_WaitsForTelnetPort()
    {
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Cli", "RestartHandler.cs");
        Assert.Contains("TelnetEnabled", src);
        Assert.Contains("telnet port", src);
    }

    [Fact]
    public void CreateHandler_ReverifiesLivenessAfterLoad()
    {
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Cli", "CreateHandler.cs");
        Assert.Contains("Re-verify the liveness backstop", src);
        Assert.Contains("IsLiveClaim(pidFile2", src);
    }

    [Fact]
    public void DaemonSpawner_ReadinessNamesChildPid()
    {
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        Assert.Contains("c == childPid", src);
    }

    [Fact]
    public void AssetResolver_SnapshotsCwdPerCall()
    {
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        Assert.Contains("cwd = Directory.GetCurrentDirectory()", src);
    }
}
