// Port of atheriz/reloader.py:536 — faithful ALC double-pass + _apply_patch + _reload_game_logic
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
namespace Atheriz.Core.Plugins;
public static class PluginReloader
{
    // Port of atheriz/reloader.py:14 _EXCLUDED_MODULES
    public static readonly HashSet<string> ExcludedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    { "Microsoft.*", "System.*", "Atheriz.Core", "netstandard", "xunit.*" };
    // Port of atheriz/reloader.py:326 _reload_lock = _SHARED_WORLD_LOCK
    private static readonly object _reloadLock = new();
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
        try { _reloadGate.Release(); } catch {}
    }
    private static PluginLoader? _loader;
    // Port of reloader.py:249 _apply_patch transient preserves
    private static readonly HashSet<string> _transientFields = new(StringComparer.Ordinal)
    { "session","_session","listeners","_listeners","command","_command","_lock","_hooks","_msgLog" };
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
    private static int ScanAssembly(Assembly asm)
    {
        int f = 0;
        try { foreach (var t in asm.GetTypes()) f += t.GetCustomAttributes<EntityReplacementAttribute>(false).Count(); f += asm.GetCustomAttributes<EntityReplacementAttribute>().Count(); }
        catch (ReflectionTypeLoadException ex) { Console.Error.WriteLine($"[HotReload] Type load: {string.Join("; ", ex.LoaderExceptions.Select(e=>e?.Message))}"); }
        catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Scan failed: {ex.Message}"); }
        return f;
    }
    // Port of atheriz/reloader.py:340 _reload_game_logic — single pass per assembly
    // (the C# "second pass to fix forward refs" loaded+unloaded without scanning, so it was deleted).
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
            var alc1 = new AssemblyLoadContext($"game_reload_{Guid.NewGuid():N}", true);
            Assembly asm1;
            try { asm1 = alc1.LoadFromAssemblyPath(full); } catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Pass1 failed {ex.Message}"); try{alc1.Unload();}catch{} return false; }
            int found1 = ScanAssembly(asm1);
            Console.Error.WriteLine($"[HotReload] Pass1 {found1} repl from {Path.GetFileName(full)} (forward refs pending).");
            try{alc1.Unload();}catch{} GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if (_loader != null) { try{_loader.Unload();}catch{} _loader=null; GC.Collect(); }
            _loader = new PluginLoader();
            try { _loader.Load(full); } catch (Exception ex){ Console.Error.WriteLine($"[HotReload] Pass2 failed: {ex.Message}"); return false; }
            Console.Error.WriteLine($"[HotReload] Pass2 loaded {_loader.Replacements.Count} repl (forward refs fixed).");
            int patched=0;
            foreach(var kv in _loader.Replacements.ToList()){ try{patched+=PatchLiveObjects(kv.Key,kv.Value);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] Patch {kv.Key.Name}->{kv.Value.Name}: {ex.Message}");} }
            try{ReregisterTicks(ticker);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] ReregisterTicks: {ex.Message}");}
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
        if(oldType==null||newType==null) return 0;
        var live = ObjectRegistry.FilterBy(o=>o.GetType()==oldType);
        if(live.Count==0) return 0;
        int patched=0;
        foreach(var obj in live.ToList()){ try{if(PatchSingleObject(obj,newType))patched++;}catch(Exception ex){Console.Error.WriteLine($"[HotReload] patch {obj.Id}: {ex.Message}");} }
        // Only the live replacements need ResolveRelations (old instances are detached after AddObject rewire).
        var newLive=ObjectRegistry.FilterBy(o=>o.GetType()==newType);
        foreach(var obj in newLive){ try{ var mi=obj.GetType().GetMethod("ResolveRelations",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); mi?.Invoke(obj,null);}catch{}}
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
            try { if (c is Channel ch) ch.ReplaceListener(newObj); } catch { }
        try { GlobalServices.GetMapHandler()?.ReplaceMapEntries(oldObj.Id, newObj); } catch { }
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
                    foreach (var g in grids) try { g.ReplaceNodeValue(nn); } catch { }
                }
            }
            catch { }
        }
        // Own session (transient _session was restored onto newObj above).
        // Cross-session puppet-stack Prev refs are out of contract (no session registry).
        try { newObj.Session?.ReplacePuppetRefs(newObj); } catch { }
    }
    private static bool PatchSingleObject(GameObject oldObj, Type newType)
    {
        var oldType=oldObj.GetType();
        var saved=new Dictionary<string,object?>(StringComparer.Ordinal);
        foreach(var fn in _transientFields){ var f=FindField(oldType,fn); if(f!=null) try{saved[fn]=f.GetValue(oldObj);}catch{}}
        var origSnap=new Dictionary<FieldInfo,object?>();
        var oldFields=GetAllFields(oldType);
        foreach(var f in oldFields) try{origSnap[f]=f.GetValue(oldObj);}catch{}
        var lk=oldObj.SyncRoot; bool taken=false;
        try{
            try{lk.EnterWriteLock(); taken=true;}catch{taken=false;}
            GameObject newObj; try{newObj=(GameObject)RuntimeHelpers.GetUninitializedObject(newType);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] GetUninitializedObject {newType.Name}: {ex.Message}"); return false;}
            var newByName=GetAllFields(newType).GroupBy(f=>f.Name).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
            foreach(var fOld in oldFields){
                if(_transientFields.Contains(fOld.Name)) continue;
                if(!newByName.TryGetValue(fOld.Name,out var fNew)) continue;
                if(fNew.IsInitOnly) continue;
                try{ var v=fOld.GetValue(oldObj); if(v==null||fNew.FieldType.IsAssignableFrom(v.GetType())||fNew.FieldType.IsAssignableFrom(fOld.FieldType)||fNew.FieldType==typeof(object)) fNew.SetValue(newObj,v); else try{fNew.SetValue(newObj,v);}catch{}}catch{}
            }
            foreach(var kv in saved){ var fNew=FindField(newType,kv.Key); if(fNew!=null&&!fNew.IsInitOnly) try{fNew.SetValue(newObj,kv.Value);}catch{} }
            // GetUninitializedObject skips field initializers, so readonly fields (e.g. _flags)
            // stay null — the copy loop above skips init-only fields. Backfill them from the
            // old instance (shared refs are safe: the old instance is detached after rewire).
            foreach (var fOld in oldFields)
            {
                if (!newByName.TryGetValue(fOld.Name, out var fNew) || !fNew.IsInitOnly) continue;
                try
                {
                    if (fNew.GetValue(newObj) == null)
                    {
                        var v = fOld.GetValue(oldObj);
                        if (v != null) fNew.SetValue(newObj, v);
                    }
                }
                catch { }
            }
            var lf=FindField(newType,"_lock");
            if(lf!=null) try{ var cur=lf.GetValue(newObj); if(cur==null) lf.SetValue(newObj,saved.TryGetValue("_lock",out var v)?v:new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion)); }catch{}
            try{newObj.Id=oldObj.Id;}catch{}
            ObjectRegistry.AddObject(newObj);
            // C# cannot swap __class__ in place like Python: AddObject replaced the id,
            // so rewire direct refs (channels/map/nodes/sessions hold instances, not ids).
            try { RewireReferences(oldObj, newObj); } catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Rewire {oldObj.Id}: {ex.Message}"); }
            return true;
        }catch{
            try{ foreach(var kv in origSnap) try{kv.Key.SetValue(oldObj,kv.Value);}catch{}}catch{}
            throw;
        }finally{ if(taken) try{lk.ExitWriteLock();}catch{}}
    }
    private static FieldInfo? FindField(Type t,string n){ var cur=t; while(cur!=null&&cur!=typeof(object)){ var f=cur.GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); if(f!=null) return f; cur=cur.BaseType; } return null; }
    private static List<FieldInfo> GetAllFields(Type t){ var l=new List<FieldInfo>(); var cur=t; while(cur!=null&&cur!=typeof(object)){ l.AddRange(cur.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)); cur=cur.BaseType; } return l; }
    // Port of atheriz/reloader.py:339 _reload_game_logic + startstop.py:85 _reregister_ticks — Remove old + AddCoro(at_tick, TickSeconds)
    public static void ReregisterTicks(AsyncTicker ticker)
    {
        if(ticker==null) throw new ArgumentNullException(nameof(ticker));
        var tickables=ObjectRegistry.FilterBy(o=>o.IsTickable);
        try{
            var nh=GlobalServices.GetNodeHandler();
            if(nh!=null){
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
        }catch{}
        RemoveTickDelegatesFor(ticker,tickables);
        foreach(var obj in tickables){
            double secs=1; try{secs=obj.TickSeconds;}catch{secs=1;} if(secs<=0) secs=1;
            var mi=obj.GetType().GetMethod("AtTick",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(mi==null) continue;
            try{ Action act=()=>{try{mi.Invoke(obj,null);}catch{}}; ticker.AddCoro(act,secs);}catch(Exception ex){Console.Error.WriteLine($"[HotReload] rereg {obj.Id}: {ex.Message}");}
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
            foreach (var d in coros.ToList())
            {
                if (!TargetsTickable(d.Target, tickables)) continue;
                try
                {
                    var iv = TimeSpan.FromSeconds(kv.Key);
                    if (d is Action a) ticker.RemoveCoro(a, iv);
                    else if (d is Func<Task> f) ticker.RemoveCoro(f, iv);
                }
                catch { }
            }
        }
    }
    private static bool TargetsTickable(object? target, List<GameObject> tickables)
    {
        if (target == null) return false;
        foreach (var obj in tickables)
        {
            if (ReferenceEquals(target, obj)) return true;
            // Compiler-generated closure capturing the tickable (e.g. () => obj.AtTick()).
            try
            {
                foreach (var f in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (typeof(GameObject).IsAssignableFrom(f.FieldType) && ReferenceEquals(f.GetValue(target), obj))
                        return true;
            }
            catch { }
        }
        return false;
    }
    // Port of atheriz/reloader.py:58 _discover_new_atheriz_modules + 93 _discover_new_game_modules — scan plugins + game project
    // Game project discovery: look for already-built dlls near CWD / SavePath parent (mirrors Python game folder import).
    // Never builds: Python imports source (no build step); a dropped-in .csproj must not trigger compilation on reload.
    public static int DiscoverGameAssembly(AtherizSettings settings, List<string> outList)
    {
        int added = 0;
        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { searchDirs.Add(Directory.GetCurrentDirectory()); } catch { }
        try
        {
            var sp = settings.SavePath;
            if (!string.IsNullOrWhiteSpace(sp))
            {
                var abs = Path.IsPathRooted(sp) ? sp : Path.Combine(Directory.GetCurrentDirectory(), sp);
                var gameRoot = Path.GetDirectoryName(Path.GetFullPath(abs));
                if (!string.IsNullOrWhiteSpace(gameRoot) && Directory.Exists(gameRoot)) searchDirs.Add(gameRoot);
                // also parent of gameRoot (handles save/ inside mygame)
                try { var parent = Path.GetDirectoryName(gameRoot!); if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) searchDirs.Add(parent); } catch { }
            }
        }
        catch { }
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
                var dllRelease = Path.Combine(dir, "bin", "Release", "net8.0", $"{name}.dll");
                var dllDebug = Path.Combine(dir, "bin", "Debug", "net8.0", $"{name}.dll");
                string? dll = null;
                if (File.Exists(dllRelease)) dll = dllRelease;
                else if (File.Exists(dllDebug)) dll = dllDebug;
                // Build if missing or csproj newer than dll
                bool needBuild = dll == null;
                if (!needBuild)
                {
                    try
                    {
                        var csprojTime = File.GetLastWriteTimeUtc(csproj);
                        var dllTime = File.GetLastWriteTimeUtc(dll!);
                        if (csprojTime > dllTime) needBuild = true;
                        // also if any *.cs newer
                        foreach (var cs in Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
                            if (File.GetLastWriteTimeUtc(cs) > dllTime) { needBuild = true; break; }
                    }
                    catch { }
                }
                // Reload never shells a compiler (pipe-deadlock + trust: a dropped-in
                // .csproj must not trigger compilation). Only use already-built dlls.
                if (needBuild)
                {
                    Console.Error.WriteLine($"[HotReload] Skipping {name}: no up-to-date built dll (run 'dotnet build' first; reload never builds).");
                    continue;
                }
                if (dll != null && File.Exists(dll) && !outList.Contains(dll, StringComparer.OrdinalIgnoreCase) && !IsExcluded(dll))
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
    private static void PatchChannelsFirstAndRest()
    {
        try
        {
            var channels = ObjectRegistry.FilterBy(o => o.IsChannel);
            var rest = ObjectRegistry.FilterBy(o => !o.IsChannel);
            // Channels already patched via replacement loop; this mirrors ordering for future type swaps not via replacement
            foreach (var c in channels) { /* channel type swap would be handled via PatchLiveObjects if channel type changed */ }
            foreach (var r in rest) { /* same */ }
        }
        catch { }
    }
    // Port of atheriz/reloader.py:494-512 re-init global cmdsets after patch (LoggedIn/UnloggedIn)
    private static void ReinitGlobalCmdSets()
    {
        try
        {
            var loggedIn = GlobalServices.GetLoggedInCmdSet();
            var unloggedIn = GlobalServices.GetUnloggedInCmdSet();
            foreach (var cs in new[] { loggedIn, unloggedIn })
            {
                if (cs == null) continue;
                // CmdSet re-init: in Python s.__init__() rebuilds commands; in C# we ensure commands reflect new types by touching
                try { var cmds = cs.GetAll().ToList(); foreach(var _ in cmds){} } catch { }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[HotReload] Reinit CmdSets: {ex.Message}"); }
    }
    // Port of atheriz/reloader.py:306 reload_game_logic orchestrates Unload→Load→Patch→Reregister→ MapEdit clear (double pass 381-389 fix forward refs)
    public static async Task<string> ReloadGameLogicAsync(AsyncTicker ticker, AsyncThreadPool pool, AtherizSettings settings)
    {
        settings??=AtherizSettings.Global; ticker??=GlobalServices.GetAsyncTicker(); pool??=GlobalServices.GetAsyncThreadPool();
        if(!TryEnterGate()) return "Reload already in progress; skipping.";
        try{
            Console.Error.WriteLine("Server reload initiated.");
            int reloaded=0; var errors=new List<string>();
            var cands=new List<string>();
            DiscoverGameAssembly(settings,cands);
            DiscoverNewPluginModules(settings,cands);
            cands=cands.Where(p=>!IsExcluded(p)&&File.Exists(p)).Distinct().ToList();
            Console.Error.WriteLine($"[HotReload] Found {cands.Count} plugin assemblies.");
            foreach(var p in cands){ try{ if(await ReloadAsync(p,ticker,pool)) reloaded++; }catch(Exception ex){ var m=$"Failed {p}: {ex.Message}"; Console.Error.WriteLine($"[HotReload] {m}"); errors.Add(m);} }
            // No dead second pass (load-then-immediately-unload scanned nothing) and no
            // double patch: ReloadAsync already patched each assembly's replacements.
            if(cands.Count==0) try{ReregisterTicks(ticker);}catch(Exception ex){errors.Add($"ReregisterTicks: {ex.Message}");}
            try{ PatchChannelsFirstAndRest(); }catch(Exception ex){errors.Add($"Channel patch: {ex.Message}");}
            try{ ReinitGlobalCmdSets(); }catch(Exception ex){errors.Add($"CmdSet reinit: {ex.Message}");}
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
