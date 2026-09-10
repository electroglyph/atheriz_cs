using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression tests for networking, concurrency, persistence, and plugins.
// Each test fails while the defect is present and passes once fixed; a few
// pin verified-correct behavior and pass as-is.
[Collection("Ported")]
public class NetworkRegressionTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        try { NodeHandler.SetCurrent(null); } catch { }
    }

    // an all-corrupt object table must not wipe the live world
    // on load (per-row skips already log; the empty swap is the caller bug —
    // exercised here through corrupt rows, not an empty table).
    [Fact]
    public void AllCorruptLoad_PreservesLiveWorld()
    {
        Reset();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_reg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var live = GameObject.Create("live");
            ObjectRegistry.AddObject(live);
            using (var db = new AtherizDbContext(dir))
            {
                db.Database.EnsureCreated();
                db.Objects.Add(new ObjectRow { Id = 5001, Data = "{corrupt-json" });
                db.Objects.Add(new ObjectRow { Id = 5002, Data = "[1,2" });
                db.SaveChanges();
            }
            using (var db = new AtherizDbContext(dir))
            {
                ObjectRegistry.LoadObjects(db);
            }
            Assert.NotEmpty(ObjectRegistry.Get(live.Id));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } Reset(); }
    }

    // destination-only transitions tables (pre-fan-in schema) must migrate,
    // recovering each row's source from its Data JSON.
    [Fact]
    public void OldSchemaTransitionsTable_MigratesWithSource()
    {
        Reset();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_reg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                new Transition(new Coord("regmig", 0, 0, 0), new Coord("regmig", 5, 5, 0), "east"),
                Atheriz.Core.Persistence.JsonOptions.Default);
            using (var setup = new AtherizDbContext(dir))
            {
                var conn = setup.Database.GetDbConnection();
                conn.Open();
                try
                {
                    using (var mk = conn.CreateCommand())
                    {
                        mk.CommandText = "CREATE TABLE \"transitions\" (\"ToArea\" TEXT NOT NULL, \"ToX\" INTEGER NOT NULL, \"ToY\" INTEGER NOT NULL, \"ToZ\" INTEGER NOT NULL, \"Data\" TEXT, PRIMARY KEY (\"ToArea\",\"ToX\",\"ToY\",\"ToZ\"))";
                        mk.ExecuteNonQuery();
                    }
                    using (var ins = conn.CreateCommand())
                    {
                        ins.CommandText = "INSERT INTO \"transitions\" VALUES ('regmig',5,5,0,@d)";
                        var p = ins.CreateParameter();
                        p.ParameterName = "@d";
                        p.Value = json;
                        ins.Parameters.Add(p);
                        ins.ExecuteNonQuery();
                    }
                }
                finally { conn.Close(); }
            }
            AtherizDbContextFactory.DoSetup(dir);
            using (var db = new AtherizDbContext(dir))
            {
                var rows = db.Transitions.ToList();
                var row = Assert.Single(rows);
                Assert.Equal("regmig", row.FromArea);
                Assert.Equal(0, row.FromX);
                Assert.Equal("regmig", row.ToArea);
                Assert.Equal(5, row.ToX);
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } Reset(); }
    }

    // the pool-full log must report the count captured under the lock,
    // not a racy post-release read.
    [Fact]
    public void PoolFullLog_UsesLockedCount()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "BaseConnection.cs");
        var region = SourceScan.Region(src, "lock (Lock) // port of connection.py:101-106");
        Assert.Contains("pendingCount = _inputQueue.Count", region);
    }

    // normal pool shutdown logs at info (asyncthreadpool.py:307), not error.
    // (Threads index-alignment dummy stays: pinned by ThreadPoolSizingTests;
    // QueueCount stays O(n): no production callers.)
    [Fact]
    public void PoolStop_LogsInfo()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncThreadPool.cs");
        var region = SourceScan.Region(src, "public void Stop(");
        Assert.Contains("LogInformation(\"at AsyncThreadPool.stop()", region);
    }

    // plugin staleness must see subdirectory edits, not just top-level files
    // (csproj discovery itself stays top-level).
    [Fact]
    public void PluginStaleness_Recurses()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        Assert.Contains("GetFiles(dir, \"*.cs\", SearchOption.AllDirectories)", src);
    }

    // ticker shutdown must not nest locks, must dispose its CTS, and must
    // log at info/error (asyncthreadpool.py:599-607), not Console.Error.
    [Fact]
    public void TickerShutdown_IsClean()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncTicker.cs");
        var clear = SourceScan.Region(src, "public void Clear()");
        Assert.True(clear.IndexOf("Stop();", StringComparison.Ordinal) < clear.IndexOf("lock (_lock)", StringComparison.Ordinal));
        var stopInternal = SourceScan.Region(src, "private void StopInternal()");
        Assert.Contains("Dispose()", stopInternal);
        var stop = SourceScan.Region(src, "public void Stop()");
        Assert.Contains("LogInformation(\"at AsyncTicker.stop()", stop);
    }

    // pending-byte accounting lives solely in PendingLimiter: no mirrors,
    // no reflection-shim fields on either protocol (live _closing stays).
    [Fact]
    public void PendingAccounting_HasNoShimFields()
    {
        var telnet = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.DoesNotContain("_pendingBytes;", telnet);
        Assert.DoesNotContain("_pendingLock", telnet);
        var ws = SourceScan.Read("src", "Atheriz.Core", "Network", "WebSocketProtocol.cs");
        Assert.DoesNotContain("_pendingTasks", ws);
        Assert.DoesNotContain("_pendingBytesByTask", ws);
        Assert.DoesNotContain("_pendingCount", ws);
        Assert.DoesNotContain("_pendingBytes;", ws);
        Assert.DoesNotContain("_pendingLock", ws);
    }

    // handler discovery must be an explicit static registry,
    // not per-construction reflection (per-message caching already holds).
    [Fact]
    public void HandlerDiscovery_UsesNoPerConstructionReflection()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = SourceScan.Region(src, "public Dictionary<string, Delegate> GetHandlers()");
        Assert.DoesNotContain("GetMethods", region);
    }

    // workers must not block unboundedly; the gate take must be async.
    [Fact]
    public void PoolWaits_AreBounded()
    {
        var pool = SourceScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncThreadPool.cs");
        var run = SourceScan.Region(pool, "private static void RunInternal(");
        Assert.Contains("task.Wait(TimeSpan", run);
        var gate = SourceScan.Read("src", "Atheriz.Core", "Persistence", "DbWriteGate.cs");
        Assert.Contains("WaitAsync", gate);
    }

    // a forked AsyncLocal copy must not be mistaken for the gate owner.
    [Fact]
    public void ForkedFlow_DoesNotInheritGate()
    {
        DbWriteGate.Enter();
        try
        {
            bool forkEntered = Task.Run(() => DbWriteGate.TryEnter(TimeSpan.FromMilliseconds(200))).GetAwaiter().GetResult();
            Assert.False(forkEntered);
        }
        finally { DbWriteGate.Exit(); }
    }

    // busy-retries must run on a clean tracker, not a dirty context.
    [Fact]
    public void BusyRetry_ClearsTracker()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "DbTransactionHelper.cs");
        Assert.Contains("ChangeTracker.Clear()", src);
    }

    // fan-in transitions must survive (PK must include the source).
    [Fact]
    public void FanInTransitions_SurviveRoundTrip()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            var dest = new Coord("regfan", 5, 5, 0);
            nh.AddTransition(new Transition(new Coord("regfan", 0, 0, 0), dest, "east"));
            nh.AddTransition(new Transition(new Coord("regfan", 1, 1, 0), dest, "west"));
            nh.Save();
            using var db = new AtherizDbContext(env.TempPath);
            Assert.Equal(2, db.Transitions.Count());
        }
        finally { Reset(); }
    }

    // teardown must not be silently dropped when the pool is
    // stopped/saturated (drop mechanism confirmed; AtDisconnect itself does
    // not checkpoint — session/puppet cleanup is what's lost). NB: teardown
    // CANCELS the pending prompt (TaskCanceledException on Wait), so assert
    // completion, not a successful result.
    [Fact]
    public void StoppedPool_StillTearsDownSession()
    {
        Reset();
        var prev = ConnectionManager.GlobalInstance;
        var mgr = new ConnectionManager();
        try
        {
            var conn = new TestConnection("dropme");
            Assert.True(mgr.RegisterConnection("dropme", conn));
            var promptTask = conn.Session.Prompt("name:");
            mgr.Atp.Stop(wait: false);
            mgr.Disconnect(conn);
            try { promptTask.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { }
            Assert.True(promptTask.IsCompleted);
        }
        finally { ConnectionManager.GlobalInstance = prev; Reset(); }
    }

    // admission checks must run before spawning handler work
    // (per-send offload is already post-TryReserve; only accept is pre-cap).
    [Fact]
    public void AcceptChecks_PrecedeHandlerSpawn()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        int accept = src.IndexOf("AcceptTcpClientAsync", StringComparison.Ordinal);
        int spawn = src.IndexOf("Task.Run(() => HandleTelnetClientAsync", accept, StringComparison.Ordinal);
        Assert.True(accept >= 0 && spawn > accept);
        Assert.Contains("IsIpBanned", src.Substring(accept, spawn - accept));
    }

    // Plugin discovery (GetTypes scan) and live-patch field copy
    // (GetUninitializedObject ctor-bypass, covered by PortedReloaderTests with
    // a throwing ctor) have no non-reflection mechanism. Member-method
    // lookups were NOT exempt: ResolveRelations/AtTick go through direct
    // virtual dispatch now (pinned below). SourceHygieneTests excludes both
    // plugin files from its no-reflection rule.
    [Fact]
    public void ReloadPath_UsesNoPrivateReflection()
    {
        var reloader = SourceScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        Assert.DoesNotContain("GetMethod(\"AtTick\"", reloader);
        Assert.DoesNotContain("GetMethod(\"ResolveRelations\"", reloader);
    }

    // reload must remove pre-patch tick delegates, not duplicate them.
    [Fact]
    public void Reload_DoesNotDuplicateTicks()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        var ticker = new AsyncTicker();
        try
        {
            var oldObj = GameObject.Create("tickold", isTickable: true);
            ObjectRegistry.AddObject(oldObj);
            ticker.AddCoro(oldObj.AtTick, 1.0);
            var newObj = GameObject.Create("ticknew", isTickable: true);
            newObj.Id = oldObj.Id;
            ObjectRegistry.AddObject(newObj); // same id: simulates a patched replacement
            Atheriz.Core.Plugins.PluginReloader.ReregisterTicks(ticker);
            int coros = ticker.Slots.Values.Sum(s => s.Coros.Count);
            Assert.Equal(1, coros);
        }
        finally { try { ticker.Stop(); } catch { } Reset(); }
    }

    // journal failures must fail loud, never read as clean.
    [Fact]
    public void JournalFailures_FailLoud()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "DbTransactionHelper.cs");
        Assert.DoesNotContain("catch { return false; }", src);
    }

    // Dispose must join in-flight work, not spin 250ms then abort.
    [Fact]
    public void Dispose_JoinsInflightWork()
    {
        foreach (var f in new[] { "TelnetProtocol.cs", "WebSocketProtocol.cs" })
        {
            var src = SourceScan.Read("src", "Atheriz.Core", "Network", f);
            Assert.DoesNotContain("SpinUntil(", src);
        }
    }

    // limiter accounting must be exact (no blind overwrite,
    // no clamped over-release); the WS close-continuation must ride the
    // owning try (reserve/Track already merged; the trailing ContinueWith is
    // still outside it).
    [Fact]
    public void LimiterAccounting_IsExact()
    {
        var lim = new PendingLimiter(1000);
        var t = Task.CompletedTask;
        Assert.True(lim.TryReserve(t, 100));
        lim.Track(t, 50); // second association for the same task
        lim.Release(t);
        Assert.Equal(0, lim.PendingBytes);
    }

    // Over-release clamps to zero (a duplicate completion must neither corrupt
    // debt nor crash send paths) and is surfaced via a warning, not an exception.
    [Fact]
    public void OverRelease_IsSurfaced()
    {
        var lim = new PendingLimiter(1000);
        using var cap = new CaptureAtherizLog();
        lim.ReleaseSync(100);
        Assert.Equal(0, lim.PendingBytes);
        Assert.Equal(0, lim.PendingCount);
        Assert.Contains("over-release", cap.Read());
    }

    // real-stream buffer check must be live or the dead path removed.
    [Fact]
    public void WriteBufferCheck_IsLive()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.DoesNotContain("GetWriteBufferSize() => null", src);
    }

    // no unconditional 100ms tax on every plaintext login.
    [Fact]
    public void NoUnconditionalAutodetectDelay()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.DoesNotContain("await Task.Delay(100)", src);
    }

    // orphan reaping must be timer-driven, not every-50th-registration.
    [Fact]
    public void OrphanSweep_IsTimerDriven()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        Assert.DoesNotContain("% 50", src);
    }

    // refusal Close must run after the manager lock releases.
    [Fact]
    public void RefusalClose_RunsOutsideManagerLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = SourceScan.Region(src, "public virtual bool RegisterConnection(");
        Assert.DoesNotContain("connection.Close()", region);
    }

    // legend get-or-create must be atomic (last-writer-wins loses entries).
    [Fact]
    public void LegendGetOrCreate_IsAtomic()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = SourceScan.Region(src, "mi = new MapInfo(result.Chain.Area);", @"\n        \}");
        Assert.Contains("GetOrAdd", region);
    }

    // per-message throttle must not scan every host's TTL.
    [Fact]
    public void ThrottleCheck_IsPerHost()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ThrottleWindow.cs");
        var region = SourceScan.Region(src, "public static bool ShouldLog(Dictionary<string, double> last, Lock syncLock, string host, double window, double now)");
        Assert.DoesNotContain("foreach (var kv in last)", region);
    }

    // size cap must precede the JSON parse, not live only at the edge.
    [Fact]
    public void SizeCap_PrecedesParse()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = SourceScan.Region(src, "public virtual void HandleCommand(");
        int cap = region.IndexOf("MaxMessageSize", StringComparison.Ordinal);
        int parse = region.IndexOf("JsonDocument.Parse", StringComparison.Ordinal);
        Assert.True(cap >= 0 && cap < parse);
    }

    // WAL setup must be single-statement-applied with shutdown checkpoint.
    [Fact]
    public void WalLifecycle_IsComplete()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContext.cs")
            + SourceScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContextFactory.cs");
        Assert.Contains("wal_checkpoint", src);
    }

    // patch must release the object lock before registry rewire.
    [Fact]
    public void PatchReleasesLock_BeforeRewire()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        int take = src.IndexOf("lk.EnterWriteLock()", StringComparison.Ordinal);
        int add = src.IndexOf("ObjectRegistry.AddObject(newObj);", StringComparison.Ordinal);
        Assert.True(take >= 0 && add > take);
        Assert.Contains("ExitWriteLock", src.Substring(take, add - take));
    }

    // collectible ALCs must be unloadable despite live replacements.
    [Fact]
    public void PluginAssemblies_UnloadCleanly()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Plugins", "PluginLoader.cs");
        Assert.True(src.Contains("WeakReference") || src.Contains("ConditionalWeakTable"));
    }

    // multi-kwarg command mapping must be specified, not First().
    [Fact]
    public void CommandMapping_IsSpecified()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "BaseConnection.cs");
        Assert.DoesNotContain("outgoingKwargs.First()", src);
    }

    // save JSON encoding must run after the object lock releases (via the
    // EncodeSaveJson helper, which also restores the flag on failure).
    [Fact]
    public void SaveJson_EncodesAfterLockRelease()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "Converters", "GameObjectDtoConverter.cs");
        var region = SourceScan.Region(src, "private static string BuildSaveJson(");
        Assert.True(region.IndexOf("ExitWriteLock", StringComparison.Ordinal) < region.IndexOf("EncodeSaveJson(obj, dto, had)", StringComparison.Ordinal));
        var enc = SourceScan.Region(src, "private static string EncodeSaveJson(");
        Assert.Contains("ToJson(dto)", enc);
    }

}
