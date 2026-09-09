// Port of atheriz/reloader.py:536 — faithful ALC single-load + _apply_patch + _reload_game_logic
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
    // Port of atheriz/reloader.py:14 _EXCLUDED_MODULES
    public static readonly HashSet<string> ExcludedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    { "Microsoft.*", "System.*", "Atheriz.Core", "netstandard", "xunit.*" };
    // Port of atheriz/reloader.py:326 _reload_lock = _SHARED_WORLD_LOCK.
    // _reloadGate serializes concurrent *reloads* only (TryEnter = skip when busy,
    // pinned by Reloads_AreSerialized_MaxOverlapOne — NOT a bounded wait, so a stuck
    // reload can never wedge admin threads). It does NOT exclude world mutation:
    // that exclusion is StartStop.WorldLock, held around the patch phase below
    // (same outermost direction as DoShutdown: WorldLock → object/handler locks).
    private static readonly SemaphoreSlim _reloadGate = new(1,1);
    private static readonly System.Threading.AsyncLocal<int> _gateRecursion = new();
    private static bool TryEnterGate()
    {
        if ((_gateRecursion.Value) > 0) { _gateRecursion.Value++; return true; }
        if (!_reloadGate.Wait(0)) return false;
        _gateRecursion.Value = 1;
        return true;
    }
    private static void ExitGate()
    {
        if ((_gateRecursion.Value) > 1) { _gateRecursion.Value--; return; }
        _gateRecursion.Value = 0;
        try { _reloadGate.Release(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.ExitGate: " + logEx.Message, "PluginReloader"); }
    }
    private static PluginLoader? _loader;
    // Port of reloader.py:249 _apply_patch transient preserves. Bare
    // spellings (session/listeners/command) never matched a field — the port
    // renamed them _-prefixed — so only the resolving spellings are listed:
    // a rename that stops resolving is caught by the field-coverage test.
    private static readonly HashSet<string> _transientFields = new(StringComparer.Ordinal)
    { "_session","_listeners","_command","_lock","_hooks","_msgLog" };
    // Shared exclusion check (also used by PluginLoader): exact filename match only.
    // Never substring-match the full path — "MySystem.Game.dll" must not match "System.*".
    internal static bool IsExcludedAssembly(string p)
    {
        var n = Path.GetFileNameWithoutExtension(p) ?? "";
        foreach (var pat in ExcludedAssemblies)
        {
            if (pat.EndsWith(".*", StringComparison.Ordinal))
            { var pre = pat[..^2]; if (n.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return true; }
            else if (string.Equals(n, pat, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    private static bool IsExcluded(string p) => IsExcludedAssembly(p);
    // Port of atheriz/reloader.py:340 _reload_game_logic — single load per assembly.
    // (The old pass1 scan-ALC existed only to count forward refs, then unloaded with
    // a stop-the-world triple GC while the gate was held; PluginLoader.Load scans
    // identically, so pass1 was deleted — one ALC, one load, one post-unload GC.)
    public static async Task<bool> ReloadAsync(string assemblyPath, AsyncTicker ticker, AsyncThreadPool pool)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath)) return false;
        if (IsExcluded(assemblyPath)) { Console.Error.WriteLine($"[PluginReloader] Skipping excluded: {assemblyPath}"); return false; }
        if (!TryEnterGate()) { Console.Error.WriteLine("[HotReload] Reload already in progress; skipping."); return false; }
        try
        {
            await Task.Yield();
            var full = Path.GetFullPath(assemblyPath);
            if (!File.Exists(full)) { Console.Error.WriteLine($"[HotReload] Not found: {full}"); return false; }
            if (_loader is not null) { try{_loader.Unload();}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.ReloadAsync: " + logEx.Message, "PluginReloader"); } _loader=null; GC.Collect(); }
            _loader = new PluginLoader();
            try { _loader.Load(full); } catch (Exception ex){ Console.Error.WriteLine($"[HotReload] Load failed: {ex.Message}"); return false; }
            Console.Error.WriteLine($"[HotReload] Loaded {_loader.Replacements.Count} repl from {Path.GetFileName(full)}.");
            int patched=0;
            // Patch under the shared world lock (port of _SHARED_WORLD_LOCK): patch
            // takes per-object write + handler read locks, same outermost direction
            // as DoShutdown, so no ABBA. Load/scan stay outside (I/O, no locks held).
            lock (StartStop.WorldLock)
            {
                foreach(var kv in _loader.Replacements.ToList()){ try{patched+=PatchLiveObjects(kv.Key,kv.Value);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] Patch {kv.Key.Name}->{kv.Value.Name}: {ex.Message}");} }
                try{ReregisterTicks(ticker);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] ReregisterTicks: {ex.Message}");}
            }
            Console.Error.WriteLine($"[HotReload] ReloadAsync patched {patched}.");
            return true;
        } finally { ExitGate(); }
    }
    // Port of atheriz/reloader.py:249 _apply_patch preserves dict, skips __init__ side effects.
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
        foreach(var obj in newLive){ try{ obj.ResolveRelations(); }catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchLiveObjects: " + logEx.Message, "PluginReloader"); }}
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
            try { if (c is Channel ch) ch.ReplaceListener(newObj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.RewireReferences: " + logEx.Message, "PluginReloader"); }
        try { GlobalServices.GetMapHandler()?.ReplaceMapEntries(oldObj.Id, newObj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.RewireReferences: " + logEx.Message, "PluginReloader"); }
        if (newObj is Node nn)
        {
            try
            {
                var nh = GlobalServices.GetNodeHandler();
                List<NodeArea> areas;
                nh.Lock.EnterReadLock();
                try { areas = nh.GetAreas(); } finally { nh.Lock.ExitReadLock(); }
                foreach (var area in areas)
                {
                    List<NodeGrid> grids;
                    area.Lock.EnterReadLock();
                    try { grids = area.Grids.Values.ToList(); } finally { area.Lock.ExitReadLock(); }
                    foreach (var g in grids) try { g.ReplaceNodeValue(nn); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.RewireReferences: " + logEx.Message, "PluginReloader"); }
                }
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.RewireReferences: " + logEx.Message, "PluginReloader"); }
        }
        // Own session (transient _session was restored onto newObj above).
        // Cross-session puppet-stack Prev refs are out of contract (no session registry).
        try { newObj.Session?.ReplacePuppetRefs(newObj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.RewireReferences: " + logEx.Message, "PluginReloader"); }
    }
    /// <summary>
    /// Builds the replacement WITHOUT running any constructor, by design (mirrors
    /// Python <c>_apply_patch</c> skipping <c>__init__</c> side effects): field
    /// initializers do not run either, so every field is copied from the old
    /// instance — plain fields by assignability, transient session/lock state from
    /// the saved snapshot, and <c>readonly</c>/<c>init-only</c> fields backfilled by
    /// shared reference (safe: the old instance is detached after rewire). A field
    /// rename on either side silently skips that field (per-field LogDebug).
    /// Callers must hold <c>StartStop.WorldLock</c> (see <c>ReloadAsync</c>).
    /// </summary>
    private static bool PatchSingleObject(GameObject oldObj, Type newType)
    {
        var oldType=oldObj.GetType();
        var saved=new Dictionary<string,object?>(StringComparer.Ordinal);
        foreach(var fn in _transientFields){ var f=FindField(oldType,fn); if(f is not null) try{saved[fn]=f.GetValue(oldObj);}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }}
        Dictionary<FieldInfo,object?> origSnap = [];
        var oldFields=GetAllFields(oldType);
        foreach(var f in oldFields) try{origSnap[f]=f.GetValue(oldObj);}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }
        var lk=oldObj.SyncRoot; bool taken=false;
        try{
            try{lk.EnterWriteLock(); taken=true;}catch{taken=false;}
            GameObject newObj; try{newObj=(GameObject)RuntimeHelpers.GetUninitializedObject(newType);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] GetUninitializedObject {newType.Name}: {ex.Message}"); return false;}
            var newByName=GetAllFields(newType).GroupBy(f=>f.Name).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
            foreach(var fOld in oldFields){
                if(_transientFields.Contains(fOld.Name)) continue;
                if(!newByName.TryGetValue(fOld.Name,out var fNew)) continue;
                if(fNew.IsInitOnly) continue;
                try{ var v=fOld.GetValue(oldObj); if(v is null||fNew.FieldType.IsAssignableFrom(v.GetType())||fNew.FieldType.IsAssignableFrom(fOld.FieldType)||fNew.FieldType==typeof(object)) fNew.SetValue(newObj,v); else try{fNew.SetValue(newObj,v);}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }
            }
            foreach(var kv in saved){ var fNew=FindField(newType,kv.Key); if(fNew is not null&&!fNew.IsInitOnly) try{fNew.SetValue(newObj,kv.Value);}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); } }
            // GetUninitializedObject skips field initializers, so readonly fields (e.g. _flags)
            // stay null — the copy loop above skips init-only fields. Backfill them from the
            // old instance (shared refs are safe: the old instance is detached after rewire).
            foreach (var fOld in oldFields)
            {
                if (!newByName.TryGetValue(fOld.Name, out var fNew) || !fNew.IsInitOnly) continue;
                try
                {
                    if (fNew.GetValue(newObj) is null)
                    {
                        var v = fOld.GetValue(oldObj);
                        if (v is not null) fNew.SetValue(newObj, v);
                    }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }
            }
            var lf=FindField(newType,"_lock");
            if(lf is not null) try{ var cur=lf.GetValue(newObj); if(cur is null) lf.SetValue(newObj,saved.TryGetValue("_lock",out var v)?v:new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion)); }catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }
            try{newObj.Id=oldObj.Id;}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }
            // release the old-object write lock BEFORE AddObject +
            // RewireReferences (which take channel/map/area/grid/session
            // locks). Holding it across inverted the registry→object order
            // of FilterBy while ReloadAsync holds WorldLock.
            if(taken) try{lk.ExitWriteLock(); taken=false;}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }
            ObjectRegistry.AddObject(newObj);
            // C# cannot swap __class__ in place like Python: AddObject replaced the id,
            // so rewire direct refs (channels/map/nodes/sessions hold instances, not ids).
            try { RewireReferences(oldObj, newObj); } catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Rewire {oldObj.Id}: {ex.Message}"); }
            return true;
        }catch{
            try{ foreach(var kv in origSnap) try{kv.Key.SetValue(oldObj,kv.Value);}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }
            throw;
        }finally{ if(taken) try{lk.ExitWriteLock();}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.PatchSingleObject: " + logEx.Message, "PluginReloader"); }}
    }
    private static FieldInfo? FindField(Type t,string n){ var cur=t; while(cur is not null&&cur!=typeof(object)){ var f=cur.GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); if(f is not null) return f; cur=cur.BaseType; } return null; }
    private static List<FieldInfo> GetAllFields(Type t){ List<FieldInfo> l = []; var cur=t; while(cur is not null&&cur!=typeof(object)){ l.AddRange(cur.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)); cur=cur.BaseType; } return l; }
    // Port of atheriz/reloader.py:339 _reload_game_logic + startstop.py:85 _reregister_ticks — Remove old + AddCoro(at_tick, TickSeconds)
    public static void ReregisterTicks(AsyncTicker ticker)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        var tickables=ObjectRegistry.FilterBy(o=>o.IsTickable);
        try{
            var nh=GlobalServices.GetNodeHandler();
            if(nh is not null){
                nh.Lock.EnterReadLock(); List<NodeArea> areas;
                try{areas=nh.GetAreas();}finally{nh.Lock.ExitReadLock();}
                foreach(var area in areas){
                    area.Lock.EnterReadLock(); List<NodeGrid> grids;
                    try{grids=area.Grids.Values.ToList();}finally{area.Lock.ExitReadLock();}
                    foreach(var grid in grids){
                        grid.Lock.EnterReadLock(); List<Node> nodes;
                        try{nodes=grid.Nodes.Values.Where(n=>n.IsTickable).ToList();}finally{grid.Lock.ExitReadLock();}
                        foreach(var n in nodes) if(!tickables.Contains(n)) tickables.Add(n);
                    }
                }
            }
        }catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.ReregisterTicks: " + logEx.Message, "PluginReloader"); }
        RemoveTickDelegatesFor(ticker,tickables);
        foreach(var obj in tickables){
            double secs=1; try{secs=obj.TickSeconds;}catch{secs=1;} if(secs<=0) secs=1;
            // direct virtual dispatch — every GameObject
            // carries AtTick, so the old reflective tick-method lookup plus
            // Invoke was pure overhead with identical dispatch (all overrides,
            // no `new` shadows). No behavior change.
            try{ Action act=()=>{try{obj.AtTick();}catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.ReregisterTicks: " + logEx.Message, "PluginReloader"); }}; ticker.AddCoro(act,secs);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] rereg {obj.Id}: {ex.Message}");}
        }
    }
    private static void RemoveTickDelegatesFor(AsyncTicker ticker, List<GameObject> tickables)
    {
        // Public ticker surface only (Slots/Coros/RemoveCoro) — never poke _slots/_coros privates (breaks on rename).
        IReadOnlyDictionary<double, AsyncTicker.TimeSlot> slots;
        try { slots = ticker.Slots; }
        catch (Exception ex) { Console.Error.WriteLine($"[HotReload] RemoveTickDelegates: {ex.Message}"); return; }
        foreach (var kv in slots.ToList())
        {
            IReadOnlySet<Delegate> coros;
            try { coros = kv.Value.Coros; } catch { continue; }
            // One id set per sweep, not per delegate: rebuilding it inside
            // the per-delegate walk is O(tickables x delegates).
            HashSet<int> tickableIds = [];
            foreach (var obj in tickables) tickableIds.Add(obj.Id);
            foreach (var d in coros.ToList())
            {
                if (!TargetsTickable(d.Target, tickableIds)) continue;
                try
                {
                    var iv = TimeSpan.FromSeconds(kv.Key);
                    if (d is Action a) ticker.RemoveCoro(a, iv);
                    else if (d is Func<Task> f) ticker.RemoveCoro(f, iv);
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.RemoveTickDelegatesFor: " + logEx.Message, "PluginReloader"); }
            }
        }
    }
    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        public static readonly IdentityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static bool TargetsTickable(object? target, HashSet<int> tickableIds)
    {
        if (target is null) return false;
        // Same instance shares the id, and a pre-patch delegate targets the
        // OLD instance, which shares the replacement's registry id but never
        // the reference — one id check covers both.
        if (target is GameObject self && tickableIds.Contains(self.Id)) return true;
        // Id match : a pre-patch delegate targets the OLD instance,
        // which shares the replacement's registry id but never the reference.
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
                if (cur is GameObject go)
                {
                    if (tickableIds.Contains(go.Id)) return true;
                    continue; // never walk live game objects' fields
                }
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
    // Port of atheriz/reloader.py:58 _discover_new_atheriz_modules + 93 _discover_new_game_modules — scan plugins + game project
    // Game project discovery: look for already-built dlls near CWD / SavePath parent (mirrors Python game folder import).
    // Never builds: Python imports source (no build step); a dropped-in .csproj must not trigger compilation on reload.
    public static int DiscoverGameAssembly(AtherizSettings settings, List<string> outList)
    {
        int added = 0;
        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { searchDirs.Add(Directory.GetCurrentDirectory()); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.DiscoverGameAssembly: " + logEx.Message, "PluginReloader"); }
        try
        {
            var sp = settings.SavePath;
            if (!string.IsNullOrWhiteSpace(sp))
            {
                var abs = Path.IsPathRooted(sp) ? sp : Path.Combine(Directory.GetCurrentDirectory(), sp);
                var gameRoot = Path.GetDirectoryName(Path.GetFullPath(abs));
                if (!string.IsNullOrWhiteSpace(gameRoot) && Directory.Exists(gameRoot)) searchDirs.Add(gameRoot);
                // also parent of gameRoot (handles save/ inside mygame)
                try { var parent = Path.GetDirectoryName(gameRoot!); if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) searchDirs.Add(parent); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.DiscoverGameAssembly: " + logEx.Message, "PluginReloader"); }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.DiscoverGameAssembly: " + logEx.Message, "PluginReloader"); }
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
                        // also if any *.cs newer (recursive: subdirectory
                        // edits must not look "up to date")
                        foreach (var cs in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                            if (File.GetLastWriteTimeUtc(cs) > dllTime) { needBuild = true; break; }
                    }
                    catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed PluginReloader.DiscoverGameAssembly: " + logEx.Message, "PluginReloader"); }
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
    // Port of atheriz/reloader.py:404 _patch_object channel-first then rest + 430 cmdset patch + 518 resolve_relations
    // NOTE: the old PatchChannelsFirstAndRest + ReinitGlobalCmdSets no-op shells were
    // deleted 2026-09-07 — channels are patched via the replacement loop above and
    // cmdsets need no re-init (C# commands reflect new types by construction).
    // Port of atheriz/reloader.py:306 reload_game_logic orchestrates Unload→Load→Patch→Reregister→ MapEdit clear
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
            foreach(var p in cands){ try{ if(await ReloadAsync(p,ticker,pool)) reloaded++; }catch(Exception ex){ var m=$"Failed {p}: {ex.Message}"; Console.Error.WriteLine($"[HotReload] {m}"); errors.Add(m);} }
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
}
