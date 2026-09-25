using System.Reflection;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: settings-cache
// stability, single-owner shutdown hooks, racing-shutdown readiness,
// foreign-pid refusal, token lookup, overwrite liveness checks,
// shared-DTO subtype loads, concurrent setups, write shedding.
// CLI/host paths stage no live servers here; behavior pins drive real
// code, sequenced paths pin the guard structurally.
// Reflection is used only where the member is internal/private (allowed).
[Collection("Ported")]
public sealed class ThreadSafetyRegression13Tests
{
    private static object? GetStatic(Type t, string name)
    {
        var p = t.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (p is not null) return p.GetValue(null);
        var f = t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return f?.GetValue(null);
    }

    private static object? InvokeStatic(Type t, string name, params object?[] args)
    {
        var m = t.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"no method {name} on {t.Name}");
        return m.Invoke(null, args);
    }

    private static void SetInstance(object target, string name, object? value)
    {
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"no field {name}");
        f.SetValue(target, value);
    }

    // Concurrent settings-cache reads vs invalidates never throw, never
    // null; quiesced reads are reference-stable (one generation per load).
    [Fact]
    public async Task EffectiveSettings_ConcurrentInvalidate_NeverTorn()
    {
        var t = typeof(StopHandler);
        using var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int j = 0; j < 300; j++)
            {
                if (j % 3 == 0) InvokeStatic(t, "InvalidateEffectiveSettings");
                else Assert.NotNull(GetStatic(t, "EffectiveSettingsValue"));
            }
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
        InvokeStatic(t, "InvalidateEffectiveSettings");
        var a = GetStatic(t, "EffectiveSettingsValue");
        var b = GetStatic(t, "EffectiveSettingsValue");
        Assert.NotNull(a);
        Assert.Same(a, b);
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public void StopApplication() { }
    }

    // Re-registering shutdown replaces the previous owner's hooks instead
    // of stacking N x DoShutdown / pid release / foreign token deletes.
    [Fact]
    public void RegisterShutdown_SecondHost_ReplacesFirstHooks()
    {
        var t = typeof(Atheriz.Server.Hosting.ServerHost);
        var m = t.GetMethod("RegisterShutdown", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("no RegisterShutdown");
        var s1 = new AtherizSettings();
        var s2 = new AtherizSettings();
        m.Invoke(null, [new StubLifetime(), s1, null, false]);
        var reg1 = t.GetField("_stoppingReg", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        var exit1 = t.GetField("_exitHandler", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        Assert.NotNull(reg1);
        Assert.NotNull(exit1);
        m.Invoke(null, [new StubLifetime(), s2, null, false]);
        var reg2 = t.GetField("_stoppingReg", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        var exit2 = t.GetField("_exitHandler", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        Assert.NotNull(reg2);
        Assert.NotSame(reg1, reg2);
        Assert.NotSame(exit1, exit2);
    }

    // Shutdowns racing a startup (before, during, after) always leave
    // readiness false — never a false-ready on a shut-down world.
    [Fact]
    public async Task Lifecycle_ShutdownRacingStartup_NeverFalseReady()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 0 };
        ServerLifecycle.Reset();
        var startup = Task.Run(() =>
        {
            try { ServerLifecycle.DoStartup(settings); } catch { }
        });
        await Task.Delay(50);
        for (int i = 0; i < 20; i++)
        {
            try { ServerLifecycle.DoShutdown(settings); } catch { }
            await Task.Delay(20);
        }
        await startup.WaitAsync(TimeSpan.FromSeconds(120));
        Assert.False(ServerLifecycle.StartupSucceeded);
        try { ServerLifecycle.DoShutdown(settings); } catch { }
    }

    // The kill gate refuses a live process that is not a verified server
    // (here: the test runner itself) — returns 1, signals nothing.
    [Fact]
    public async Task KillVerifiedPid_RefusesForeignLiveProcess()
    {
        int self = Environment.ProcessId;
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_f8_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pidFile = Path.Combine(dir, "server.pid");
            var task = (Task<int>)InvokeStatic(typeof(StopHandler), "KillVerifiedPidAsync", self, 9, pidFile)!;
            int rc = await task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, rc);
            Assert.True(Environment.ProcessId == self);
            try { _ = System.Diagnostics.Process.GetProcessById(self); } catch { Assert.Fail("self was killed"); }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // The upward token walk observes one CWD generation per call.
    [Fact]
    public void FindTokenFile_UpwardWalk_FindsOwningGameToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_f13_" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "gameA", "sub", "dir");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(Path.Combine(root, "gameA", "secret"));
        var token = Path.Combine(root, "gameA", "secret", "admin.token");
        File.WriteAllText(token, "tok");
        string prev = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(sub);
            var found = (string?)InvokeStatic(typeof(ShutdownClient), "FindTokenFile", "no-such-secret-dir", 0);
            Assert.Equal(token, found);
        }
        finally { try { Directory.SetCurrentDirectory(prev); } catch { } try { Directory.Delete(root, true); } catch { } }
    }

    // Overwrite on a non-live folder succeeds; the liveness check runs
    // after the hold AND immediately before the deletes (no check->wipe gap).
    [Fact]
    public void NewOverwrite_NonLiveFolder_SucceedsWithDoubleCheck()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_f14_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(GameTemplateGenerator.CreateGameFolder(dir, "f14game", overwrite: false));
            Assert.True(GameTemplateGenerator.CreateGameFolder(dir, "f14game", overwrite: true));
            var src = Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
            Assert.True(Features.Regression.SourceScan.Count(src, "IsLiveServerFolder(folderPath)") >= 2);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private sealed class SharedSubtypeProbe : GameObject { }

    // Concurrent loads of one dto instance both carry the subtype and
    // the input keeps its markers (no swap-then-restore on shared input).
    [Fact]
    public async Task FromDto_SharedDtoConcurrentLoads_BothKeepSubtype()
    {
        using var env = GlobalTestEnv.Enter();
        GameObjectDtoConverter.RegisterSubtype("SharedSubtypeProbe", typeof(SharedSubtypeProbe), () => new SharedSubtypeProbe());
        var dto = new GameObjectDto
        {
            Id = 424242,
            Type = "object",
            Name = "f16",
            Extra = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["__object_type"] = Atheriz.Core.Persistence.JsonOptions.ToElement("SharedSubtypeProbe"),
            },
        };
        using var barrier = new Barrier(2);
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            GameObject? last = null;
            for (int i = 0; i < 2000; i++)
                last = GameObjectDtoConverter.FromDto(dto);
            return last;
        })).ToArray();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.All(results, r => Assert.IsType<SharedSubtypeProbe>(r));
        Assert.True(dto.Extra.ContainsKey("__object_type"));
    }

    // Concurrent setups on one save dir (legacy table planted per round)
    // never throw — the save-dir lock serializes the migration.
    [Fact]
    public async Task DoSetup_ConcurrentSameDir_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        for (int round = 0; round < 10; round++)
        {
            var dir = Path.Combine(Path.GetTempPath(), "atheriz_f18c_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var dbPath = Path.Combine(dir, "database.sqlite3");
                using (var sqlite = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
                {
                    sqlite.Open();
                    using var cmd = sqlite.CreateCommand();
                    cmd.CommandText = "CREATE TABLE \"transitions\" (\"ToArea\" TEXT NOT NULL, \"ToX\" INTEGER NOT NULL, \"ToY\" INTEGER NOT NULL, \"ToZ\" INTEGER NOT NULL, \"Data\" TEXT)";
                    cmd.ExecuteNonQuery();
                }
                using var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); AtherizDbContextFactory.DoSetup(dir); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); AtherizDbContextFactory.DoSetup(dir); });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(60));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }

    private sealed class ListWriter : ITelnetWriter, IDisposable
    {
        private readonly List<string> _writes = new();
        private readonly object _lock = new();
        public readonly ManualResetEventSlim Entered = new(false);
        public IReadOnlyList<string> Writes { get { lock (_lock) return _writes.ToList(); } }
        public void Write(string text) { Entered.Set(); lock (_lock) _writes.Add(text); }
        public void Iac(byte cmd, byte opt) { }
        public void Close() { }
        public int? GetWriteBufferSize() => null;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "127.0.0.1";
        public void Dispose() { }
    }

    // Past the parked-task bound, new writes shed (no new parked task,
    // limiter released) instead of parking unbounded threads on a wedged peer.
    [Fact]
    public async Task ScheduleWrite_ParkedPastBound_ShedsInsteadOfParking()
    {
        using var env = GlobalTestEnv.Enter();
        var writer = new ListWriter();
        var conn = await Task.Run(() => new TelnetConnection(new object(), writer, "f20shed"));
        int bound = (int)typeof(TelnetConnection).GetField("MaxScheduledWrites", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        SetInstance(conn, "_inflight", bound);
        conn.SendCommand("text", new List<object?> { "shed-me" }, new Dictionary<string, object?>());
        Assert.False(writer.Entered.Wait(TimeSpan.FromMilliseconds(300)));
        Assert.Equal(0, conn.PendingBytes);
        // Control: below the bound the write goes through.
        SetInstance(conn, "_inflight", 0);
        conn.SendCommand("text", new List<object?> { "through" }, new Dictionary<string, object?>());
        Assert.True(writer.Entered.Wait(TimeSpan.FromSeconds(5)));
    }

    // create_account runs off the Kestrel worker with a bounded wait.
    [Fact]
    public void CreateAccountRoute_BoundedOffload()
    {
        var src = Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        var region = Features.Regression.SourceScan.Region(src, "MapPost(\"/_internal/create_account\"");
        Assert.Contains("Task.Run", region);
        Assert.Contains("WaitAsync(TimeSpan.FromSeconds(60))", region);
        Assert.Contains("AtCharCreate", region);
    }

    // reset parks savers across the close/wipe/reopen sandwich.
    [Fact]
    public void ResetHoldsWriteGate_AcrossCloseReopen()
    {
        var src = Features.Regression.SourceScan.Read("src", "Atheriz.Server", "Cli", "ResetHandler.cs");
        int enter = src.IndexOf("DbWriteGate.EnterAsync", StringComparison.Ordinal);
        int close = src.IndexOf("CloseDatabase()", StringComparison.Ordinal);
        int reopen = src.IndexOf("ReopenDatabase()", StringComparison.Ordinal);
        Assert.True(enter >= 0 && close > enter && reopen > close);
    }
}
