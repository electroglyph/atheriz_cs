using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Audit;

// Should-be tests for audit §1 A3/A5/A6 and §2 B17/B20/B22/B24-B28/B31.
// Fails while the bug is present, passes once fixed. Production untouched.
//
// Deliberately NOT covered here (no deterministic single-process oracle;
// documented in audit.md instead): A1 optimistic-clear races, A7 torn
// multi-table checkpoints, B21 load-under-lock, B23 in-memory substitution,
// B29/B30 socket/TLS lifecycles, B32 pool leaks, §5 banned-pattern and O1-O9
// organization items (no behavioral delta; review-level).
[Collection("Ported")]
public class AuditGlobalsNetworkShouldBeTests
{
    // --- A3: ban/cooldown check-then-remove race ---

    [Fact]
    public void IpBan_ExpiryRefresh_Race_DoesNotDeleteFreshBan_Stress()
    {
        // audit A3: IsIpBanned does TryGetValue + unconditional Remove. A
        // BanIp landing between the two deletes the FRESH ban. Hammer the
        // interleave; a correct remove-if-equal never loses the refresh.
        ObjectRegistry.ClearAll();
        try
        {
            const string host = "10.9.9.9";
            int lost = 0;
            for (int i = 0; i < 2500; i++)
            {
                ObjectRegistry.BanIp(host, 999.0); // expired relative to t=1000
                var check = Task.Run(() => ObjectRegistry.IsIpBanned(host, 1000.0));
                var refresh = Task.Run(() => ObjectRegistry.BanIp(host, 1100.0));
                Task.WaitAll(check, refresh);
                if (!ObjectRegistry.IsIpBanned(host, 1000.0)) lost++;
                ObjectRegistry.UnbanIp(host);
            }
            Assert.Equal(0, lost);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void CreationCooldown_Refresh_Race_DoesNotDeleteFreshCooldown_Stress()
    {
        // audit A3: same check-then-remove shape in CreationCooldownActive.
        ObjectRegistry.ClearAll();
        try
        {
            const string host = "10.9.9.8";
            int lost = 0;
            for (int i = 0; i < 800; i++)
            {
                ObjectRegistry.ApplyCreationCooldown("create", host, 900.0, 50.0); // exp 950 < 1000
                var check = Task.Run(() => ObjectRegistry.CreationCooldownActive("create", host, 1000.0));
                var refresh = Task.Run(() => ObjectRegistry.ApplyCreationCooldown("create", host, 1000.0, 100.0));
                Task.WaitAll(check, refresh);
                if (!ObjectRegistry.CreationCooldownActive("create", host, 1000.0)) lost++;
                ObjectRegistry.ClearCreationCooldown(host);
            }
            Assert.Equal(0, lost);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- A5: raw lock exposure ---

    [Fact]
    public void GameObject_SyncRoot_IsNotPublic()
    {
        // audit A5 fix direction: raw ReaderWriterLockSlim exposure (SyncRoot /
        // NodeLock / Lock + ReadScope/WriteScope + raw Enter*) lets external
        // code invert the lock order (Channel.Msg vs Delete-detach today).
        var prop = typeof(GameObject).GetProperty("SyncRoot");
        Assert.NotNull(prop);
        Assert.False(prop!.GetMethod!.IsPublic);
    }

    // --- A6: ServerLifecycle readiness ---

    [Fact]
    public void ServerLifecycle_ResetForTesting_ClearsStartupFlag()
    {
        // audit A6: DoStartup sets _startupSucceeded=true even when
        // StartStop.DoStartup throws, and ResetForTesting never clears it, so
        // /ready can report ok for a failed startup.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 0 };
        try
        {
            ServerLifecycle.DoStartup(settings);
            Assert.True(ServerLifecycle.StartupSucceeded);
            ServerLifecycle.ResetForTesting();
            Assert.False(ServerLifecycle.StartupSucceeded);
        }
        finally { try { ServerLifecycle.DoShutdown(settings); } catch { } }
    }

    [Fact]
    public void ServerLifecycle_WorldAndShutdownLocks_AreOneLock()
    {
        // audit A6: Python has a single _WORLD_LOCK (with _shutdown_lock as an
        // alias); the port has two independent locks, so the documented
        // ordering guarantee does not hold.
        var t = typeof(ServerLifecycle);
        var w = t.GetField("WorldLock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        var s = t.GetField("ShutdownLock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        Assert.Same(w, s);
    }

    // --- B17: IsMapable/MapEnabled split brain ---

    [Fact]
    public void MapEnabled_False_SurvivesSaveLoadRoundtrip()
    {
        // audit B17: setters sync both fields, but ApplyDtoFields restores only
        // _flags.IsMapable, so MapEnabled flips back to true on load.
        using var env = GlobalTestEnv.Enter();
        var o = GameObject.Create("mapper");
        ObjectRegistry.AddObject(o);
        o.MapEnabled = false;
        Assert.False(o.IsMapable);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db, force: true); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var back = ObjectRegistry.Get(o.Id).FirstOrDefault();
        Assert.NotNull(back);
        Assert.False(back!.IsMapable);
        Assert.False(back.MapEnabled);
    }

    // --- B20: silent load data loss ---

    [Fact]
    public void LoadObjects_CorruptRow_IsReported_NotSwallowed()
    {
        // audit B20: DTO parse / FromDto / ResolveRelations failures are
        // swallowed with no count, log, or metric — corrupt rows vanish.
        using var env = GlobalTestEnv.Enter();
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            db.Objects.Add(new ObjectRow { Id = 999991, Type = "object", Version = 1, Data = "{not valid json" });
            db.SaveChanges();
        }
        ObjectRegistry.ClearAll();
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            ObjectRegistry.LoadObjects(env.TempPath);
            log = cap.Read();
        }
        Assert.Contains("999991", log);
    }

    // --- B22: MapHandler.Load merges, never deletes ---

    [Fact]
    public void MapHandler_Load_RemovesRowsDeletedFromDb()
    {
        // audit B22: Load does `_data[kv]=v` merges, so rows deleted from the
        // DB resurrect from memory on the next save.
        using var env = GlobalTestEnv.Enter();
        var mh = GlobalServices.GetMapHandler();
        try
        {
            mh.SetMapInfo("zona", 0, new MapInfo());
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); mh.Save(db, force: true); }
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var rows = db.MapData.Where(r => r.Area == "zona").ToList();
                Assert.NotEmpty(rows);
                db.MapData.RemoveRange(rows);
                db.SaveChanges();
            }
            using (var db = new AtherizDbContext(env.TempPath)) { mh.Load(db); }
            Assert.Null(mh.GetMapInfo("zona", 0));
        }
        finally { GlobalServices.ResetForTesting(); }
    }

    // --- B24: GameTime legacy migration + alarm data lifetime ---

    [Fact]
    public void GameTime_LegacyMigration_SavesToConfiguredSavePath()
    {
        // audit B24: TryLoadLegacyFile calls parameterless Save() (factory
        // default path) instead of _settings.SavePath, so migrated ticks land
        // in the wrong DB before the legacy file is deleted.
        using var env = GlobalTestEnv.Enter();
        var customDir = Path.Combine(env.TempPath, "custom");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, "time"), "{\"ticks\": 12345}");
        var settings = new AtherizSettings { SavePath = customDir };
        var gt = new GameTime(settings, autoLoad: false);
        using (var db = new AtherizDbContext(customDir)) { db.Database.EnsureCreated(); gt.Load(db); }
        Assert.Equal(12345, gt.Ticks);
        Assert.False(File.Exists(Path.Combine(customDir, "time")));
        using (var db = new AtherizDbContext(customDir))
        {
            var row = db.GameTime.FirstOrDefault(r => r.Id == 0);
            Assert.NotNull(row);
        }
    }

    [Fact]
    public void GameTime_AlarmData_OutlivesSourceDocument_OnSave()
    {
        // audit B24: alarm Data holds JsonElements borrowing their source
        // JsonDocument; saving after the doc is disposed must not throw
        // ObjectDisposedException (elements must be cloned on the way in).
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(settings, autoLoad: false);
        var caller = GameObject.Create("alarmcaller");
        ObjectRegistry.AddObject(caller);
        // Live borrow into AddAlarm (doc alive), then kill the doc: the alarm
        // must own its copy from AddAlarm on, so Save cannot throw.
        using (var doc = JsonDocument.Parse("{\"k\":\"v\"}"))
        {
            var data = new Dictionary<string, JsonElement>();
            foreach (var p in doc.RootElement.EnumerateObject()) data[p.Name] = p.Value;
            gt.AddAlarm("1", "2", caller, false, data);
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            var ex = Record.Exception(() => gt.Save(db));
            Assert.Null(ex);
        }
    }

    // --- B25: RenderGrid unbounded ---

    [Fact]
    public void RenderGrid_RendersFullBoundingBox_LikePython()
    {
        // audit B25, corrected for Python parity (map.py:164-183 render_grid
        // renders the full bounding box with no cap). Capping would diverge
        // from Python output for large legitimate maps; sparse-grid blowup
        // requires build rights, which is a bigger problem already.
        var grid = new Dictionary<(int X, int Y), string> { [(0, 0)] = "#", [(2, 1)] = "#" };
        var (rendered, minX, maxY) = MapInfo.RenderGrid(grid);
        Assert.Equal(0, minX);
        Assert.Equal(1, maxY);
        Assert.Contains("#", rendered);
        var lines = rendered.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(3, lines[0].Length);
    }

    // --- B26: MapEdit live references ---

    [Fact]
    public void MapEdit_GetChain_ReturnsCopy_NotLiveReference()
    {
        // audit B26: GetChain hands out the stored mutable chain while Consume
        // mutates Key/Seq/PreviousKey in place -> torn reads for holders.
        MapEdit.ResetForTesting();
        try
        {
            string key = MapEdit.Grant("9.9.9.9", "limbo", 0, session: null);
            var c1 = MapEdit.GetChain(key);
            Assert.NotNull(c1);
            string orig = c1!.Key;
            c1.Key = "MUTATED";
            var c2 = MapEdit.GetChain(key);
            Assert.NotNull(c2);
            Assert.Equal(orig, c2!.Key);
        }
        finally { MapEdit.ResetForTesting(); }
    }

    // --- B27: numeric command handling ---

    [Fact]
    public void HandleCommand_NumericCommand_IsRejectedAsMalformed()
    {
        // audit B27: `cmdElement.GetString() ?? ToString()` intends to accept
        // non-string commands, but GetString() THROWS for numbers/arrays, so a
        // numeric command lands in the error path instead of the malformed
        // path. Malformed input must take the "Invalid message format" path.
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            var c = new TestConn("c1", "9.9.9.9");
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                mgr.HandleCommand(c, "[123]");
                Thread.Sleep(50);
                log = cap.Read();
            }
            Assert.Contains("Invalid message format", log);
            Assert.DoesNotContain("Error handling message", log);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }

    // --- B28: throttle dict growth ---

    [Fact]
    public void ThrottleWindow_EvictsExpiredHosts()
    {
        // audit B28: per-host throttle dicts grow forever (DoS memory); entries
        // whose window has fully elapsed must be evicted on later calls.
        var last = new Dictionary<string, double>();
        var lck = new object();
        Assert.True(ThrottleWindow.ShouldLog(last, lck, "h1", 5.0, now: 100.0));
        Assert.False(ThrottleWindow.ShouldLog(last, lck, "h1", 5.0, now: 102.0));
        Assert.True(ThrottleWindow.ShouldLog(last, lck, "h2", 5.0, now: 200.0));
        Assert.Single(last);
    }

    [Fact]
    public void ThrottleWindow_Now_IsPositiveMonotonic()
    {
        // Pin for §3.2: Now() exposes the monotonic clock.
        Assert.True(ThrottleWindow.Now() > 0);
    }

    // --- B31: AsyncThreadPool worker count ---

    [Fact]
    public void ThreadPool_FixedThreads_MatchPythonLayout()
    {
        // audit B31, corrected: maxThreads counts the async slot plus
        // (maxThreads-1) fixed workers, mirroring Python's threads[0] async +
        // threads[1:] workers layout (see the Threads dummy placeholder).
        // Existing saturation tests pin this contract; this pins it explicitly.
        var pool = new AsyncThreadPool(maxThreads: 3);
        try
        {
            Assert.Equal(3, pool.MaxThreads);
            Assert.Equal(2, pool.FixedThreads.Count);
            Assert.Equal(3, pool.Threads.Count); // dummy async slot + 2 workers
        }
        finally { pool.Stop(wait: false); }
    }
}
