// faithful ALC single-load + _apply_patch + _reload_game_logic
//
// Reflection is confined to the live-patch field copy here (see
// SourceHygieneTests exclusions): ctor-bypass is intrinsic to _apply_patch
// fidelity (Python skips __init__; covered by PortedReloaderTests with a
// throwing ctor), and no non-reflection mechanism instantiates without
// running a ctor. Member-method lookups use direct virtual dispatch
// (ResolveRelations/AtTick).
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Atheriz.Core.Concurrency;
namespace Atheriz.Core.Plugins;
public static class PluginReloader
{
    public static readonly HashSet<string> ExcludedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    { "Microsoft.*", "System.*", "Atheriz.Core", "netstandard", "xunit.*" };
    // Guards cooperative mutation through the helpers below. Raw writes
    // straight to the field still work (back-compat) but race readers;
    // the reader retries those, while helper-driven writes never tear.
    private static readonly Lock _excludedLock = new();
    // _gate serializes concurrent *reloads* only (TryEnter = skip when busy,
    // pinned by Reloads_AreSerialized_MaxOverlapOne — NOT a bounded wait, so a stuck
    // reload can never wedge admin threads). It does NOT exclude world mutation:
    // that exclusion is StartStop.WorldLock, held around the patch phase below
    // (same outermost direction as DoShutdown: WorldLock → object/handler locks).
    //
    // Re-entrant skip-when-busy gate core. This is a DEDICATED reload instance —
    // never the shared DB write gate, which would serialize reloads against saves.
    // Audit 2026-09: reentrancy is defense-in-depth, not a calling
    // convention — entry points never nest (ReloadGameLogicAsync holds the
    // gate and calls the gateless ReloadCoreAsync; the public ReloadAsync
    // wrapper must never be entered under the gate). Invariant: never fork
    // unawaited work inside the gate (Task.Run/QueueUserWorkItem inherit
    // the AsyncLocal count and would corrupt the pairing). All three
    // entries pair TryEnter→try→finally Exit with nothing throwable
    // between enter and try.
    private sealed class ReentrantSkipGate
    {
        private readonly SemaphoreSlim _sem = new(1, 1);
        private readonly System.Threading.AsyncLocal<int> _recursion = new();

        public bool TryEnter()
        {
            if (_recursion.Value > 0) { _recursion.Value++; return true; }
            if (!_sem.Wait(0)) return false;
            _recursion.Value = 1;
            return true;
        }

        public void Exit()
        {
            if (_recursion.Value > 1) { _recursion.Value--; return; }
            _recursion.Value = 0;
            try { _sem.Release(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.ExitGate: " + logEx.Message, "PluginReloader"); }
        }
    }

    private static readonly ReentrantSkipGate _gate = new();
    private static bool TryEnterGate() => _gate.TryEnter();
    private static void ExitGate() => _gate.Exit();
    private static PluginLoader? _loader;
    // spellings (session/listeners/command) never matched a field — the port
    // renamed them _-prefixed — so only the resolving spellings are listed:
    // a rename that stops resolving is caught by the field-coverage test.
    private static readonly HashSet<string> _transientFields = new(StringComparer.Ordinal)
    { "_session","_listeners","_command","_lock","_hookRegistry","_msgLog" };
    // Shared exclusion check (also used by PluginLoader): exact filename match only.
    // Never substring-match the full path — "MySystem.Game.dll" must not match "System.*".
    // The set is public mutable (game code may add entries) and reloaded
    // while scans run on other threads, so a live enumeration can throw
    // mid-scan. Cooperative writers use the helpers below (locked);
    // the copy here is taken under the same lock, and a bounded retry
    // covers legacy raw-field writes landing mid-copy.
    public static void AddExcludedAssembly(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        lock (_excludedLock) ExcludedAssemblies.Add(pattern);
    }
    public static bool RemoveExcludedAssembly(string pattern)
    {
        lock (_excludedLock) return ExcludedAssemblies.Remove(pattern);
    }
    internal static bool IsExcludedAssembly(string p)
    {
        var n = Path.GetFileNameWithoutExtension(p) ?? "";
        string[] snapshot = [];
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                lock (_excludedLock) snapshot = [.. ExcludedAssemblies];
                break;
            }
            // Any corruption shape from an unsynchronized legacy raw-write
            // racing the copy (InvalidOperationException, ArgumentException
            // from a torn count, ...): the copy is side-effect-free, so a
            // bounded retry on any failure is safe. Cooperative writers use
            // the locked helpers and never trip this.
            catch (Exception) when (attempt < 64)
            {
                continue;
            }
        }
        foreach (var pat in snapshot)
        {
            // Null-tolerant: an unsynchronized legacy raw-write racing
            // the copy can corrupt the set's buckets so the snapshot yields a
            // null slot — skip it instead of throwing. Cooperative writers use
            // the locked helpers above and never plant nulls.
            if (pat is null) continue;
            if (pat.EndsWith(".*", StringComparison.Ordinal))
            { var pre = pat[..^2]; if (n.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return true; }
            else if (string.Equals(n, pat, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    private static bool IsExcluded(string p) => IsExcludedAssembly(p);
    // True for .cs files that can never feed the game dll, so they must not
    // count toward staleness: generated outputs under obj/bin segments, and
    // test sources (any ancestor dir named test/tests or ending in Tests, e.g.
    // Grotto.Tests/). Pure path-string check, no IO. Convention cost: game
    // code must not live under such names (a change there would load a stale
    // dll instead of skipping loudly).
    private static bool IsNonGameSource(string gameDir, string file)
    {
        try
        {
            var rel = Path.GetRelativePath(gameDir, file);
            foreach (var seg in rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]))
            {
                if (seg.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    seg.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    seg.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                    seg.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                    seg.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex) { Suppress("DiscoverGameAssembly", ex); }
        return false;
    }
    // Shared suppressed-log helper: the op string is preserved verbatim so
    // the emitted text stays byte-identical to the inlined LogDebug calls.
    private static void Suppress(string op, Exception ex) => AtherizLogger.LogDebug("Suppressed PluginReloader." + op + ": " + ex.Message, "PluginReloader");
// single load per assembly.
    // (The old pass1 scan-ALC existed only to count forward refs, then unloaded with
    // a stop-the-world triple GC while the gate was held; PluginLoader.Load scans
    // identically, so pass1 was deleted — one ALC, one load, one post-unload GC.)
    // Evicts live global-registry commands whose types died with a replaced
    // plugin load. Without this a reload patches world objects but keeps the
    // previous load's command INSTANCES (registry Add throws on duplicates,
    // so game reinstalls keep the stale ones): new command code never runs
    // until a restart, and the dead instances root the old ALC forever.
    // Pairing is by defining-assembly identity — fully generic, no game
    // knowledge. The game's own installer re-adds fresh instances right
    // after (patched objects run AtInit → reinstall). Per-object
    // InternalCmdSets are untouched (exits/channels rebuild on re-resolve).
    // Returns the evicted count. Never throws.
    public static int EvictStaleCommands(System.Reflection.Assembly? oldAssembly)
        => EvictStaleCommands(oldAssembly, CommandRegistry.LoggedIn, CommandRegistry.UnloggedIn);
    public static int EvictStaleCommands(System.Reflection.Assembly? oldAssembly, params CmdSet[] sets)
    {
        if (oldAssembly is null || sets is null || sets.Length == 0) return 0;
        int evicted = 0;
        foreach (var set in sets)
        {
            if (set is null) continue;
            List<Command> doomed;
            try { doomed = set.GetAll().Where(c => c is not null && ReferenceEquals(c.GetType().Assembly, oldAssembly)).ToList(); }
            catch (Exception ex) { Suppress("EvictStaleCommands", ex); continue; }
            foreach (var c in doomed)
            {
                try { set.Remove(c); evicted++; }
                catch (Exception ex) { Suppress("EvictStaleCommands", ex); }
            }
        }
        if (evicted > 0) Console.Error.WriteLine($"[HotReload] Evicted {evicted} stale commands.");
        return evicted;
    }
    // True when live objects already carry a previous load's replacement
    // types: plugin-defined (non-Core assembly) instances deriving a pair
    // base. Exact-match patching is blind to those, so converting again
    // would match nothing. Internal for direct unit tests.
    internal static bool WorldAlreadyConverted(Dictionary<Type, Type> replacements)
    {
        if (replacements is null || replacements.Count == 0) return false;
        var coreAsm = typeof(GameObject).Assembly;
        try
        {
            foreach (var kv in replacements)
            {
                if (kv.Key is null) continue;
                foreach (var o in ObjectRegistry.FilterBy(x => x is not null && x.GetType().Assembly != coreAsm && kv.Key.IsAssignableFrom(x.GetType())))
                    return true;
            }
        }
        catch (Exception ex) { Suppress("WorldAlreadyConverted", ex); }
        return false;
    }
    public static async Task<bool> ReloadAsync(string assemblyPath, AsyncTicker ticker, AsyncThreadPool pool)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath)) return false;
        if (IsExcluded(assemblyPath)) { Console.Error.WriteLine($"[PluginReloader] Skipping excluded: {assemblyPath}"); return false; }
        if (!TryEnterGate()) { Console.Error.WriteLine("[HotReload] Reload already in progress; skipping."); return false; }
        try { return await ReloadCoreAsync(assemblyPath, ticker, pool).ConfigureAwait(false); }
        finally { ExitGate(); }
    }
    // Gateless core: one gate-holding per operation. ReloadGameLogicAsync
    // holds the gate across assemblies and calls this directly — the public
    // wrapper above must never be entered under the gate. Reentrancy stays
    // in the gate only as defense-in-depth, not as a calling convention.
    private static async Task<bool> ReloadCoreAsync(string assemblyPath, AsyncTicker ticker, AsyncThreadPool pool)
    {
        await Task.Yield();
        var full = Path.GetFullPath(assemblyPath);
        if (!File.Exists(full)) { Console.Error.WriteLine($"[HotReload] Not found: {full}"); return false; }
        var oldAsm = _loader?.LoadedAssembly;
        if (_loader is not null) { try{_loader.Unload();}catch (Exception logEx) { Suppress("ReloadAsync", logEx); } _loader=null; GC.Collect(); }
        _loader = new PluginLoader();
        try { _loader.Load(full); } catch (Exception ex){ Console.Error.WriteLine($"[HotReload] Load failed: {ex.Message}"); return false; }
        Console.Error.WriteLine($"[HotReload] Loaded {_loader.Replacements.Count} repl from {Path.GetFileName(full)}.");
        // A previous load already converted the world when live instances
        // carry plugin types (non-Core assembly deriving a pair base):
        // exact-match patching can't see them, so a second conversion
        // would patch nothing while eviction destroyed the working
        // commands. Skip both loudly instead (restart picks up new code).
        if (WorldAlreadyConverted(_loader.Replacements))
        {
            Console.Error.WriteLine("[HotReload] World already converted by a previous load; skipping object patch and command eviction (restart to pick up new code).");
            return true;
        }
        int patched=0;
        // takes per-object write + handler read locks, same outermost direction
        // as DoShutdown, so no ABBA. Load/scan stay outside (I/O, no locks held).
        lock (StartStop.WorldLock)
        {
            // Stale command instances first, so the reinstall that patched
            // objects trigger via AtInit re-adds fresh ones instead of
            // colliding with (and keeping) the dead ones.
            try { EvictStaleCommands(oldAsm); } catch (Exception ex) { Suppress("ReloadAsync", ex); }
            foreach(var kv in _loader.Replacements.ToList()){ try{patched+=PatchLiveObjects(kv.Key,kv.Value);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] Patch {kv.Key.Name}->{kv.Value.Name}: {ex.Message}");} }
            try{ReregisterTicks(ticker);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] ReregisterTicks: {ex.Message}");}
        }
        Console.Error.WriteLine($"[HotReload] ReloadAsync patched {patched}.");
        return true;
    }
    // C# cannot swap __class__ in place: build the replacement via GetUninitializedObject
    // (bypasses ctor like Python skipping __init__), copy state, AddObject-replace by id,
    // then RewireReferences swaps instance-valued stores (channels/map/nodes/sessions).
    public static int PatchLiveObjects(Type oldType, Type newType)
    {
        if(oldType is null||newType is null) return 0;
        var live = ObjectRegistry.FilterBy(o=>o.GetType()==oldType);
        if(live.Count==0) return 0;
        int patched=0;
        foreach(var obj in live.ToList()){ try{if(PatchSingleObject(obj,newType))patched++;}catch(Exception ex){Console.Error.WriteLine($"[HotReload] patch {obj.Id}: {ex.Message}");} }
        // Only the live replacements need ResolveRelations (old instances are detached after AddObject rewire).
        var newLive=ObjectRegistry.FilterBy(o=>o.GetType()==newType);
        foreach(var obj in newLive){ try{ obj.ResolveRelations(); }catch (Exception logEx) { Suppress("PatchLiveObjects", logEx); }}
        return patched;
    }
    /// <summary>
    /// Rewire direct object refs from the pre-patch instance to its replacement
    /// (same id). Id-keyed stores (registry/contents/followers/locations) need no
    /// fix; instance-valued stores do: channel listeners, map infos, node grids,
    /// and the patched object's own session puppet refs.
    /// </summary>
    private static void RewireReferences(GameObject oldObj, GameObject newObj)
    {
        foreach (var c in ObjectRegistry.FilterBy(o => o.IsChannel))
            try { if (c is Channel ch) ch.ReplaceListener(newObj); } catch (Exception logEx) { Suppress("RewireReferences", logEx); }
        try { GlobalServices.GetMapHandler()?.ReplaceMapEntries(oldObj.Id, newObj); } catch (Exception logEx) { Suppress("RewireReferences", logEx); }
        if (newObj is Node nn)
        {
            try
            {
                foreach (var g in SnapshotGrids(GlobalServices.GetNodeHandler())) try { g.ReplaceNodeValue(nn); } catch (Exception logEx) { Suppress("RewireReferences", logEx); }
            }
            catch (Exception logEx) { Suppress("RewireReferences", logEx); }
        }
        // Own session (transient _session was restored onto newObj above).
        // Cross-session puppet-stack Prev refs are out of contract (no session registry).
        try { newObj.Session?.ReplacePuppetRefs(newObj); } catch (Exception logEx) { Suppress("RewireReferences", logEx); }
    }
    // Shared handler→areas→grids read-lock snapshot walk for RewireReferences
    // above and ReregisterTicks below. Per-site extras stay at their sites:
    // RewireReferences' node-type guard and ReplaceNodeValue calls,
    // ReregisterTicks' grid→nodes level.
    private static List<NodeGrid> SnapshotGrids(NodeHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        List<NodeArea> areas;
        handler.Lock.EnterReadLock();
        try { areas = handler.GetAreas(); } finally { handler.Lock.ExitReadLock(); }
        List<NodeGrid> grids = [];
        foreach (var area in areas)
        {
            area.Lock.EnterReadLock();
            try { grids.AddRange(area.Grids.Values); } finally { area.Lock.ExitReadLock(); }
        }
        return grids;
    }

    /// <summary>
    /// Builds the replacement, explicit contract first: a new type
    /// implementing <c>IMigrateFrom&lt;T&gt;</c> is constructed normally (all
    /// field initializers and invariants run) and pulls state via
    /// <c>MigrateFrom</c> — mirroring Python <c>_apply_patch</c> intent
    /// without skipping construction. Types without the contract keep the
    /// legacy field copy below (ctor bypass is intrinsic there: Python
    /// skips <c>__init__</c> side effects; covered by PortedReloaderTests
    /// with a throwing ctor). A declared-but-broken contract keeps the old
    /// instance live and loud, never half-applied, never silently copied.
    /// Transient session/lock state is engine-restored on both paths.
    /// Callers must hold <c>StartStop.WorldLock</c> (see <c>ReloadAsync</c>).
    /// </summary>
    private static bool PatchSingleObject(GameObject oldObj, Type newType)
    {
        var oldType=oldObj.GetType();
        var saved=new Dictionary<string,object?>(StringComparer.Ordinal);
        foreach(var fn in _transientFields){ var f=FindField(oldType,fn); if(f is not null) try{saved[fn]=f.GetValue(oldObj);}catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }}
        Dictionary<FieldInfo,object?> origSnap = [];
        var oldFields=GetAllFields(oldType);
        foreach(var f in oldFields) try{origSnap[f]=f.GetValue(oldObj);}catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }
        var lk=oldObj.SyncRoot; bool taken=false;
        try{
            try{lk.EnterWriteLock(); taken=true;}catch{taken=false;}
            var migration = TryMigrateExplicit(oldObj, oldType, newType);
            if (migration.Handled)
            {
                if (migration.Replacement is null) return false;
                RestoreTransients(newType, migration.Replacement, saved);
                return RegisterReplacement(oldObj, migration.Replacement, lk, ref taken);
            }
            GameObject newObj; try{newObj=(GameObject)RuntimeHelpers.GetUninitializedObject(newType);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] GetUninitializedObject {newType.Name}: {ex.Message}"); return false;}
            var newByName=GetAllFields(newType).GroupBy(f=>f.Name).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.Ordinal);
            foreach(var fOld in oldFields){
                if(_transientFields.Contains(fOld.Name)) continue;
                if(!newByName.TryGetValue(fOld.Name,out var fNews)) continue;
                object? v;
                try { v=fOld.GetValue(oldObj); } catch (Exception logEx) { Suppress("PatchSingleObject", logEx); continue; }
                foreach(var fNew in fNews){
                    if(fNew.IsInitOnly) continue;
                    // Pair by name AND assignability: a derived type may shadow
                    // a base field with an incompatible type (same name, new
                    // meaning). Writing the old value into that slot corrupts
                    // it and starves the true slot (reads keep hitting the
                    // unset base slot). Copy only to slots accepting the value.
                    try{
                        if(v is null){
                            if(!fNew.FieldType.IsValueType||Nullable.GetUnderlyingType(fNew.FieldType) is not null) fNew.SetValue(newObj,null);
                        }
                        else if(fNew.FieldType.IsAssignableFrom(v.GetType())||fNew.FieldType.IsAssignableFrom(fOld.FieldType)||fNew.FieldType==typeof(object)) fNew.SetValue(newObj,v);
                        // else: same-name shadow with an incompatible type and
                        // different meaning — leave the slot at its default.
                    }catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }
                }
            }
            RestoreTransients(newType, newObj, saved);
            // GetUninitializedObject skips field initializers, so readonly fields (e.g. _flags)
            // stay null — the copy loop above skips init-only fields. Backfill them from the
            // old instance (shared refs are safe: the old instance is detached after rewire).
            foreach (var fOld in oldFields)
            {
                if (!newByName.TryGetValue(fOld.Name, out var fNews)) continue;
                object? v;
                try { v = fOld.GetValue(oldObj); } catch (Exception logEx) { Suppress("PatchSingleObject", logEx); continue; }
                if (v is null) continue;
                foreach (var fNew in fNews)
                {
                    if (!fNew.IsInitOnly) continue;
                    // Same name+assignability pairing as the main loop above:
                    // an incompatible shadow must not swallow the backfill.
                    if (!(fNew.FieldType.IsAssignableFrom(v.GetType()) || fNew.FieldType.IsAssignableFrom(fOld.FieldType))) continue;
                    try
                    {
                        if (fNew.GetValue(newObj) is null) fNew.SetValue(newObj, v);
                    }
                    catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }
                }
            }
            return RegisterReplacement(oldObj, newObj, lk, ref taken);
        }catch{
            try{ foreach(var kv in origSnap) try{kv.Key.SetValue(oldObj,kv.Value);}catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }}catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }
            throw;
        }finally{ if(taken) try{lk.ExitWriteLock();}catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }}
    }
    // Explicit-contract probe: the first IMigrateFrom<> whose TOld accepts
    // the live instance wins (implement at most one per replacement type).
    // No contract → (false, null): the legacy copy path runs. Broken
    // contract (no ctor, unreachable, throwing MigrateFrom) → (true, null):
    // the old instance stays live; the failure is logged, never copied.
    // The call itself is a direct interface call (DIM forwarder routes to
    // the typed method) — no MethodInfo.Invoke.
    private static (bool Handled, GameObject? Replacement) TryMigrateExplicit(GameObject oldObj, Type oldType, Type newType)
    {
        foreach (var i in newType.GetInterfaces())
        {
            if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(IMigrateFrom<>)) continue;
            if (!i.GetGenericArguments()[0].IsAssignableFrom(oldType)) continue;
            GameObject candidate;
            try { candidate = (GameObject)Activator.CreateInstance(newType)!; }
            catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Migrate {oldType.Name}->{newType.Name}: ctor failed ({ex.GetBaseException().Message}); keeping old instance."); return (true, null); }
            if (candidate is not IMigrateFrom contract)
            { Console.Error.WriteLine($"[HotReload] Migrate {oldType.Name}->{newType.Name}: contract unreachable; keeping old instance."); return (true, null); }
            try { contract.MigrateFrom(oldObj); }
            catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Migrate {oldType.Name}->{newType.Name}: {ex.GetBaseException().Message}; keeping old instance."); return (true, null); }
            return (true, candidate);
        }
        return (false, null);
    }
    // Transient session/lock state belongs to the engine, not the game
    // type: restored identically on migrated and copied replacements —
    // MigrateFrom must never be trusted with locks or sessions.
    private static void RestoreTransients(Type newType, GameObject newObj, Dictionary<string, object?> saved)
    {
        foreach (var kv in saved) { var fNew = FindField(newType, kv.Key); if (fNew is not null && !fNew.IsInitOnly) try { fNew.SetValue(newObj, kv.Value); } catch (Exception logEx) { Suppress("PatchSingleObject", logEx); } }
        var lf = FindField(newType, "_lock");
        if (lf is not null) try { var cur = lf.GetValue(newObj); if (cur is null) lf.SetValue(newObj, saved.TryGetValue("_lock", out var v) ? v : new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion)); } catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }
    }
    // Shared tail: id, unlock-before-registry, AddObject, rewire. The unlock
    // order is load-bearing (registry→object inversion documented at the old
    // release site); both construction paths share it so neither can regress.
    private static bool RegisterReplacement(GameObject oldObj, GameObject newObj, ReaderWriterLockSlim lk, ref bool taken)
    {
        try { newObj.SetIdRaw(oldObj.Id); } catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }
        // release the old-object write lock BEFORE AddObject +
        // RewireReferences (which take channel/map/area/grid/session
        // locks). Holding it across inverted the registry→object order
        // of FilterBy while ReloadAsync holds WorldLock.
        if (taken) try { lk.ExitWriteLock(); taken = false; } catch (Exception logEx) { Suppress("PatchSingleObject", logEx); }
        ObjectRegistry.AddObject(newObj);
        // C# cannot swap __class__ in place like Python: AddObject replaced the id,
        // so rewire direct refs (channels/map/nodes/sessions hold instances, not ids).
        try { RewireReferences(oldObj, newObj); } catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Rewire {oldObj.Id}: {ex.Message}"); }
        return true;
    }
    private static FieldInfo? FindField(Type t,string n){ var cur=t; while(cur is not null&&cur!=typeof(object)){ var f=cur.GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); if(f is not null) return f; cur=cur.BaseType; } return null; }
    private static List<FieldInfo> GetAllFields(Type t){ List<FieldInfo> l = []; var cur=t; while(cur is not null&&cur!=typeof(object)){ l.AddRange(cur.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)); cur=cur.BaseType; } return l; }
// Remove old + AddCoro(at_tick, TickSeconds)
    public static void ReregisterTicks(AsyncTicker ticker)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        var tickables=ObjectRegistry.FilterBy(o=>o.IsTickable);
        try{
            foreach(var grid in SnapshotGrids(GlobalServices.GetNodeHandler())){
                grid.Lock.EnterReadLock(); List<Node> nodes;
                try{nodes=grid.Nodes.Values.Where(n=>n.IsTickable).ToList();}finally{grid.Lock.ExitReadLock();}
                foreach(var n in nodes) if(!tickables.Contains(n)) tickables.Add(n);
            }
        }catch (Exception logEx) { Suppress("ReregisterTicks", logEx); }
        RemoveTickDelegatesFor(ticker,tickables);
        foreach(var obj in tickables){
            double secs=1; try{secs=obj.TickSeconds;}catch{secs=1;} if(secs<=0) secs=1;
            // direct virtual dispatch — every GameObject
            // carries AtTick, so the old reflective tick-method lookup plus
            // Invoke was pure overhead with identical dispatch (all overrides,
            // no `new` shadows). No behavior change.
            try{ Action act=()=>{try{obj.AtTick();}catch (Exception logEx) { Suppress("ReregisterTicks", logEx); }}; ticker.AddCoro(act,secs);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] rereg {obj.Id}: {ex.Message}");}
        }
    }
    // Internal (not private): StartStop.ReregisterTicks shares this sweep so
    // both reload paths evict the same stale object-targeting delegates.
    internal static void RemoveTickDelegatesFor(AsyncTicker ticker, List<GameObject> tickables)
    {
        // Public ticker surface only (Slots/Coros/RemoveCoro) — never poke _slots/_coros privates (breaks on rename).
        IReadOnlyDictionary<double, AsyncTicker.TimeSlot> slots;
        try { slots = ticker.Slots; }
        catch (Exception ex) { Console.Error.WriteLine($"[HotReload] RemoveTickDelegates: {ex.Message}"); return; }
        // Zombie sweep: evict delegates targeting ANY game object, not just
        // live tickables. ReregisterTicks re-adds a fresh delegate for every
        // live tickable right after, so every pre-existing object-targeting
        // delegate is stale by construction: a pre-patch delegate targets
        // the OLD instance (shares the replacement's registry id but never
        // the reference), and a deleted object's delegate targets a dead id.
        // Matching by live-id set kept both zombies ticking on dead state.
        foreach (var kv in slots.ToList())
        {
            IReadOnlySet<Delegate> coros;
            try { coros = kv.Value.Coros; } catch { continue; }
            foreach (var d in coros.ToList())
            {
                if (!TargetsGameObject(d.Target)) continue;
                try
                {
                    var iv = TimeSpan.FromSeconds(kv.Key);
                    if (d is Action a) ticker.RemoveCoro(a, iv);
                    else if (d is Func<Task> f) ticker.RemoveCoro(f, iv);
                }
                catch (Exception logEx) { Suppress("RemoveTickDelegatesFor", logEx); }
            }
        }
    }
    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        public static readonly IdentityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static bool TargetsGameObject(object? target)
    {
        if (target is null) return false;
        // Any game object, live or dead: the re-add loop below covers every
        // live tickable with a fresh delegate, so a pre-existing delegate
        // targeting an object is stale however its id resolves.
        if (target is GameObject) return true;
        // Closure walk: Release builds one display class holding the tickable
        // directly; Debug splits captures across linked display classes
        // (CS$<>8__locals) — recurse through compiler-generated frames.
        var seen = new HashSet<object>(IdentityComparer.Instance);
        var queue = new Queue<object>();
        queue.Enqueue(target);
        seen.Add(target);
        for (int depth = 0; depth < 4 && queue.Count > 0; depth++)
        {
            int n = queue.Count;
            for (int i = 0; i < n; i++)
            {
                var cur = queue.Dequeue();
                if (cur is GameObject) return true;
                if (!IsCompilerGenerated(cur.GetType())) continue;
                FieldInfo[] fields;
                try { fields = cur.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                catch { continue; }
                foreach (var f in fields)
                {
                    if (f.FieldType.IsValueType) continue;
                    object? v;
                    try { v = f.GetValue(cur); } catch { continue; }
                    if (v is null || !seen.Add(v)) continue;
                    queue.Enqueue(v);
                }
            }
        }
        return false;
    }

    private static bool IsCompilerGenerated(Type t)
    {
        try
        {
            if (t.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length > 0)
                return true;
        }
        catch { }
        try { return (t.FullName ?? "").Contains("DisplayClass", StringComparison.Ordinal); }
        catch { return false; }
    }
// scan plugins + game project
    // Game project discovery: look for already-built dlls near CWD / SavePath parent (mirrors Python game folder import).
    // Never builds: Python imports source (no build step); a dropped-in .csproj must not trigger compilation on reload.
    public static int DiscoverGameAssembly(AtherizSettings settings, List<string> outList)
    {
        int added = 0;
        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { searchDirs.Add(Directory.GetCurrentDirectory()); } catch (Exception logEx) { Suppress("DiscoverGameAssembly", logEx); }
        try
        {
            var sp = settings.SavePath;
            if (!string.IsNullOrWhiteSpace(sp))
            {
                var abs = Path.IsPathRooted(sp) ? sp : Path.Combine(Directory.GetCurrentDirectory(), sp);
                var gameRoot = Path.GetDirectoryName(Path.GetFullPath(abs));
                if (!string.IsNullOrWhiteSpace(gameRoot) && Directory.Exists(gameRoot)) searchDirs.Add(gameRoot);
                // also parent of gameRoot (handles save/ inside mygame)
                try { var parent = Path.GetDirectoryName(gameRoot!); if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) searchDirs.Add(parent); } catch (Exception logEx) { Suppress("DiscoverGameAssembly", logEx); }
            }
        }
        catch (Exception logEx) { Suppress("DiscoverGameAssembly", logEx); }
        foreach (var dir in searchDirs)
        {
            string[] csprojs;
            try { csprojs = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly); } catch { continue; }
            foreach (var csproj in csprojs)
            {
                // skip server/core templates themselves
                var name = Path.GetFileNameWithoutExtension(csproj);
                if (string.Equals(name, "Atheriz.Server", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Atheriz.Core", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Atheriz.GameTemplate", StringComparison.OrdinalIgnoreCase)) continue;
                // Try already-built dll first
                var dllRelease = Path.Combine(dir, "bin", "Release", "net10.0", $"{name}.dll");
                var dllDebug = Path.Combine(dir, "bin", "Debug", "net10.0", $"{name}.dll");
                string? dll = null;
                if (File.Exists(dllRelease)) dll = dllRelease;
                else if (File.Exists(dllDebug)) dll = dllDebug;
                // Build if missing or csproj newer than dll
                bool needBuild = dll is null;
                if (!needBuild)
                {
                    try
                    {
                        var csprojTime = File.GetLastWriteTimeUtc(csproj);
                        var dllTime = File.GetLastWriteTimeUtc(dll!);
                        if (csprojTime > dllTime) needBuild = true;
                        // also if any game *.cs newer (recursive: subdirectory
                        // edits must not look "up to date") — but only sources
                        // that can feed the game dll. Generated outputs
                        // (obj/bin) and test projects never do: counting them
                        // bricks every boot into engine-only after any test
                        // build/edit, which build.sh (game project only,
                        // incremental) can never heal.
                        foreach (var cs in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                        {
                            if (IsNonGameSource(dir, cs)) continue;
                            if (File.GetLastWriteTimeUtc(cs) > dllTime) { needBuild = true; break; }
                        }
                    }
                    catch (Exception logEx) { Suppress("DiscoverGameAssembly", logEx); }
                }
                // Reload never shells a compiler (pipe-deadlock + trust: a dropped-in
                // .csproj must not trigger compilation). Only use already-built dlls.
                if (needBuild)
                {
                    Console.Error.WriteLine($"[HotReload] Skipping {name}: no up-to-date built dll (run 'dotnet build' first; reload never builds).");
                    continue;
                }
                if (dll is not null && File.Exists(dll) && !outList.Contains(dll, StringComparer.OrdinalIgnoreCase) && !IsExcluded(dll))
                {
                    outList.Add(dll);
                    added++;
                    Console.Error.WriteLine($"[HotReload] Discovered game assembly {Path.GetFileName(dll)}");
                }
            }
        }
        return added;
    }

    private static int DiscoverNewPluginModules(AtherizSettings settings, List<string> outList)
    {
        int discovered = 0;
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var pd = Path.Combine(baseDir, "plugins");
            if (Directory.Exists(pd))
            {
                foreach (var f in Directory.GetFiles(pd, "*.dll"))
                {
                    if (IsExcluded(f)) continue;
                    if (!outList.Contains(f)) { outList.Add(f); discovered++; }
                }
            }
            var alt = Path.Combine(settings.SavePath, "plugins");
            if (Directory.Exists(alt))
            {
                foreach (var f in Directory.GetFiles(alt, "*.dll"))
                {
                    if (IsExcluded(f)) continue;
                    if (!outList.Contains(f)) { outList.Add(f); discovered++; }
                }
            }
            if (discovered > 0) Console.Error.WriteLine($"[HotReload] Discovered {discovered} new plugin module(s).");
        }
        catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Discover failed: {ex.Message}"); }
        return discovered;
    }
    // NOTE: the old PatchChannelsFirstAndRest + ReinitGlobalCmdSets no-op shells were
    // deleted 2026-09-07 — channels are patched via the replacement loop above and
    // cmdsets need no re-init (C# commands reflect new types by construction).
    public static async Task<string> ReloadGameLogicAsync(AsyncTicker ticker, AsyncThreadPool pool, AtherizSettings settings)
    {
        settings??=AtherizSettings.Global; ticker??=GlobalServices.GetAsyncTicker(); pool??=GlobalServices.GetAsyncThreadPool();
        if(!TryEnterGate()) return "Reload already in progress; skipping.";
        try{
            Console.Error.WriteLine("Server reload initiated.");
            int reloaded=0; List<string> errors = [];
            List<string> cands = [];
            DiscoverGameAssembly(settings,cands);
            DiscoverNewPluginModules(settings,cands);
            cands=cands.Where(p=>!IsExcluded(p)&&File.Exists(p)).Distinct().ToList();
            Console.Error.WriteLine($"[HotReload] Found {cands.Count} plugin assemblies.");
            foreach(var p in cands){ try{ if(await ReloadCoreAsync(p,ticker,pool).ConfigureAwait(false)) reloaded++; }catch(Exception ex){ var m=$"Failed {p}: {ex.Message}"; Console.Error.WriteLine($"[HotReload] {m}"); errors.Add(m);} }
            // No dead second pass (load-then-immediately-unload scanned nothing) and no
            // double patch: ReloadAsync already patched each assembly's replacements.
            if(cands.Count==0) try{ lock (StartStop.WorldLock) { ReregisterTicks(ticker); } }catch(Exception ex){errors.Add($"ReregisterTicks: {ex.Message}");}
            try{ MapEdit.ClearStale(); }catch(Exception ex){errors.Add($"MapEdit: {ex.Message}");}
            var res=$"Reloaded {reloaded} modules. Patched {ObjectRegistry.Count} objects. Errors: {errors.Count}";
            if(errors.Count>0) res+=$"\nFirst Error: {errors[0]}";
            Console.Error.WriteLine($"[HotReload] {res}");
            return res;
        }finally{ExitGate();}
    }
    public static Task<string> ReloadGameLogicAsync(AsyncTicker ticker, AtherizSettings settings)=>ReloadGameLogicAsync(ticker,GlobalServices.GetAsyncThreadPool(),settings);
    public static Task<string> ReloadGameLogicAsync(AtherizSettings settings)=>ReloadGameLogicAsync(GlobalServices.GetAsyncTicker(),GlobalServices.GetAsyncThreadPool(),settings);
    // Boot-time game load: same discover→load→patch as a hot reload, minus the
    // async reload orchestration (ticker reregistration, map-edit clearing),
    // which only makes sense for an already-running world. Runs synchronously;
    // the caller (StartStop.DoStartup) holds WorldLock, which is re-entrant.
    // Never throws: every failure is logged and boot continues engine-only.
    // Returns the patched live-object count (0 when no game assembly exists).
    public static int LoadGameAssembliesAtBoot(AtherizSettings? settings = null)
        => LoadGameAssembliesAtBoot(settings, out _);

    // Same load, additionally handing back the discovered game-setup entry
    // (last loaded wins, matching the old static slot) for explicit CLI
    // dispatch; null when no game assembly provides one.
    public static int LoadGameAssembliesAtBoot(AtherizSettings? settings, out IGameSetup? gameSetup)
    {
        gameSetup = null;
        settings ??= AtherizSettings.Global;
        if (!TryEnterGate()) { Console.Error.WriteLine("[Boot] Game load already in progress; skipping."); return 0; }
        try
        {
            List<string> cands = [];
            try { DiscoverGameAssembly(settings, cands); } catch (Exception ex) { Suppress("LoadGameAssembliesAtBoot.Discover", ex); }
            try { DiscoverNewPluginModules(settings, cands); } catch (Exception ex) { Suppress("LoadGameAssembliesAtBoot.Discover", ex); }
            cands = cands.Where(p => !IsExcluded(p) && File.Exists(p)).Distinct().ToList();
            if (cands.Count == 0)
            {
                Console.Error.WriteLine("[Boot] No game assembly discovered — running engine-only. Build the game project first (dotnet build) so its dll is discoverable next to the game folder.");
                return 0;
            }
            Console.Error.WriteLine($"[Boot] Found {cands.Count} game assemblies.");
            int patched = 0;
            foreach (var p in cands)
            {
                try
                {
                    var full = Path.GetFullPath(p);
                    if (_loader is not null) { try { _loader.Unload(); } catch (Exception logEx) { Suppress("LoadGameAssembliesAtBoot", logEx); } _loader = null; GC.Collect(); }
                    _loader = new PluginLoader();
                    try
                    {
                        var discovered = _loader.Load(full);
                        if (discovered is not null) gameSetup = discovered;
                    }
                    catch (Exception ex) { Console.Error.WriteLine($"[Boot] Load failed: {ex.Message}"); continue; }
                    Console.Error.WriteLine($"[Boot] Loaded {_loader.Replacements.Count} repl from {Path.GetFileName(full)}.");
                    lock (StartStop.WorldLock)
                    {
                        foreach (var kv in _loader.Replacements.ToList())
                        {
                            try { patched += PatchLiveObjects(kv.Key, kv.Value); }
                            catch (Exception ex) { Console.Error.WriteLine($"[Boot] Patch {kv.Key.Name}->{kv.Value.Name}: {ex.Message}"); }
                        }
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine($"[Boot] Failed {p}: {ex.Message}"); }
            }
            Console.Error.WriteLine($"[Boot] Game load patched {patched} of {ObjectRegistry.Count} objects.");
            return patched;
        }
        finally { ExitGate(); }
    }
}
