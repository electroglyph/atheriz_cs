using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Audit;

// Fourth-pass regression tests for audit2.md §2 (Globals/ + roots).
// Each test FAILS while the finding is present and PASSES once fixed,
// except STRUCK findings (G-P0-2 as P0, G-P2-10, G-P2-11) which are pinned
// by documentation in audit2.md §7 (no deterministic failing test exists
// for a refuted mechanism). No production code touched.
[Collection("Ported")]
public class AuditGlobalsTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    // G-P0-1: loading zero usable rows must preserve the live world.
    [Fact]
    public void G_P0_1_EmptyLoad_PreservesLiveWorld()
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
                ObjectRegistry.LoadObjects(db);
            }
            Assert.NotEmpty(ObjectRegistry.Get(live.Id));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } Reset(); }
    }

    // G-P0-2b (demoted P0->P2): the UnauthorizedAccessException fallback must
    // read any peer-persisted salt BEFORE overwriting, never write-then-verify.
    [Fact]
    public void G_P0_2b_SaltFallback_ReadsBeforeOverwriting()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "SaltProvider.cs");
        var region = AuditScan.Region(src, "catch (UnauthorizedAccessException)");
        Assert.True(region.IndexOf("TryReadSalt", StringComparison.Ordinal) < region.IndexOf("WriteAllText", StringComparison.Ordinal));
    }

    // G-P0-3: DoStartup must thread settings.SavePath into all singletons.
    [Fact]
    public void G_P0_3_DoStartup_ThreadsSettingsIntoSingletons()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        var region = AuditScan.Region(src, "public static void DoStartup(");
        Assert.Contains("GetMapHandler(settings", region);
        Assert.Contains("GetNodeHandler(settings", region);
        Assert.Contains("GetGameTime(settings", region);
    }

    // G-P0-4: DoStartup load phase must hold _worldLock against shutdown/reload.
    [Fact]
    public void G_P0_4_DoStartup_HoldsWorldLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        var region = AuditScan.Region(src, "public static void DoStartup(");
        Assert.Contains("lock (_worldLock)", region);
    }

    // G-P1-1: id watermark must advance for maxNodeId==0 (single node Id 0).
    [Fact]
    public void G_P1_1_Watermark_AdvancesForIdZero()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        Assert.DoesNotContain("if (maxNodeId != 0)", src);
    }

    // G-P1-2: parameterless MapHandler.Save must surface failures, not swallow.
    [Fact]
    public void G_P1_2_MapSave_SurfacesFailures()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        var region = AuditScan.Region(src, "public virtual void Save(bool force = false)");
        Assert.Contains("AtherizLogger.Log", region);
    }

    // G-P1-3: Load must query before locking, not hold the write lock over I/O.
    [Fact]
    public void G_P1_3_MapLoad_QueriesBeforeLocking()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        var region = AuditScan.Region(src, "public void Load(AtherizDbContext db)");
        Assert.True(region.IndexOf("AsNoTracking().Any()", StringComparison.Ordinal) < region.IndexOf("EnterWriteLock", StringComparison.Ordinal));
    }

    // G-P1-4: Render must not clear a dirty flag set after PreRender.
    [Fact]
    public void G_P1_4_Render_UsesGenerationGuard()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        Assert.DoesNotContain("try { MapChanged = false; }", src);
    }

    // G-P1-5: tombstone commits must re-validate against live keys in-txn.
    [Fact]
    public void G_P1_5_Tombstones_RevalidatedAtCommit()
    {
        var node = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        var commit = node.Substring(node.IndexOf("Tombstone deletes ride", StringComparison.Ordinal));
        commit = commit.Substring(0, commit.IndexOf("onRollback", StringComparison.Ordinal));
        Assert.Contains("ExceptWith(_areas.Keys)", commit);
        var map = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        Assert.Equal(2, AuditScan.Count(map, "ExceptWith(_data.Keys)"));
    }

    // G-P1-6: RemoveNode must resolve under the mutation lock, not snapshot-then-act.
    [Fact]
    public void G_P1_6_RemoveNode_ResolvesUnderLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.Partial.cs");
        var region = AuditScan.Region(src, "public void RemoveNode(Coord coord)");
        Assert.True(region.IndexOf("EnterWriteLock", StringComparison.Ordinal) < region.IndexOf("GetNode(coord)", StringComparison.Ordinal));
    }

    // G-P1-7: RemapTransitions must snapshot the caller dict and not clobber destinations.
    [Fact]
    public void G_P1_7_RemapTransitions_SnapshotsAndGuards()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.Partial.cs");
        var region = AuditScan.Region(src, "public void RemapTransitions(Dictionary<Coord, Coord> oldToNewFull)");
        Assert.Contains(".ToList()", region);
        Assert.DoesNotContain("_transitions[newFull] = trans;", region);
    }

    // G-P1-8: a twice-rotated chain must still resolve to the live generation.
    [Fact]
    public void G_P1_8_DoubleRotation_ResolvesLiveChain()
    {
        Reset();
        try
        {
            var k1 = MapEdit.Grant("127.0.0.1", "auditarea", 0);
            var r1 = MapEdit.Consume(k1, "127.0.0.1", 0);
            Assert.Equal(MapEditResult.Processed, r1.Status);
            var r2 = MapEdit.Consume(r1.NewKey!, "127.0.0.1", 1);
            Assert.Equal(MapEditResult.Processed, r2.Status);
            Assert.NotNull(MapEdit.GetChain(k1));
        }
        finally { Reset(); }
    }

    // G-P1-9: object-dict alarm payloads must survive, not be silently dropped.
    [Fact]
    public void G_P1_9_ObjectDictAlarm_Preserved()
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

    // G-P1-10: ClearForShutdown must not leave stale command sets behind.
    [Fact]
    public void G_P1_10_ClearForShutdown_ClearsCommandSets()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        var region = AuditScan.Region(src, "internal static void ClearForShutdown()");
        Assert.Contains("_loggedInCmdSet = null", region);
        Assert.Contains("_unloggedInCmdSet = null", region);
    }

    // G-P1-11: Stop(otherTicker) must not kill owned fallbacks out from under live use.
    [Fact]
    public void G_P1_11_StopForeignTicker_KeepsOwnedFallbacks()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "GameTime.cs");
        var region = AuditScan.Region(src, "public void Stop(AsyncTicker ticker)");
        Assert.DoesNotContain("        StopOwnedFallbacks();", region);
    }

    // G-P1-12: shutdown broadcast must precede ticker/pool stops.
    [Fact]
    public void G_P1_12_Broadcast_PrecedesStops()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        var region = AuditScan.Region(src, "public static void DoShutdown(");
        Assert.True(region.IndexOf("\"msg_all\"", StringComparison.Ordinal) < region.IndexOf("\"ticker_stop\"", StringComparison.Ordinal));
    }

    // G-P1-13: AutosaveTick(settings) must journal/save objects via settings.SavePath.
    [Fact]
    public void G_P1_13_AutosaveTick_UsesExplicitSettingsPath()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "Autosave.cs");
        var region = AuditScan.Region(src, "public static void AutosaveTick(AtherizSettings?");
        Assert.DoesNotContain("AtherizDbContextFactory.Create()", region);
    }

    // G-P1-14 PARTIAL: creation lock must not cover GetNode I/O, and the
    // success-path save must honor settingsOverride.
    [Fact]
    public void G_P1_14_CharCreate_NarrowsLockAndHonorsSettings()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        var region = AuditScan.Region(src, "lock (_charCreateLock)");
        Assert.DoesNotContain("GetNodeHandler().GetNode", region);
        Assert.Matches(@"SaveObjects\([^)]", src);
    }

    // G-P2-1: salt RNG must not run under the global salt lock.
    [Fact]
    public void G_P2_1_SaltRng_RunsOutsideLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "SaltProvider.cs");
        var region = src.Substring(src.IndexOf("lock (_lock)", src.IndexOf("lock (_lock)", StringComparison.Ordinal) + 1, StringComparison.Ordinal));
        Assert.DoesNotContain("CryptoRandom", region);
    }

    // G-P2-2: Autosave cached-handler reads must be locked/volatile.
    [Fact]
    public void G_P2_2_CachedHandlerReads_AreSynchronized()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "Autosave.cs");
        Assert.Contains("Volatile.Read", src);
    }

    // G-P2-3 PARTIAL: GetGameTime factory reads must be volatile (style nit;
    // race claim withdrawn — factory runs under the singleton write lock).
    [Fact]
    public void G_P2_3_GameTimeFactory_ReadsVolatile()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        var region = AuditScan.Region(src, "public static GameTime GetGameTime()");
        Assert.Contains("Volatile.Read(ref _asyncTicker)", region);
    }

    // G-P2-4: AddObject must not pay an O(n) stale-key scan per insert.
    [Fact]
    public void G_P2_4_AddObject_HasNoLinearScan()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        Assert.DoesNotContain("AllObjects.Where(kv => ReferenceEquals", src);
    }

    // G-P2-5: caller enumerables must be materialized before the read lock.
    [Fact]
    public void G_P2_5_GetIds_MaterializesBeforeLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var region = AuditScan.Region(src, "public static List<GameObject> Get(IEnumerable<int> ids)");
        Assert.Contains("ids.ToList()", region);
    }

    // G-P2-6: saveability check must not span two sequential locks.
    [Fact]
    public void G_P2_6_SaveableCheck_IsAtomic()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var region = AuditScan.Region(src, "private static bool IsStillSaveable(");
        Assert.True(region.IndexOf("obj.SyncRoot.EnterReadLock()", StringComparison.Ordinal) < region.IndexOf("AllLock.ExitReadLock()", StringComparison.Ordinal));
    }

    // G-P2-7: mapedit token generation must run outside the global write lock.
    [Fact]
    public void G_P2_7_TokenGen_RunsOutsideLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapEdit.cs");
        var region = AuditScan.Region(src, "public static string Grant(");
        Assert.True(region.IndexOf("GenerateToken()", StringComparison.Ordinal) < region.IndexOf("EnterWriteLock", StringComparison.Ordinal));
    }

    // G-P2-8: alarm dispatch must clone the payload per firing.
    [Fact]
    public void G_P2_8_AlarmDispatch_ClonesPayload()
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

    // G-P2-9: constructing a helper handler must not hijack the process-global current.
    [Fact]
    public void G_P2_9_HandlerCtor_HasNoGlobalSideEffect()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        Assert.DoesNotContain("_current = this", src);
    }

    // G-P2-12: load graft must take the target grid's write scope.
    [Fact]
    public void G_P2_12_LoadGraft_TakesGridLock()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        int start = src.IndexOf("bool grafted = false;", StringComparison.Ordinal);
        int end = src.IndexOf("loadEvict.Add(n);", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var window = src.Substring(start, end - start);
        Assert.Contains("EnterWriteLock", window);
    }

    // G-P2-13: creation error paths must print outside the lock; the
    // success-path save must be failure-captured.
    [Fact]
    public void G_P2_13_CharCreateIo_OutsideLockAndCaptured()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        var region = AuditScan.Region(src, "lock (_charCreateLock)");
        Assert.DoesNotContain("Out($", region);
        var tail = src.Substring(src.IndexOf("if (doneChar != null", StringComparison.Ordinal));
        Assert.Contains("catch", tail);
    }

    // G-P2-14: malformed persisted grid keys must warn, not vanish silently.
    [Fact]
    public void G_P2_14_MalformedGridKey_Warns()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        var region = AuditScan.Region(src, "public MapInfo ToDomain(");
        Assert.Contains("LogWarning", region);
    }

    // G-P2-15: null legend coords must be skipped, not stamped on the origin.
    [Fact]
    public void G_P2_15_NullLegendCoord_Skipped()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        Assert.DoesNotContain("?? (0, 0)", src);
    }

    // G-P3-1: DeleteObjects should take ids, not unused SQL text.
    [Fact]
    public void G_P3_1_DeleteObjects_TakesIds()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        Assert.Contains("DeleteObjects(AtherizDbContext db, List<int>", src);
    }

    // G-P3-2: dead eviction branch + dead op parameter.
    [Fact]
    public void G_P3_2_DeadBranches_Removed()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var evict = AuditScan.Region(src, "private void EvictIfNeeded(");
        Assert.DoesNotContain("justSet", evict);
        // "Remove the parameter" resolution for the ignored op argument.
        Assert.DoesNotContain("CreationCooldownActive(string op,", src);
    }

    // G-P3-3: six map-render/parse nits, all simplifiable.
    [Fact]
    public void G_P3_3_MapNits_Simplified()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapHandler.cs");
        var conv = AuditScan.Region(src, "public override (int X, int Y)? Read(");
        Assert.Contains("JsonException", conv);
        Assert.True(AuditScan.Count(src, "grid.Keys.Min(k") + AuditScan.Count(src, "grid.Keys.Max(k") <= 2);
        Assert.Contains("HashSet<string> chars", src);
        Assert.Equal(1, AuditScan.Count(src, "if (n && s && e && w) return"));
        var pre = AuditScan.Region(src, "public void PreRender()");
        Assert.Equal(1, AuditScan.Count(pre, "new Dictionary<(int, int), string>(PreGrid)"));
        var save = src;
        Assert.True(save.IndexOf("if (!force && !wasChanged", StringComparison.Ordinal) < save.IndexOf("FromDomain(mi)", StringComparison.Ordinal));
    }

    // G-P3-4: default salt cache key must be CWD-independent.
    [Fact]
    public void G_P3_4_DefaultSaltKey_IsFullPath()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "SaltProvider.cs");
        Assert.DoesNotContain("? \"secret\" :", src);
    }

    // G-P3-5 PARTIAL: bare "Area" must not parse as origin; paren areas must parse.
    [Fact]
    public void G_P3_5_CoordParse_RejectsBareArea()
    {
        Assert.False(Coord.TryParse("Area", out _));
    }

    [Fact]
    public void G_P3_5b_CoordParse_AcceptsParenInAreaName()
    {
        Assert.True(Coord.TryParse("My (old) Area(1,2,3)", out var c));
        Assert.Equal(new Coord("My (old) Area", 1, 2, 3), c);
    }

    // G-P3-6: failed parses must not leak a default instance with null Area.
    [Fact]
    public void G_P3_6_FailedParse_LeavesUsableCoord()
    {
        Assert.False(Coord.TryParse("", out var c));
        Assert.NotNull(c.Area);
    }

    // G-P3-7: locks must not be publicly acquirable; TryGet needs no try/catch.
    [Fact]
    public void G_P3_7_Locks_AreInternal()
    {
        var gs = AuditScan.Read("src", "Atheriz.Core", "Globals", "GlobalServices.cs");
        Assert.DoesNotContain("public static ReaderWriterLockSlim SingletonLock", gs);
        Assert.DoesNotContain("try { var snap = Volatile.Read", gs);
        var me = AuditScan.Read("src", "Atheriz.Core", "Globals", "MapEdit.cs");
        Assert.DoesNotContain("public static readonly ReaderWriterLockSlim Lock", me);
    }

    // G-P3-8: one name per lock.
    [Fact]
    public void G_P3_8_LockAliases_Collapsed()
    {
        var id = AuditScan.Read("src", "Atheriz.Core", "Globals", "IdGenerator.cs");
        Assert.DoesNotContain("object Lock = LockObj", id);
        var ss = AuditScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        Assert.DoesNotContain("_shutdownLock = _worldLock", ss);
    }

    // G-P3-9: closed-DB guards collapse into one helper.
    [Fact]
    public void G_P3_9_ClosedDbGuard_HasHelper()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        Assert.Contains("RunClosedDbGuard(", src);
    }

    // G-P3-10: flag-restore blocks collapse into one helper.
    [Fact]
    public void G_P3_10_FlagRestore_HasHelper()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "NodeHandler.cs");
        Assert.Contains("RestoreSaveFlags(", src);
    }

    // G-P3-11 PARTIAL: single registry walk per server event + logged swallows.
    [Fact]
    public void G_P3_11_ServerEvents_SinglePassAndLogged()
    {
        var se = AuditScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        var region = AuditScan.Region(se, "private static void InvokeHooks(");
        region = region.Substring(0, region.IndexOf("private static string ToPascal", StringComparison.Ordinal));
        Assert.Equal(1, AuditScan.Count(region, "FilterBy("));
        Assert.DoesNotContain("catch { }", region);
        var ss = AuditScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        Assert.DoesNotContain("try { h(); } catch (Exception) { }", ss);
        Assert.DoesNotContain("try { go.AtTick(); } catch (Exception) { }", ss);
    }

    // G-P3-12: dead ordinal param + duplicated SunUp predicate.
    [Fact]
    public void G_P3_12_OrdinalAndSunUp_Folded()
    {
        var src = AuditScan.Read("src", "Atheriz.Core", "Globals", "GameTime.cs");
        Assert.DoesNotContain("string ordinal(string dayStr", src);
        Assert.DoesNotContain("public bool SunUpAlt(int hour)", src);
    }
}
