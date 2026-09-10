using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

/// <summary>
/// Faithful port of <c>atheriz/globals/node.py:NodeHandler</c>.
/// Three separate locks mirroring Python's lock/lock2/lock3, dirty flags per table,
/// JSON persistence (replaces dill), and id collision handling via <see cref="ObjectRegistry"/>.
/// </summary>
public partial class NodeHandler
{
    // Private locks with SupportsRecursion for re-entrant paths (AddArea inside WriteLock, Reregister nested reads)
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);   // areas
    private readonly ReaderWriterLockSlim _lock2 = new(LockRecursionPolicy.SupportsRecursion); // transitions
    private readonly ReaderWriterLockSlim _lock3 = new(LockRecursionPolicy.SupportsRecursion); // doors
    public ReaderWriterLockSlim SyncRoot => _lock;
    public ReaderWriterLockSlim SyncRoot2 => _lock2;
    public ReaderWriterLockSlim SyncRoot3 => _lock3;
    // Compat: keep public Lock/Lock2/Lock3 for Ported tests (now delegates to private _lock*); new code should use SyncRoot/ReadScope/WriteScope
    public ReaderWriterLockSlim Lock => _lock;
    public ReaderWriterLockSlim Lock2 => _lock2;
    public ReaderWriterLockSlim Lock3 => _lock3;
    public IDisposable ReadScope() { _lock.EnterReadLock(); return new LockScope(_lock, false); }
    public IDisposable WriteScope() { _lock.EnterWriteLock(); return new LockScope(_lock, true); }
    public IDisposable ReadScope2() { _lock2.EnterReadLock(); return new LockScope(_lock2, false); }
    public IDisposable WriteScope2() { _lock2.EnterWriteLock(); return new LockScope(_lock2, true); }
    public IDisposable ReadScope3() { _lock3.EnterReadLock(); return new LockScope(_lock3, false); }
    public IDisposable WriteScope3() { _lock3.EnterWriteLock(); return new LockScope(_lock3, true); }
    private readonly Dictionary<string, NodeArea> _areas = new();
    private readonly Dictionary<(Coord From, Coord To), Transition> _transitions = new();
    private readonly Dictionary<Coord, Dictionary<string, Door>> _doors = new();
    private bool _modified, _modified2, _modified3;
    // Port of node.py:42-43 _trans_gen/_door_gen: mutation counters so Save
    // only clears _modified2/_modified3 when nothing changed since the snapshot.
    private long _transGen, _doorGen, _areaGen;
    // Tombstones (MapHandler parity): keys removed since the last successful
    // save. RemoveArea/RemoveTransition drop their rows, but Save otherwise
    // only upserts — without deletes a removed key resurrects on the next
    // process Load. RemoveDoor needs none (doors live as values inside the
    // per-coord dict row, which Save rewrites wholesale); Remap* records the
    // relocated-from keys instead. Guarded by the domain lock each set lives
    // under (Lock / Lock2 / Lock3).
    private readonly HashSet<string> _removedAreas = new();
    private readonly HashSet<(Coord From, Coord To)> _removedTrans = new();
    private readonly HashSet<Coord> _removedDoors = new();

    private static NodeHandler? _current;
    private static readonly Lock _currentLock = new();
    public static NodeHandler? GetCurrent() { lock (_currentLock) return _current; }
    public static void SetCurrent(NodeHandler? h) { lock (_currentLock) _current = h; }
    internal void MarkDoorsModified() { Lock3.EnterWriteLock(); try { _modified3 = true; _doorGen++; } finally { Lock3.ExitWriteLock(); } }
    internal void MarkTransitionsModified() { Lock2.EnterWriteLock(); try { _modified2 = true; _transGen++; } finally { Lock2.ExitWriteLock(); } }

    // O(1) coord→node index lookup through the area/grid/(x,y) dicts.
    // Snapshot-at-each-level (same pattern as Load): handler lock released
    // before the area/grid locks are taken, so no lock nesting.
    public Node? TryGetNodeAt(Coord coord)
    {
        NodeArea? area;
        Lock.EnterReadLock();
        try { _areas.TryGetValue(coord.Area, out area); }
        finally { Lock.ExitReadLock(); }
        if (area is null) return null;
        var grid = area.GetGrid(coord.Z);
        if (grid is null) return null;
        grid.Lock.EnterReadLock();
        try { return grid.Nodes.TryGetValue((coord.X, coord.Y), out var n) ? n : null; }
        finally { grid.Lock.ExitReadLock(); }
    }

    // ctors never touch the process-global current — constructing a
    // helper handler must not hijack it. Owners publish via SetCurrent.
    public NodeHandler() { Load(); }
    public NodeHandler(bool autoLoad) { if (autoLoad) Load(); }
    // Settings-pinned load: boots from settings.SavePath instead of the ambient
    // factory path, so DoStartup(settings) uses one database everywhere .
    public NodeHandler(AtherizSettings? settings, bool autoLoad = true)
    {
        if (autoLoad)
        {
            if (settings is null) Load();
            else { using var db = new AtherizDbContext(settings); Load(db); }
        }
    }

    // --- load ---
    public void Load() => Load(global::Atheriz.Core.Persistence.AtherizDbContextFactory.Create());

    public void Load(AtherizDbContext db)
    {
        try
        {
            db.Database.EnsureCreated();
            // Lock order handler -> registry: evictions are collected under the
            // handler lock and applied after release (LoadInto holds Lock while
            // invoking this callback; RemoveObject takes the registry AllLock).
            List<Node> loadEvict = [];
            JsonTableLoader.LoadInto(db.Areas, Lock, json => JsonSerializer.Deserialize<NodeAreaDto>(json, JsonOptions.Default), (dto, row) =>
            {
                // Per-row report: corrupt areas are skipped, never silent.
                // (LoadInto still swallows after we log, preserving flow.)
                try
                {
                    var na = dto!.ToDomain();
                    // Evict the replaced area's nodes from the registry: otherwise
                    // they leak as stale ids while the handler no longer owns them.
                    // Mirrors RemoveArea/Clear eviction below. Live-modified nodes
                    // are grafted into the replacement grid instead: a stale row
                    // must not clobber newer in-memory edits.
                    if (_areas.TryGetValue(na.Name, out var old) && !ReferenceEquals(old, na))
                    {
                        foreach (var g in old.Grids.Values)
                            foreach (var n in g.Nodes.Values)
                            {
                                bool grafted = false;
                                try
                                {
                                    if (n.IsModified)
                                    {
                                        var ng = na.GetGrid(n.Coord.Z);
                                        // graft under the target
                                        // grid's write scope (leaf lock;
                                        // handler Lock is already held).
                                        if (ng is not null)
                                        {
                                            ng.Lock.EnterWriteLock();
                                            try
                                            {
                                                if (ng.Nodes.ContainsKey((n.Coord.X, n.Coord.Y)))
                                                { ng.Nodes[(n.Coord.X, n.Coord.Y)] = n; grafted = true; }
                                                else
                                                {
                                                    // Hole in an existing grid: the fresh row
                                                    // expresses no opinion about this cell, so the
                                                    // live-modified node survives by re-insertion
                                                    // (owner decision 2026-09-08 — evicting here
                                                    // destroyed newer in-memory edits). It stays
                                                    // IsModified, so the next save persists it.
                                                    ng.Nodes[(n.Coord.X, n.Coord.Y)] = n; grafted = true;
                                                }
                                            }
                                            finally { ng.Lock.ExitWriteLock(); }
                                        }
                                        else
                                        {
                                            // The fresh row has no Z grid at all — the same hole one
                                            // level up. Materialize the grid on the replacement
                                            // area and re-insert rather than evicting the
                                            // live-modified node.
                                            // `na` is still local (published below), so no
                                            // lock beyond the fresh grid's own is needed.
                                            var fresh = new NodeGrid(na.Name, n.Coord.Z);
                                            na.AddGrid(fresh);
                                            fresh.Lock.EnterWriteLock();
                                            try
                                            {
                                                fresh.Nodes[(n.Coord.X, n.Coord.Y)] = n;
                                                fresh.IsModified = true;
                                                grafted = true;
                                            }
                                            finally { fresh.Lock.ExitWriteLock(); }
                                        }
                                    }
                                }
                                catch { grafted = false; }
                                if (!grafted)
                                    loadEvict.Add(n);
                            }
                    }
                    _areas[na.Name] = na;
                    // A re-loaded row is live again: drop any tombstone so a
                    // later Save does not delete the resurrected row.
                    _removedAreas.Remove(na.Name);
                }
                catch (Exception ex)
                {
                    try { AtherizLogger.LogWarning($"[Load] skipping corrupt area row {row.Name}: {ex.GetType().Name}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
                    throw;
                }
            });
            foreach (var n in loadEvict)
                try { ObjectRegistry.RemoveObject(n); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
            JsonTableLoader.LoadInto(db.Transitions, Lock2, json => JsonSerializer.Deserialize<Transition>(json, JsonOptions.Default), (dto, row) =>
            {
                _transitions[(dto.FromCoord, dto.ToCoord)] = dto;
                _removedTrans.Remove((dto.FromCoord, dto.ToCoord));
            });
            JsonTableLoader.LoadInto(db.Doors, Lock3, json => JsonSerializer.Deserialize<Dictionary<string, Door>>(json, JsonOptions.Default), (dto, row) =>
            {
                var key = new Coord(row.Area, row.X, row.Y, row.Z);
                _doors[key] = dto;
                _removedDoors.Remove(key);
            });
            // Evict rows deleted from the DB (full-table load): absent keys must
            // not resurrect. Raw row keys (not successful deserializations)
            // distinguish deletion (DB empty) from corruption (DB has rows but
            // all failed) — corrupt preserves live.
            try
            {
                HashSet<string>? dbAreaNames = null;
                try { dbAreaNames = new HashSet<string>(db.Areas.AsNoTracking().Select(r => r.Name).ToList()); } catch { dbAreaNames = null; }
                if (dbAreaNames is not null)
                {
                    // Compute + remove under ONE write hold: the old split
                    // (collect, release, per-name remove) let a concurrent
                    // AddArea(name) land between collect and remove and lose
                    // the new area. Removed areas are evicted after release
                    // (RemoveObject takes the registry AllLock).
                    List<(string name, NodeArea removed)> evictedAreas;
                    Lock.EnterWriteLock();
                    try
                    {
                        evictedAreas = [];
                        foreach (var k in _areas.Keys.Where(k => !dbAreaNames.Contains(k)).ToList())
                            if (_areas.Remove(k, out var a) && a is not null) evictedAreas.Add((k, a));
                    }
                    finally { Lock.ExitWriteLock(); }
                    foreach (var (name, removed) in evictedAreas)
                    {
                        foreach (var g in removed.Grids.Values)
                            foreach (var n in g.Nodes.Values.ToList())
                                try { ObjectRegistry.RemoveObject(n); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
                        try { AtherizLogger.LogWarning($"[Load] evicting deleted area {name}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
                    }
                }
                HashSet<(string FA, int FX, int FY, int FZ, string TA, int TX, int TY, int TZ)>? dbTransKeys = null;
                try { dbTransKeys = new HashSet<(string FA, int FX, int FY, int FZ, string TA, int TX, int TY, int TZ)>(db.Transitions.AsNoTracking().Select(r => new { r.FromArea, r.FromX, r.FromY, r.FromZ, r.ToArea, r.ToX, r.ToY, r.ToZ }).ToList().Select(a => (FA: a.FromArea, FX: a.FromX, FY: a.FromY, FZ: a.FromZ, TA: a.ToArea, TX: a.ToX, TY: a.ToY, TZ: a.ToZ)).ToList()); } catch { dbTransKeys = null; }
                if (dbTransKeys is not null)
                {
                    Lock2.EnterWriteLock();
                    try
                    {
                        var tRem = _transitions.Keys.Where(k => !dbTransKeys.Contains((k.From.Area, k.From.X, k.From.Y, k.From.Z, k.To.Area, k.To.X, k.To.Y, k.To.Z))).ToList();
                        foreach (var k in tRem) _transitions.Remove(k);
                    }
                    finally { Lock2.ExitWriteLock(); }
                }
                HashSet<(string, int, int, int)>? dbDoorKeys = null;
                try { dbDoorKeys = new HashSet<(string, int, int, int)>(db.Doors.AsNoTracking().Select(r => new ValueTuple<string, int, int, int>(r.Area, r.X, r.Y, r.Z)).ToList()); } catch { dbDoorKeys = null; }
                if (dbDoorKeys is not null)
                {
                    Lock3.EnterWriteLock();
                    try
                    {
                        var dRem = _doors.Keys.Where(k => !dbDoorKeys.Contains((k.Area, k.X, k.Y, k.Z))).ToList();
                        foreach (var k in dRem) _doors.Remove(k);
                    }
                    finally { Lock3.ExitWriteLock(); }
                }
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
        }
        catch (Exception ex)
        {
            // Load-swallow: a failed load keeps live state, but never silently.
            try { AtherizLogger.LogWarning($"[Load] node load failed, keeping live state: {ex.GetType().Name} {ex.Message}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
            return;
        }

        int maxNodeId = 0;
        List<NodeArea> areasSnap;
        Lock.EnterReadLock();
        try { areasSnap = _areas.Values.ToList(); }
        finally { Lock.ExitReadLock(); }
        foreach (var area in areasSnap)
        {
            area.Lock.EnterReadLock();
            List<NodeGrid> grids;
            try { grids = area.Grids.Values.ToList(); }
            finally { area.Lock.ExitReadLock(); }
            foreach (var grid in grids)
            {
                grid.Lock.EnterReadLock();
                List<Node> nodes;
                try { nodes = grid.Nodes.Values.ToList(); }
                finally { grid.Lock.ExitReadLock(); }
                foreach (var node in nodes)
                {
                    if (node.Id > maxNodeId) maxNodeId = node.Id;
                    var existing = ObjectRegistry.Get(node.Id);
                    if (existing.Count > 0 && !ReferenceEquals(existing[0], node))
                    {
                        bool liveModified = false;
                        try { liveModified = existing[0].IsModified; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
                        if (liveModified)
                        {
                            try { AtherizLogger.LogWarning($"[Load] skipping stale node row {node.Id} (live modified)"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
                            continue;
                        }
                    }
                    ObjectRegistry.AddObject(node);
                    // Port of node.py:84-86 node.resolve_relations() — reinstall script hooks, ticker, at_init
                    try { node.ResolveRelations(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Load: " + logEx.Message, "NodeHandler"); }
                }
            }
        }
        // unconditional max-merge. The old nonzero-only guard
        // skipped the watermark when the DB held exactly node Id==0, so the
        // next alloc reused 0 and overwrote the loaded node.
        lock (IdGenerator.LockObj)
        {
            if (maxNodeId > IdGenerator.GetId())
                IdGenerator.SetId(maxNodeId);
        }
    }

    // Read-lock scan for a dirty area subtree (area/grid/node flags).
    // Flags do not bubble up (a node edit sets only the node flag), so a
    // clean area flag alone never proves a clean subtree — walk it all.
    private static bool AreaSubtreeDirty(NodeArea a)
    {
        a.Lock.EnterReadLock();
        List<NodeGrid> grids;
        bool areaDirty;
        try { areaDirty = a.IsModified; grids = a.Grids.Values.ToList(); }
        finally { a.Lock.ExitReadLock(); }
        if (areaDirty) return true;
        foreach (var g in grids)
        {
            g.Lock.EnterReadLock();
            List<Node> nodes;
            bool gridDirty;
            try { gridDirty = g.IsModified; nodes = g.Nodes.Values.ToList(); }
            finally { g.Lock.ExitReadLock(); }
            if (gridDirty) return true;
            foreach (var n in nodes)
            {
                // Node.IsModified is via GameObject flag (read lock internally)
                if (n.IsModified) return true;
            }
        }
        return false;
    }

    private bool IsDirty()
    {
        List<NodeArea> areas;
        using (ReadScope())
        {
            if (_modified) return true;
            areas = _areas.Values.ToList();
        }
        foreach (var a in areas) if (AreaSubtreeDirty(a)) return true;
        Lock2.EnterReadLock();
        try { if (_modified2) return true; }
        finally { Lock2.ExitReadLock(); }
        Lock3.EnterReadLock();
        try { if (_modified3) return true; }
        finally { Lock3.ExitReadLock(); }
        return false;
    }

    /// <summary>
    /// Serializes one save snapshot DTO. Virtual so game code can customize
    /// snapshot encoding (e.g. compression); the default is plain JSON.
    /// Runs outside the DB gate — implementations must not take DbWriteGate.
    /// </summary>
    protected virtual string SerializeSnapshot(object dto) => JsonSerializer.Serialize(dto, JsonOptions.Default);

    public virtual void Save(bool force = false)
    {
        if (!force && !ObjectRegistry.AlwaysSaveAll && !IsDirty()) return;
        try { Save(global::Atheriz.Core.Persistence.AtherizDbContextFactory.Create(), force); }
        catch (Exception ex)
        {
            // Closed-DB guard is the IsClosed flag (no message sniffing):
            // anything else propagates. One catch covers
            // InvalidOperationException too (it derives from Exception).
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping node save: {ex.Message}");
            return;
        }
    }

    // Restores the transient save flags cleared for persistence.
    private void RestoreSaveFlags(bool handlerWas, bool transWas, bool doorsWas, List<NodeArea> clearedAreas, List<NodeGrid> clearedGrids, List<Node> clearedNodes)
    {
        if (handlerWas) { Lock.EnterWriteLock(); try { _modified = true; } finally { Lock.ExitWriteLock(); } }
        if (transWas) { Lock2.EnterWriteLock(); try { _modified2 = true; } finally { Lock2.ExitWriteLock(); } }
        if (doorsWas) { Lock3.EnterWriteLock(); try { _modified3 = true; } finally { Lock3.ExitWriteLock(); } }
        foreach (var a in clearedAreas) { a.Lock.EnterWriteLock(); try { a.IsModified = true; } finally { a.Lock.ExitWriteLock(); } }
        foreach (var g in clearedGrids) { g.Lock.EnterWriteLock(); try { g.IsModified = true; } finally { g.Lock.ExitWriteLock(); } }
        foreach (var n in clearedNodes) n.IsModified = true;
    }

    public virtual void Save(AtherizDbContext db, bool force = false)
    {
        // Snapshot refs (using scoped helpers)
        List<NodeArea> areaRefs;
        bool handlerWas;
        long areaGen0;
        HashSet<string> areaDeletes;
        using (ReadScope()) { areaRefs = _areas.Values.ToList(); handlerWas = _modified; areaGen0 = _areaGen; areaDeletes = new HashSet<string>(_removedAreas); areaDeletes.ExceptWith(_areas.Keys); }
        List<Transition> transRefs = [];
        bool transWas;
        long transGen0;
        HashSet<(Coord From, Coord To)> transDeletes;
        using (ReadScope2()) { transRefs = _transitions.Values.ToList(); transWas = _modified2; transGen0 = _transGen; transDeletes = new HashSet<(Coord From, Coord To)>(_removedTrans); transDeletes.ExceptWith(_transitions.Keys); }
        List<(Coord, Dictionary<string, Door>)> doorsRefs;
        bool doorsWas;
        long doorGen0;
        HashSet<Coord> doorDeletes;
        using (ReadScope3()) { doorsRefs = _doors.Select(kv => (kv.Key, new Dictionary<string, Door>(kv.Value))).ToList(); doorsWas = _modified3; doorGen0 = _doorGen; doorDeletes = new HashSet<Coord>(_removedDoors); doorDeletes.ExceptWith(_doors.Keys); }

        // Detach copies (fresh locks, is_modified false). Clean domains are
        // skipped before paying for the copies: their rows are already
        // persisted and tombstones ride the delete sets below, not the snaps.
        bool saveAll = force || ObjectRegistry.AlwaysSaveAll;
        var transitionsSnap = (saveAll || transWas || transDeletes.Count > 0)
            ? transRefs.Select(t => new Transition(t.FromCoord, t.ToCoord, t.Name)).ToList()
            : [];
        var doorsSnap = (saveAll || doorsWas || doorDeletes.Count > 0)
            ? doorsRefs.Select(kv => (kv.Item1, kv.Item2.ToDictionary(kv2 => kv2.Key, kv2 =>
        {
            var d = kv2.Value;
            d.Lock.EnterReadLock();
            try { return new Door(d.FromCoord, d.ToCoord, d.FromExit, d.ToExit, d.SymbolCoord, d.ClosedSymbol, d.OpenSymbol, d.Closed, d.Locked); }
            finally { d.Lock.ExitReadLock(); }
        }))).ToList()
            : [];

        List<NodeArea> clearedAreas = [];
        List<NodeGrid> clearedGrids = [];
        List<Node> clearedNodes = [];
        List<NodeAreaDto> areasDto = [];
        // Pre-serialize outside gate to avoid holding DB lock during serialization (faithful to Python not holding db.lock during dill.dumps)
        List<(NodeAreaDto dto, string json)> areaJsons = [];
        List<(Transition t, string json)> transJsons = [];
        List<((Coord coord, Dictionary<string, Door> dict) item, string json)> doorsJsons = [];
        try
        {
            foreach (var a in areaRefs)
            {
                // Skip clean areas before paying for the DTO walk: with no
                // force and no save-all, an area whose whole subtree is
                // unmodified contributes nothing to this checkpoint
                // (tombstones ride areaDeletes below, not the DTOs). A flag
                // set after this scan survives for the next checkpoint —
                // nothing is cleared without being written.
                if (!force && !ObjectRegistry.AlwaysSaveAll && !AreaSubtreeDirty(a)) continue;
                bool wasArea;
                Dictionary<int, NodeGrid> gridsSnap;
                Dictionary<string, JsonElement> dataCopy;
                string name, theme;
                HashSet<string>? linked;
                a.Lock.EnterWriteLock();
                try
                {
                    wasArea = a.IsModified;
                    gridsSnap = new Dictionary<int, NodeGrid>(a.Grids);
                    dataCopy = new Dictionary<string, JsonElement>(a.Data);
                    name = a.Name;
                    theme = a.Theme ?? "";
                    linked = a.LinkedAreas is not null ? new HashSet<string>(a.LinkedAreas) : null;
                    if (wasArea) { a.IsModified = false; clearedAreas.Add(a); }
                }
                finally { a.Lock.ExitWriteLock(); }

                Dictionary<int, NodeGridDto> gridsDto = [];
                List<NodeGrid> localGrids = [];
                List<Node> localNodes = [];
                foreach (var (z,g) in gridsSnap)
                {
                    bool wasGrid;
                    Dictionary<(int,int), Node> nodesSnap;
                    Dictionary<string, JsonElement> gData;
                    g.Lock.EnterWriteLock();
                    try
                    {
                        wasGrid = g.IsModified;
                        nodesSnap = new Dictionary<(int,int), Node>(g.Nodes);
                        gData = new Dictionary<string, JsonElement>(g.Data);
                        if (wasGrid) { g.IsModified = false; localGrids.Add(g); }
                    }
                    finally { g.Lock.ExitWriteLock(); }

                    Dictionary<string, NodeDto> nodesDto = [];
                    foreach (var (coord,n) in nodesSnap)
                    {
                        NodeDto dto;
                        bool wasNode;
                        // Snapshot + clear atomically under the node write lock. The old
                        // code read fields off-lock and cleared afterwards, losing edits
                        // that landed in between (torn DTO / cleared-but-unsaved). All
                        // Node/GameObject mutators take SyncRoot and the lock is
                        // SupportsRecursion, so the locked property reads + clear below
                        // are safe while held; no handler lock is held here (leaf-level).
                        using (n.WriteScope())
                        {
                            wasNode = n.IsModified;
                            HashSet<int> scriptsSnap;
                            string? objType = null;
                            try
                            {
                                scriptsSnap = n.ScriptsSet;
                                var t = n.GetType();
                                if (t != typeof(Node))
                                    objType = Node.RegisteredNameFor(t) ?? t.AssemblyQualifiedName ?? t.FullName;
                            }
                            catch { scriptsSnap = []; }
                            dto = new NodeDto
                            {
                                Coord = n.Coord,
                                Name = n.Name,
                                Desc = n.Desc,
                                Theme = n.Theme,
                                Symbol = n.Symbol,
                                LegendDesc = n.LegendDesc,
                                Links = n.GetLinks(),
                                Nouns = new Dictionary<string,string>(n.Nouns),
                                Id = n.Id,
                                Scripts = scriptsSnap,
                                ObjectType = objType,
                            };
                            if (wasNode) { n.IsModified = false; localNodes.Add(n); }
                        }
                        nodesDto[$"{coord.Item1},{coord.Item2}"] = dto;
                    }
                    gridsDto[z] = new NodeGridDto { Area = g.Area, Z = g.Z, Nodes = nodesDto, Data = gData };
                }
                areasDto.Add(new NodeAreaDto { Name=name, Theme=theme, Grids=gridsDto, Data=dataCopy, LinkedAreas=linked });
                clearedGrids.AddRange(localGrids);
                clearedNodes.AddRange(localNodes);
            }

            if (areasDto.Count==0 && transitionsSnap.Count==0 && doorsSnap.Count==0
                && areaDeletes.Count==0 && transDeletes.Count==0 && doorDeletes.Count==0)
            {
                // restore if nothing to write
                RestoreSaveFlags(handlerWas, transWas, doorsWas, clearedAreas, clearedGrids, clearedNodes);
                return;
            }

            // Serialize outside DB gate
            foreach (var dto in areasDto)
            {
                var json = SerializeSnapshot(dto);
                areaJsons.Add((dto, json));
            }
            foreach (var t in transitionsSnap)
            {
                var json = SerializeSnapshot(t);
                transJsons.Add((t, json));
            }
            foreach (var item in doorsSnap)
            {
                var json = SerializeSnapshot(item.Item2);
                doorsJsons.Add((item, json));
            }
        }
        catch
        {
            // Restore flags on serialization/build failure (mirrors Python detach failure restore)
            RestoreSaveFlags(handlerWas, transWas, doorsWas, clearedAreas, clearedGrids, clearedNodes);
            throw;
        }

        // Post-commit clear of handler flags (mirrors Python flag reset after COMMIT).
        // Transition/door domains clear only if their gen counter is unchanged
        // since the snapshot (node.py:487-491): a concurrent mutation survives
        // for the next checkpoint instead of being lost.
        void MarkHandlerClean()
        {
            // Area branch is gen-guarded like transitions/doors: a concurrent
            // AddArea/AddNode between snapshot and clean must survive.
            if (handlerWas) { Lock.EnterWriteLock(); try { if (_areaGen == areaGen0) _modified = false; } finally { Lock.ExitWriteLock(); } }
            if (transWas) { Lock2.EnterWriteLock(); try { if (_transGen == transGen0) _modified2 = false; } finally { Lock2.ExitWriteLock(); } }
            if (doorsWas) { Lock3.EnterWriteLock(); try { if (_doorGen == doorGen0) _modified3 = false; } finally { Lock3.ExitWriteLock(); } }
        }

        try
        {
            DbTransactionHelper.WithGateAndTransaction(db, ctx =>
            {
                foreach (var (dto, json) in areaJsons)
                {
                    DbTransactionHelper.UpsertJson(ctx.Areas, () => ctx.Areas.Find(dto.Name), () => new Persistence.Entities.AreaRow { Name = dto.Name }, json);
                }
                foreach (var (t, json) in transJsons)
                {
                    DbTransactionHelper.UpsertJson(ctx.Transitions, () => ctx.Transitions.Find(t.FromCoord.Area, t.FromCoord.X, t.FromCoord.Y, t.FromCoord.Z, t.ToCoord.Area, t.ToCoord.X, t.ToCoord.Y, t.ToCoord.Z), () => new Persistence.Entities.TransitionRow { FromArea = t.FromCoord.Area, FromX = t.FromCoord.X, FromY = t.FromCoord.Y, FromZ = t.FromCoord.Z, ToArea = t.ToCoord.Area, ToX = t.ToCoord.X, ToY = t.ToCoord.Y, ToZ = t.ToCoord.Z }, json);
                }
                foreach (var (item, json) in doorsJsons)
                {
                    var coord = item.coord;
                    DbTransactionHelper.UpsertJson(ctx.Doors, () => ctx.Doors.Find(coord.Area, coord.X, coord.Y, coord.Z), () => new Persistence.Entities.DoorRow { Area = coord.Area, X = coord.X, Y = coord.Y, Z = coord.Z }, json);
                }
                // Tombstone deletes ride the SAME transaction as the upserts.
                // Re-validate against live keys in-txn : a key
                // removed then re-added after the snapshot must not be
                // deleted at commit. Read locks inside the gate follow the
                // IsStillSaveable precedent in ObjectRegistry.SaveObjects.
                Lock.EnterReadLock();
                try { areaDeletes.ExceptWith(_areas.Keys); }
                finally { Lock.ExitReadLock(); }
                Lock2.EnterReadLock();
                try { transDeletes.ExceptWith(_transitions.Keys); }
                finally { Lock2.ExitReadLock(); }
                Lock3.EnterReadLock();
                try { doorDeletes.ExceptWith(_doors.Keys); }
                finally { Lock3.ExitReadLock(); }
                foreach (var name in areaDeletes)
                {
                    var row = ctx.Areas.Find(name);
                    if (row is not null) ctx.Areas.Remove(row);
                }
                foreach (var key in transDeletes)
                {
                    var row = ctx.Transitions.Find(key.From.Area, key.From.X, key.From.Y, key.From.Z, key.To.Area, key.To.X, key.To.Y, key.To.Z);
                    if (row is not null) ctx.Transitions.Remove(row);
                }
                foreach (var key in doorDeletes)
                {
                    var row = ctx.Doors.Find(key.Area, key.X, key.Y, key.Z);
                    if (row is not null) ctx.Doors.Remove(row);
                }
            }, onRollback: () =>
            {
                RestoreSaveFlags(handlerWas, transWas, doorsWas, clearedAreas, clearedGrids, clearedNodes);
            });
        }
        catch (Exception ex)
        {
            // Closed-DB guard is the IsClosed flag (no message sniffing):
            // anything else propagates. One catch covers
            // InvalidOperationException too (it derives from Exception).
            if (!AtherizDbContext.IsClosed) throw;
            AtherizLogger.LogWarning($"database closed; skipping node save: {ex.Message}");
            // restore already handled via onRollback for transaction failure, but for gate failure before transaction we restored via catch above
            // Ensure flags restored
            RestoreSaveFlags(handlerWas, transWas, doorsWas, clearedAreas, clearedGrids, clearedNodes);
            return;
        }
        MarkHandlerClean();
        // Success path only (every failure path above returns or throws):
        // the tombstoned rows are now gone from the DB.
        if (areaDeletes.Count > 0) { Lock.EnterWriteLock(); try { _removedAreas.ExceptWith(areaDeletes); } finally { Lock.ExitWriteLock(); } }
        if (transDeletes.Count > 0) { Lock2.EnterWriteLock(); try { _removedTrans.ExceptWith(transDeletes); } finally { Lock2.ExitWriteLock(); } }
        if (doorDeletes.Count > 0) { Lock3.EnterWriteLock(); try { _removedDoors.ExceptWith(doorDeletes); } finally { Lock3.ExitWriteLock(); } }
    }
}
