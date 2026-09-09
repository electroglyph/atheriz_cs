using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression tests for globals (registry, handlers, time, map, salt).
// Each test fails while the defect is present and passes once fixed; a few
// pin verified-correct behavior and pass as-is.
[Collection("Ported")]
public class GlobalRegressionTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    // loading zero usable rows must preserve the live world.
    [Fact]
    public void EmptyLoad_PreservesLiveWorld()
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
                ObjectRegistry.LoadObjects(db);
            }
            Assert.NotEmpty(ObjectRegistry.Get(live.Id));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } Reset(); }
    }

    // The UnauthorizedAccessException fallback must read any peer-persisted
    // read any peer-persisted salt BEFORE overwriting, never write-then-verify.
    [Fact]
    public void SaltFallback_ReadsBeforeOverwriting()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "SaltProvider.cs");
        var region = SourceScan.Region(src, "catch (UnauthorizedAccessException)");
        Assert.True(region.IndexOf("TryReadSalt", StringComparison.Ordinal) < region.IndexOf("WriteAllText", StringComparison.Ordinal));
    }

    // DoStartup must thread settings.SavePath into all singletons.
    [Fact]
    public void DoStartup_ThreadsSettingsIntoSingletons()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        var region = SourceScan.Region(src, "public static void DoStartup(");
        Assert.Contains("GetMapHandler(settings", region);
        Assert.Contains("GetNodeHandler(settings", region);
        Assert.Contains("GetGameTime(settings", region);
    }

    // DoStartup load phase must hold _worldLock against shutdown/reload.
    [Fact]
    public void DoStartup_HoldsWorldLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        var region = SourceScan.Region(src, "public static void DoStartup(");
        Assert.Contains("lock (_worldLock)", region);
    }

    // id watermark must advance for maxNodeId==0 (single node Id 0).
    [Fact]
    public void Watermark_AdvancesForIdZero()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        Assert.DoesNotContain("if (maxNodeId != 0)", src);
    }

    // parameterless MapHandler.Save must surface failures, not swallow.
    [Fact]
    public void MapSave_SurfacesFailures()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        var region = SourceScan.Region(src, "public virtual void Save(bool force = false)");
        Assert.Contains("AtherizLogger.Log", region);
    }

    // Load must query before locking, not hold the write lock over I/O.
    [Fact]
    public void MapLoad_QueriesBeforeLocking()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        var region = SourceScan.Region(src, "public void Load(AtherizDbContext db)");
        Assert.True(region.IndexOf("AsNoTracking().Any()", StringComparison.Ordinal) < region.IndexOf("EnterWriteLock", StringComparison.Ordinal));
    }

    // Render must not clear a dirty flag set after PreRender.
    [Fact]
    public void Render_UsesGenerationGuard()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        Assert.DoesNotContain("try { MapChanged = false; }", src);
    }

    // RemoveNode must resolve under the mutation lock, not snapshot-then-act.
    [Fact]
    public void RemoveNode_ResolvesUnderLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.Partial.cs");
        var region = SourceScan.Region(src, "public void RemoveNode(Coord coord)");
        Assert.True(region.IndexOf("EnterWriteLock", StringComparison.Ordinal) < region.IndexOf("GetNode(coord)", StringComparison.Ordinal));
    }

    // RemapTransitions must snapshot the caller dict and not clobber destinations.
    [Fact]
    public void RemapTransitions_SnapshotsAndGuards()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.Partial.cs");
        var region = SourceScan.Region(src, "public void RemapTransitions(Dictionary<Coord, Coord> oldToNewFull)");
        Assert.Contains(".ToList()", region);
        Assert.DoesNotContain("_transitions[newFull] = trans;", region);
    }

    // server-event dispatch walks the registry once and logs handler failures.
    [Fact]
    public void ServerEventDispatch_SinglePassAndLogged()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        var region = SourceScan.Region(src, "private static void InvokeHooks(");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(region, "FilterBy"));
        Assert.DoesNotContain("catch (Exception) { }", region);
    }

    // a twice-rotated key goes stale and resolves to nothing (mapedit.py:50-52 _evict).
    // only the single previous generation stays usable (retry path).
    [Fact]
    public void DoubleRotation_OrphansGrandparent()
    {
        Reset();
        try
        {
            var k1 = MapEdit.Grant("127.0.0.1", "regarea", 0);
            var r1 = MapEdit.Consume(k1, "127.0.0.1", 0);
            Assert.Equal(MapEditResult.Processed, r1.Status);
            var k2 = r1.NewKey!;
            var r2 = MapEdit.Consume(k2, "127.0.0.1", 1);
            Assert.Equal(MapEditResult.Processed, r2.Status);
            Assert.Null(MapEdit.GetChain(k1));
            var r3 = MapEdit.Consume(k1, "127.0.0.1", 0);
            Assert.Equal(MapEditResult.Reject, r3.Status);
            Assert.Equal("unknown_key", r3.Reason);
            // single previous generation still retries.
            Assert.NotNull(MapEdit.GetChain(k2));
        }
        finally { Reset(); }
    }

    // object-dict alarm payloads must survive, not be silently dropped.
    [Fact]
    public void ObjectDictAlarm_Preserved()
    {
        Reset();
        try
        {
            var gt = new GameTime(settings: null, autoLoad: false);
            var caller = GameObject.Create("alarmcaller");
            ObjectRegistry.AddObject(caller);
            gt.AddAlarm("0", "0", caller, false, (object)new Dictionary<string, object> { ["k"] = "v" });
            var snap = gt.SnapshotAlarms();
            Assert.True(snap.TryGetValue(("0", "0"), out var list) && list.Count > 0);
            Assert.NotNull(list[0].Data);
        }
        finally { Reset(); }
    }

    // ClearForShutdown must not leave stale command sets behind.
    [Fact]
    public void ClearForShutdown_ClearsCommandSets()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        var region = SourceScan.Region(src, "internal static void ClearForShutdown()");
        Assert.Contains("_loggedInCmdSet = null", region);
        Assert.Contains("_unloggedInCmdSet = null", region);
    }

    // Stop(otherTicker) must not kill owned fallbacks out from under live use.
    [Fact]
    public void StopForeignTicker_KeepsOwnedFallbacks()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "GameTime.cs");
        var region = SourceScan.Region(src, "public void Stop(AsyncTicker ticker)");
        Assert.DoesNotContain("        StopOwnedFallbacks();", region);
    }

    // shutdown broadcast runs last, after ticker/pool stops and saves (startstop.py:69-75).
    [Fact]
    public void Broadcast_FollowsStopsAndSaves()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        var region = SourceScan.Region(src, "public static void DoShutdown(");
        int msg = region.IndexOf("\"msg_all\"", StringComparison.Ordinal);
        Assert.True(msg > region.IndexOf("\"ticker_stop\"", StringComparison.Ordinal));
        Assert.True(msg > region.IndexOf("\"threadpool_stop\"", StringComparison.Ordinal));
        Assert.True(msg > region.IndexOf("SaveWorld(settings)", StringComparison.Ordinal));
    }

    // AutosaveTick(settings) must journal/save objects via settings.SavePath.
    [Fact]
    public void AutosaveTick_UsesExplicitSettingsPath()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "Autosave.cs");
        var region = SourceScan.Region(src, "public static void AutosaveTick(AtherizSettings?");
        Assert.DoesNotContain("AtherizDbContextFactory.Create()", region);
    }

    // creation lock must not cover GetNode I/O, and the
    // success-path save must honor settingsOverride.
    [Fact]
    public void CharCreate_NarrowsLockAndHonorsSettings()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        var region = SourceScan.Region(src, "lock (_charCreateLock)");
        Assert.DoesNotContain("GetNodeHandler().GetNode", region);
        Assert.Matches(@"SaveObjects\([^)]", src);
    }

    // salt RNG must not run under the global salt lock.
    [Fact]
    public void SaltRng_RunsOutsideLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "SaltProvider.cs");
        var region = src.Substring(src.IndexOf("lock (_lock)", src.IndexOf("lock (_lock)", StringComparison.Ordinal) + 1, StringComparison.Ordinal));
        Assert.DoesNotContain("CryptoRandom", region);
    }

    // Autosave cached-handler reads must be locked/volatile.
    [Fact]
    public void CachedHandlerReads_AreSynchronized()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "Autosave.cs");
        Assert.Contains("Volatile.Read", src);
    }

    // GetGameTime factory reads must be volatile (style nit;
    // race claim withdrawn — factory runs under the singleton write lock).
    [Fact]
    public void GameTimeFactory_ReadsVolatile()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        var region = SourceScan.Region(src, "public static GameTime GetGameTime()");
        Assert.Contains("Volatile.Read(ref _asyncTicker)", region);
    }

    // AddObject must not pay an O(n) stale-key scan per insert.
    [Fact]
    public void AddObject_HasNoLinearScan()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        Assert.DoesNotContain("AllObjects.Where(kv => ReferenceEquals", src);
    }

    // caller enumerables must be materialized before the read lock.
    [Fact]
    public void GetIds_MaterializesBeforeLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var region = SourceScan.Region(src, "public static List<GameObject> Get(IEnumerable<int> ids)");
        Assert.Contains("ids.ToList()", region);
    }

    // saveability check must not span two sequential locks.
    [Fact]
    public void SaveableCheck_IsAtomic()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var region = SourceScan.Region(src, "private static bool IsStillSaveable(");
        Assert.True(region.IndexOf("obj.SyncRoot.EnterReadLock()", StringComparison.Ordinal) < region.IndexOf("AllLock.ExitReadLock()", StringComparison.Ordinal));
    }

    // mapedit token generation must run outside the global write lock.
    [Fact]
    public void TokenGen_RunsOutsideLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapEdit.cs");
        var region = SourceScan.Region(src, "public static string Grant(");
        Assert.True(region.IndexOf("GenerateToken()", StringComparison.Ordinal) < region.IndexOf("EnterWriteLock", StringComparison.Ordinal));
    }

    // alarm dispatch must clone the payload per firing.
    [Fact]
    public void AlarmDispatch_ClonesPayload()
    {
        Reset();
        try
        {
            var gt = new GameTime(settings: null, autoLoad: false);
            var caller = GameObject.Create("repeatcaller");
            ObjectRegistry.AddObject(caller);
            gt.AddAlarm("1", "2", caller, true, new Dictionary<string, JsonElement>
            {
                ["k"] = JsonDocument.Parse("\"v\"").RootElement.Clone(),
            });
            var snap1 = gt.SnapshotAlarms();
            Assert.True(snap1.TryGetValue(("1", "2"), out var list1) && list1.Count > 0);
            list1[0].CallerId = -7;
            var snap2 = gt.SnapshotAlarms();
            Assert.True(snap2.TryGetValue(("1", "2"), out var list2) && list2.Count > 0);
            Assert.NotEqual(-7, list2[0].CallerId);
        }
        finally { Reset(); }
    }

    // constructing a helper handler must not hijack the process-global current.
    [Fact]
    public void HandlerCtor_HasNoGlobalSideEffect()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        Assert.DoesNotContain("_current = this", src);
    }

    // load graft must take the target grid's write scope.
    [Fact]
    public void LoadGraft_TakesGridLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        int start = src.IndexOf("bool grafted = false;", StringComparison.Ordinal);
        int end = src.IndexOf("loadEvict.Add(n);", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var window = src.Substring(start, end - start);
        Assert.Contains("EnterWriteLock", window);
    }

    // load graft hole: a live-modified node whose cell is absent from the fresh row
    // must be re-inserted, not evicted (owner decision 2026-09-08 — the row expresses
    // no opinion about the cell, so evicting destroys newer in-memory edits).
    [Fact]
    public void LoadGraft_HoleReinsertsLiveModifiedNode()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var area = new NodeArea("grafthole");
            area.AddGrid(new NodeGrid("grafthole", 0));
            nh.AddArea(area);
            nh.AddNode(new Node(new Coord("grafthole", 0, 0, 0)));
            nh.Save(force: true);
            // Live-only edit after the save: never persisted, so the next Load's
            // fresh row has a hole at (3,3).
            var live = new Node(new Coord("grafthole", 3, 3, 0));
            nh.AddNode(live);
            live.IsModified = true;
            nh.Load();
            var got = nh.GetNode(new Coord("grafthole", 3, 3, 0));
            Assert.NotNull(got);
            Assert.Equal(live.Id, got!.Id);
            Assert.NotEmpty(ObjectRegistry.Get(live.Id));
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // Remap collisions are first-wins and loud: a relocated dict landing on an
    // occupied destination keeps the pre-existing per-name door and logs the drop
    // (owner decision 2026-09-08 — never silently clobber on a merge).
    [Fact]
    public void RemapDoors_CollisionKeepsPreexistingAndLogs()
    {
        Reset();
        using var env = GlobalTestEnv.Enter();
        NodeHandler? nh = null;
        try
        {
            nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var a = new Coord("remapcol", 0, 0, 0);
            var d = new Coord("remapcol", 5, 5, 0);
            var doorA = new Door(a, new Coord("remapcol", 9, 9, 0), "east", "west", closed: false, locked: false);
            var doorD = new Door(d, new Coord("remapcol", 8, 8, 0), "east", "west", closed: false, locked: false);
            nh.AddDoor(doorA);
            nh.AddDoor(doorD);
            using var cap = new CaptureAtherizLog();
            nh.RemapDoors(new Dictionary<Coord, Coord> { [a] = d });
            var kept = nh.GetDoors(d);
            Assert.NotNull(kept);
            Assert.Same(doorD, kept!["east"]);
            Assert.Contains("RemapDoors", cap.Read());
        }
        finally { NodeHandler.SetCurrent(null); Reset(); }
    }

    // creation error paths must print outside the lock; the
    // success-path save must be failure-captured.
    [Fact]
    public void CharCreateIo_OutsideLockAndCaptured()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        var region = SourceScan.Region(src, "lock (_charCreateLock)");
        Assert.DoesNotContain("Out($", region);
        var tail = src.Substring(src.IndexOf("if (doneChar != null", StringComparison.Ordinal));
        Assert.Contains("catch", tail);
    }

    // malformed persisted grid keys must warn, not vanish silently.
    [Fact]
    public void MalformedGridKey_Warns()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        var region = SourceScan.Region(src, "public MapInfo ToDomain(");
        Assert.Contains("LogWarning", region);
    }

    // null legend coords must be skipped, not stamped on the origin.
    [Fact]
    public void NullLegendCoord_Skipped()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        Assert.DoesNotContain("?? (0, 0)", src);
    }

    // bare "Area" must not parse as origin; paren areas must parse.
    [Fact]
    public void CoordParse_RejectsBareArea()
    {
        Assert.False(Coord.TryParse("Area", out _));
    }

    [Fact]
    public void CoordParse_AcceptsParenInAreaName()
    {
        Assert.True(Coord.TryParse("My (old) Area(1,2,3)", out var c));
        Assert.Equal(new Coord("My (old) Area", 1, 2, 3), c);
    }

    // failed parses must not leak a default instance with null Area.
    [Fact]
    public void FailedParse_LeavesUsableCoord()
    {
        Assert.False(Coord.TryParse("", out var c));
        Assert.NotNull(c.Area);
    }

    // Lock-hiding is wontfix per repo rules (external game code depends on
    // the public lock API). Pins the PUBLIC surface instead, mirroring
    // LockExposureTests.
    [Fact]
    public void Locks_AreInternal()
    {
        var singletonLock = typeof(GlobalServices).GetProperty("SingletonLock",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.NotNull(singletonLock);
        Assert.True(singletonLock!.GetMethod!.IsPublic);
        var mapEditLock = typeof(MapEdit).GetField("Lock",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.NotNull(mapEditLock);
        Assert.True(mapEditLock!.IsPublic);
    }

    // one name per lock.
    [Fact]
    public void LockAliases_Collapsed()
    {
        var id = SourceScan.Read("src", "Atheriz.Core", "Globals", "IdGenerator.cs");
        Assert.DoesNotContain("object Lock = LockObj", id);
        var ss = SourceScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        Assert.DoesNotContain("_shutdownLock = _worldLock", ss);
    }

}
