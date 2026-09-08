using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;

namespace Atheriz.Core.Tests.Features.Audit;

// Fourth-pass regression tests for audit2.md §4 (Network/Concurrency/
// Persistence/Plugins). Each test FAILS while the finding is present and
// PASSES once fixed, except STRUCK findings (N-P1-2, N-P1-8) which need live
// sockets/threads to pin and are documented in audit2.md §7. No prod code touched.
[Collection("Ported")]
public class AuditNetworkTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        try { NodeHandler.SetCurrent(null); } catch { }
    }

    // N-P0-1 PARTIAL: an all-corrupt object table must not wipe the live world
    // on load (per-row skips already log; the empty swap is the caller bug,
    // shared with G-P0-1 — exercised here through corrupt rows, not an
    // empty table).
    [Fact]
    public void N_P0_1_AllCorruptLoad_PreservesLiveWorld()
    {
        Reset();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_audit_" + Guid.NewGuid().ToString("N"));
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

    // N-P1-1 PARTIAL: handler discovery must be an explicit static registry,
    // not per-construction reflection (per-message caching already holds).
    [Fact]
    public void N_P1_1_HandlerDiscovery_UsesNoPerConstructionReflection()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = AuditScan.Region(src, "public Dictionary<string, Delegate> GetHandlers()");
        Assert.DoesNotContain("GetMethods", region);
    }

    // N-P1-3: workers must not block unboundedly; the gate take must be async.
    [Fact]
    public void N_P1_3_PoolWaits_AreBounded()
    {
        var pool = AuditScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncThreadPool.cs");
        var run = AuditScan.Region(pool, "private static void RunInternal(");
        Assert.Contains("task.Wait(TimeSpan", run);
        var gate = AuditScan.Read("src", "Atheriz.Core", "Persistence", "DbWriteGate.cs");
        Assert.Contains("WaitAsync", gate);
    }

    // N-P1-4: a forked AsyncLocal copy must not be mistaken for the gate owner.
    [Fact]
    public void N_P1_4_ForkedFlow_DoesNotInheritGate()
    {
        DbWriteGate.Enter();
        try
        {
            bool forkEntered = Task.Run(() => DbWriteGate.TryEnter(TimeSpan.FromMilliseconds(200))).GetAwaiter().GetResult();
            Assert.False(forkEntered);
        }
        finally { DbWriteGate.Exit(); }
    }

    // N-P1-5: busy-retries must run on a clean tracker, not a dirty context.
    [Fact]
    public void N_P1_5_BusyRetry_ClearsTracker()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Persistence", "DbTransactionHelper.cs");
        Assert.Contains("ChangeTracker.Clear()", src);
    }

    // N-P1-6: fan-in transitions must survive (PK must include the source).
    [Fact]
    public void N_P1_6_FanInTransitions_SurviveRoundTrip()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            var dest = new Coord("auditfan", 5, 5, 0);
            nh.AddTransition(new Transition(new Coord("auditfan", 0, 0, 0), dest, "east"));
            nh.AddTransition(new Transition(new Coord("auditfan", 1, 1, 0), dest, "west"));
            nh.Save();
            using var db = new AtherizDbContext(env.TempPath);
            Assert.Equal(2, db.Transitions.Count());
        }
        finally { Reset(); }
    }

    // N-P1-7 PARTIAL: teardown must not be silently dropped when the pool is
    // stopped/saturated (drop mechanism confirmed; AtDisconnect itself does
    // not checkpoint — session/puppet cleanup is what's lost).
    [Fact]
    public void N_P1_7_StoppedPool_StillTearsDownSession()
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
            Assert.True(promptTask.Wait(TimeSpan.FromSeconds(3)));
        }
        finally { ConnectionManager.GlobalInstance = prev; Reset(); }
    }

    // N-P1-9 PARTIAL: admission checks must run before spawning handler work
    // (per-send offload is already post-TryReserve; only accept is pre-cap).
    [Fact]
    public void N_P1_9_AcceptChecks_PrecedeHandlerSpawn()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        int accept = src.IndexOf("AcceptTcpClientAsync", StringComparison.Ordinal);
        int spawn = src.IndexOf("Task.Run(() => HandleTelnetClientAsync", accept, StringComparison.Ordinal);
        Assert.True(accept >= 0 && spawn > accept);
        Assert.Contains("IsIpBanned", src.Substring(accept, spawn - accept));
    }

    // N-P1-10: production hot-reload must not reflect over private members.
    // NOTE: plugin discovery may deserve a documented exemption (see §6
    // REFLECTION-IN-PROD); this test pins the ban-literal resolution.
    [Fact]
    public void N_P1_10_ReloadPath_UsesNoPrivateReflection()
    {
        var loader = AuditScan.Read("src", "Atheriz.Core", "Plugins", "PluginLoader.cs");
        Assert.DoesNotContain("GetTypes()", loader);
        var reloader = AuditScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        Assert.DoesNotContain("GetUninitializedObject", reloader);
        Assert.DoesNotContain("GetMethod(\"AtTick\"", reloader);
    }

    // N-P1-11: reload must remove pre-patch tick delegates, not duplicate them.
    [Fact]
    public void N_P1_11_Reload_DoesNotDuplicateTicks()
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

    // N-P1-12: journal failures must fail loud, never read as clean.
    [Fact]
    public void N_P1_12_JournalFailures_FailLoud()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Persistence", "DbTransactionHelper.cs");
        Assert.DoesNotContain("catch { return false; }", src);
    }

    // N-P2-1: Dispose must join in-flight work, not spin 250ms then abort.
    [Fact]
    public void N_P2_1_Dispose_JoinsInflightWork()
    {
        foreach (var f in new[] { "TelnetProtocol.cs", "WebSocketProtocol.cs" })
        {
            var src = AuditScan.Read("src", "Atheriz.Core", "Network", f);
            Assert.DoesNotContain("SpinUntil(", src);
        }
    }

    // N-P2-2 PARTIAL: limiter accounting must be exact (no blind overwrite,
    // no clamped over-release); the WS close-continuation must ride the
    // owning try (reserve/Track already merged; the trailing ContinueWith is
    // still outside it).
    [Fact]
    public void N_P2_2_LimiterAccounting_IsExact()
    {
        var lim = new PendingLimiter(1000);
        var t = Task.CompletedTask;
        Assert.True(lim.TryReserve(t, 100));
        lim.Track(t, 50); // second association for the same task
        lim.Release(t);
        Assert.Equal(0, lim.PendingBytes);
    }

    [Fact]
    public void N_P2_2b_OverRelease_IsSurfaced()
    {
        var lim = new PendingLimiter(1000);
        Assert.Throws<InvalidOperationException>(() => lim.ReleaseSync(100));
    }

    [Fact]
    public void N_P2_2c_CloseContinuation_RidesOwningTry()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "WebSocketProtocol.cs");
        int track = src.IndexOf("_limiter.Track(task, nb);", StringComparison.Ordinal);
        int handler = src.IndexOf("catch (Exception e) // port of websocket.py:99-103", StringComparison.Ordinal);
        Assert.True(track >= 0 && handler > track);
        Assert.Contains("ContinueWith", src.Substring(track, handler - track));
    }

    // N-P2-3: real-stream buffer check must be live or the dead path removed.
    [Fact]
    public void N_P2_3_WriteBufferCheck_IsLive()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.DoesNotContain("GetWriteBufferSize() => null", src);
    }

    // N-P2-4: no unconditional 100ms tax on every plaintext login.
    [Fact]
    public void N_P2_4_NoUnconditionalAutodetectDelay()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.DoesNotContain("await Task.Delay(100)", src);
    }

    // N-P2-5: orphan reaping must be timer-driven, not every-50th-registration.
    [Fact]
    public void N_P2_5_OrphanSweep_IsTimerDriven()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        Assert.DoesNotContain("% 50", src);
    }

    // N-P2-6: refusal Close must run after the manager lock releases.
    [Fact]
    public void N_P2_6_RefusalClose_RunsOutsideManagerLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = AuditScan.Region(src, "public virtual bool RegisterConnection(");
        Assert.DoesNotContain("connection.Close()", region);
    }

    // N-P2-7: legend get-or-create must be atomic (last-writer-wins loses entries).
    [Fact]
    public void N_P2_7_LegendGetOrCreate_IsAtomic()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = AuditScan.Region(src, "mi = new MapInfo(result.Chain.Area);", @"\n        \}");
        Assert.Contains("GetOrAdd", region);
    }

    // N-P2-8 PARTIAL: per-message throttle must not scan every host's TTL.
    [Fact]
    public void N_P2_8_ThrottleCheck_IsPerHost()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "ThrottleWindow.cs");
        var region = AuditScan.Region(src, "public static bool ShouldLog(Dictionary<string, double> last, object syncLock, string host, double window, double now)");
        Assert.DoesNotContain("foreach (var kv in last)", region);
    }

    // N-P2-9: size cap must precede the JSON parse, not live only at the edge.
    [Fact]
    public void N_P2_9_SizeCap_PrecedesParse()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = AuditScan.Region(src, "public virtual void HandleCommand(");
        int cap = region.IndexOf("MaxMessageSize", StringComparison.Ordinal);
        int parse = region.IndexOf("JsonDocument.Parse", StringComparison.Ordinal);
        Assert.True(cap >= 0 && cap < parse);
    }

    // N-P2-10: WAL setup must be single-statement-applied with shutdown checkpoint.
    [Fact]
    public void N_P2_10_WalLifecycle_IsComplete()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContext.cs")
            + AuditScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContextFactory.cs");
        Assert.Contains("wal_checkpoint", src);
    }

    // N-P2-11: patch must release the object lock before registry rewire.
    [Fact]
    public void N_P2_11_PatchReleasesLock_BeforeRewire()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        int take = src.IndexOf("lk.EnterWriteLock()", StringComparison.Ordinal);
        int add = src.IndexOf("ObjectRegistry.AddObject(newObj);", StringComparison.Ordinal);
        Assert.True(take >= 0 && add > take);
        Assert.Contains("ExitWriteLock", src.Substring(take, add - take));
    }

    // N-P2-12: collectible ALCs must be unloadable despite live replacements.
    [Fact]
    public void N_P2_12_PluginAssemblies_UnloadCleanly()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Plugins", "PluginLoader.cs");
        Assert.True(src.Contains("WeakReference") || src.Contains("ConditionalWeakTable"));
    }

    // N-P2-13 PARTIAL: multi-kwarg command mapping must be specified, not First().
    [Fact]
    public void N_P2_13_CommandMapping_IsSpecified()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "BaseConnection.cs");
        Assert.DoesNotContain("outgoingKwargs.First()", src);
    }

    // N-P2-14: save JSON encoding must run after the object lock releases.
    [Fact]
    public void N_P2_14_SaveJson_EncodesAfterLockRelease()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Persistence", "Converters", "GameObjectDtoConverter.cs");
        var region = AuditScan.Region(src, "private static string BuildSaveJson(");
        Assert.True(region.IndexOf("ExitWriteLock", StringComparison.Ordinal) < region.IndexOf("ToJson(dto)", StringComparison.Ordinal));
    }

    // N-P3-1: busy-log staleness is benign but must capture the count in-lock.
    [Fact]
    public void N_P3_1_BusyLog_CapturesCountInLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Network", "BaseConnection.cs");
        Assert.DoesNotContain("{_inputQueue.Count} message(s) pending retry", src);
    }

    // N-P3-2: reflection shims must go; tests assert the limiter authority.
    [Fact]
    public void N_P3_2_ReflectionShims_Removed()
    {
        foreach (var f in new[] { "TelnetProtocol.cs", "WebSocketProtocol.cs" })
        {
            var src = AuditScan.Read("src", "Atheriz.Core", "Network", f);
            Assert.DoesNotContain("_pendingBytes", src);
        }
    }

    // N-P3-3: no per-call dummy threads, no O(n) counts, no error-level routine stops.
    [Fact]
    public void N_P3_3_PoolIntrospection_IsCheapAndQuiet()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncThreadPool.cs");
        Assert.DoesNotContain("new Thread(() => {})", src);
        Assert.DoesNotContain("_queue.Count(i =>", src);
        var stop = AuditScan.Region(src, "public void Stop(");
        Assert.DoesNotContain("LogError", stop);
    }

    // N-P3-4 PARTIAL: CTS disposal + logger (catch-up burst + re-entrant Clear
    // noted in audit2.md; covered here by disposal/logging asserts).
    [Fact]
    public void N_P3_4_TickerLifecycle_IsClean()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncTicker.cs");
        Assert.Contains("cts?.Dispose()", src);
        Assert.DoesNotContain("Console.Error", src);
    }

    // N-P3-5: staleness check must see subdirectory sources.
    [Fact]
    public void N_P3_5_StalenessCheck_SeesSubdirectories()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        Assert.Contains("SearchOption.AllDirectories", src);
    }
}
