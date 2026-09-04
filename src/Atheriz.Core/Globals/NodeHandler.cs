using System.Text.Json;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

/// <summary>
/// Faithful port of <c>atheriz/globals/node.py:NodeHandler</c>.
/// Three separate locks mirroring Python's lock/lock2/lock3, dirty flags per table,
/// JSON persistence (replaces dill), and id collision handling via <see cref="ObjectRegistry"/>.
/// </summary>
public partial class NodeHandler
{
    // Audit P2-10: hide public Lock/Lock2/Lock3, use SupportsRecursion for re-entrant test paths (AddArea inside WriteLock, Reregister nested reads)
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
    private sealed class LockScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _rw;
        private readonly bool _isWrite;
        public LockScope(ReaderWriterLockSlim rw, bool isWrite) { _rw = rw; _isWrite = isWrite; }
        public void Dispose() { if (_isWrite) _rw.ExitWriteLock(); else _rw.ExitReadLock(); }
    }
    // Test hook for serialization lock verification (port of dill.dumps monkeypatch)
    public static Func<object, string>? TestSerializeHook;
    private readonly Dictionary<string, NodeArea> _areas = new();
    private readonly Dictionary<Coord, Transition> _transitions = new();
    private readonly Dictionary<Coord, Dictionary<string, Door>> _doors = new();
    private bool _modified, _modified2, _modified3;

    private static NodeHandler? _current;
    private static readonly object _currentLock = new();
    public static NodeHandler? GetCurrent() { lock (_currentLock) return _current; }
    public static void SetCurrent(NodeHandler? h) { lock (_currentLock) _current = h; }
    internal void MarkDoorsModified() { _modified3 = true; }
    internal void MarkTransitionsModified() { _modified2 = true; }

    public NodeHandler() { lock (_currentLock) _current = this; Load(); }
    public NodeHandler(bool autoLoad) { lock (_currentLock) _current = this; if (autoLoad) Load(); }

    // --- load ---
    public void Load() => Load(global::Atheriz.Core.Persistence.AtherizDbContextFactory.Create());

    public void Load(AtherizDbContext db)
    {
        try
        {
            db.Database.EnsureCreated();
            JsonTableLoader.LoadInto(db.Areas, Lock, json => JsonSerializer.Deserialize<NodeAreaDto>(json, JsonOptions.Default), (dto, row) =>
            {
                var na = dto.ToDomain();
                _areas[na.Name] = na;
            });
            JsonTableLoader.LoadInto(db.Transitions, Lock2, json => JsonSerializer.Deserialize<Transition>(json, JsonOptions.Default), (dto, row) => _transitions[dto.ToCoord] = dto);
            JsonTableLoader.LoadInto(db.Doors, Lock3, json => JsonSerializer.Deserialize<Dictionary<string, Door>>(json, JsonOptions.Default), (dto, row) => _doors[new Coord(row.Area, row.X, row.Y, row.Z)] = dto);
        }
        catch { return; }

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
                        // collision warning
                    }
                    ObjectRegistry.AddObject(node);
                    // Port of node.py:84-86 node.resolve_relations() — reinstall script hooks, ticker, at_init
                    try { node.ResolveRelations(); } catch {}
                }
            }
        }
        if (maxNodeId != 0 && maxNodeId > IdGenerator.GetId())
            IdGenerator.SetId(maxNodeId);
    }

    private bool IsDirty()
    {
        List<NodeArea> areas;
        using (ReadScope())
        {
            if (_modified) return true;
            areas = _areas.Values.ToList();
        }
        foreach (var a in areas)
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
        }
        Lock2.EnterReadLock();
        try { if (_modified2) return true; }
        finally { Lock2.ExitReadLock(); }
        Lock3.EnterReadLock();
        try { if (_modified3) return true; }
        finally { Lock3.ExitReadLock(); }
        return false;
    }

    public virtual void Save(bool force = false)
    {
        if (!force && !ObjectRegistry.AlwaysSaveAll && !IsDirty()) return;
        try { Save(global::Atheriz.Core.Persistence.AtherizDbContextFactory.Create(), force); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping node save: {ex.Message}");
            return;
        }
        catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping node save: {ex.Message}");
            return;
        }
    }

    public virtual void Save(AtherizDbContext db, bool force = false)
    {
        // Snapshot refs (using scoped helpers)
        List<NodeArea> areaRefs;
        bool handlerWas;
        using (ReadScope()) { areaRefs = _areas.Values.ToList(); handlerWas = _modified; }
        var transRefs = new List<Transition>();
        bool transWas;
        using (ReadScope2()) { transRefs = _transitions.Values.ToList(); transWas = _modified2; }
        List<(Coord, Dictionary<string, Door>)> doorsRefs;
        bool doorsWas;
        using (ReadScope3()) { doorsRefs = _doors.Select(kv => (kv.Key, new Dictionary<string, Door>(kv.Value))).ToList(); doorsWas = _modified3; }

        // Detach copies (fresh locks, is_modified false)
        var transitionsSnap = transRefs.Select(t => new Transition(t.FromCoord, t.ToCoord, t.Name)).ToList();
        var doorsSnap = doorsRefs.Select(kv => (kv.Item1, kv.Item2.ToDictionary(kv2 => kv2.Key, kv2 =>
        {
            var d = kv2.Value;
            d.Lock.EnterReadLock();
            try { return new Door(d.FromCoord, d.ToCoord, d.FromExit, d.ToExit, d.SymbolCoord, d.ClosedSymbol, d.OpenSymbol, d.Closed, d.Locked); }
            finally { d.Lock.ExitReadLock(); }
        }))).ToList();

        var clearedAreas = new List<NodeArea>();
        var clearedGrids = new List<NodeGrid>();
        var clearedNodes = new List<Node>();
        var areasDto = new List<NodeAreaDto>();
        // Pre-serialize outside gate to avoid holding DB lock during serialization (faithful to Python not holding db.lock during dill.dumps)
        var areaJsons = new List<(NodeAreaDto dto, string json)>();
        var transJsons = new List<(Transition t, string json)>();
        var doorsJsons = new List<((Coord coord, Dictionary<string, Door> dict) item, string json)>();
        try
        {
            foreach (var a in areaRefs)
            {
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
                    linked = a.LinkedAreas != null ? new HashSet<string>(a.LinkedAreas) : null;
                    if (wasArea) { a.IsModified = false; clearedAreas.Add(a); }
                }
                finally { a.Lock.ExitWriteLock(); }

                var gridsDto = new Dictionary<int, NodeGridDto>();
                var localGrids = new List<NodeGrid>();
                var localNodes = new List<Node>();
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

                    var nodesDto = new Dictionary<string, NodeDto>();
                    foreach (var (coord,n) in nodesSnap)
                    {
                        bool wasNode = n.IsModified;
                        // snapshot under node lock
                        HashSet<int> scriptsSnap;
                        string? objType = null;
                        try
                        {
                            scriptsSnap = n.ScriptsSet;
                            var t = n.GetType();
                            if (t != typeof(Node))
                                objType = t.AssemblyQualifiedName ?? t.FullName;
                        }
                        catch { scriptsSnap = new HashSet<int>(); }
                        var dto = new NodeDto
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
                        nodesDto[$"{coord.Item1},{coord.Item2}"] = dto;
                        if (wasNode) { n.IsModified = false; localNodes.Add(n); }
                    }
                    gridsDto[z] = new NodeGridDto { Area = g.Area, Z = g.Z, Nodes = nodesDto, Data = gData };
                }
                areasDto.Add(new NodeAreaDto { Name=name, Theme=theme, Grids=gridsDto, Data=dataCopy, LinkedAreas=linked });
                clearedGrids.AddRange(localGrids);
                clearedNodes.AddRange(localNodes);
            }

            if (areasDto.Count==0 && transitionsSnap.Count==0 && doorsSnap.Count==0)
            {
                // restore if nothing to write
                if (handlerWas) { Lock.EnterWriteLock(); try{ _modified=true;} finally{Lock.ExitWriteLock();} }
                if (transWas) { Lock2.EnterWriteLock(); try{ _modified2=true;} finally{Lock2.ExitWriteLock();} }
                if (doorsWas) { Lock3.EnterWriteLock(); try{ _modified3=true;} finally{Lock3.ExitWriteLock();} }
                foreach(var a in clearedAreas){ a.Lock.EnterWriteLock(); try{ a.IsModified=true;} finally{ a.Lock.ExitWriteLock();} }
                foreach(var g in clearedGrids){ g.Lock.EnterWriteLock(); try{ g.IsModified=true;} finally{ g.Lock.ExitWriteLock();} }
                foreach(var n in clearedNodes) n.IsModified=true;
                return;
            }

            // Serialize outside DB gate
            foreach (var dto in areasDto)
            {
                var json = TestSerializeHook != null ? TestSerializeHook(dto) : JsonSerializer.Serialize(dto, JsonOptions.Default);
                areaJsons.Add((dto, json));
            }
            foreach (var t in transitionsSnap)
            {
                var json = TestSerializeHook != null ? TestSerializeHook(t) : JsonSerializer.Serialize(t, JsonOptions.Default);
                transJsons.Add((t, json));
            }
            foreach (var item in doorsSnap)
            {
                var json = TestSerializeHook != null ? TestSerializeHook(item.Item2) : JsonSerializer.Serialize(item.Item2, JsonOptions.Default);
                doorsJsons.Add((item, json));
            }
        }
        catch
        {
            // Restore flags on serialization/build failure (mirrors Python detach failure restore)
            if (handlerWas) { Lock.EnterWriteLock(); try { _modified = true; } finally { Lock.ExitWriteLock(); } }
            if (transWas) { Lock2.EnterWriteLock(); try { _modified2 = true; } finally { Lock2.ExitWriteLock(); } }
            if (doorsWas) { Lock3.EnterWriteLock(); try { _modified3 = true; } finally { Lock3.ExitWriteLock(); } }
            foreach (var a in clearedAreas) { a.Lock.EnterWriteLock(); try { a.IsModified = true; } finally { a.Lock.ExitWriteLock(); } }
            foreach (var g in clearedGrids) { g.Lock.EnterWriteLock(); try { g.IsModified = true; } finally { g.Lock.ExitWriteLock(); } }
            foreach (var n in clearedNodes) n.IsModified = true;
            throw;
        }

        // Post-commit clear of handler flags (mirrors Python flag reset after COMMIT)
        void MarkHandlerClean()
        {
            if (handlerWas) { Lock.EnterWriteLock(); try { _modified = false; } finally { Lock.ExitWriteLock(); } }
            if (transWas) { Lock2.EnterWriteLock(); try { _modified2 = false; } finally { Lock2.ExitWriteLock(); } }
            if (doorsWas) { Lock3.EnterWriteLock(); try { _modified3 = false; } finally { Lock3.ExitWriteLock(); } }
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
                    DbTransactionHelper.UpsertJson(ctx.Transitions, () => ctx.Transitions.Find(t.ToCoord.Area, t.ToCoord.X, t.ToCoord.Y, t.ToCoord.Z), () => new Persistence.Entities.TransitionRow { ToArea = t.ToCoord.Area, ToX = t.ToCoord.X, ToY = t.ToCoord.Y, ToZ = t.ToCoord.Z }, json);
                }
                foreach (var (item, json) in doorsJsons)
                {
                    var coord = item.coord;
                    DbTransactionHelper.UpsertJson(ctx.Doors, () => ctx.Doors.Find(coord.Area, coord.X, coord.Y, coord.Z), () => new Persistence.Entities.DoorRow { Area = coord.Area, X = coord.X, Y = coord.Y, Z = coord.Z }, json);
                }
            }, onRollback: () =>
            {
                if (handlerWas) { Lock.EnterWriteLock(); try { _modified = true; } finally { Lock.ExitWriteLock(); } }
                if (transWas) { Lock2.EnterWriteLock(); try { _modified2 = true; } finally { Lock2.ExitWriteLock(); } }
                if (doorsWas) { Lock3.EnterWriteLock(); try { _modified3 = true; } finally { Lock3.ExitWriteLock(); } }
                foreach (var a in clearedAreas) { a.Lock.EnterWriteLock(); try { a.IsModified = true; } finally { a.Lock.ExitWriteLock(); } }
                foreach (var g in clearedGrids) { g.Lock.EnterWriteLock(); try { g.IsModified = true; } finally { g.Lock.ExitWriteLock(); } }
                foreach (var n in clearedNodes) n.IsModified = true;
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping node save: {ex.Message}");
            // restore already handled via onRollback for transaction failure, but for gate failure before transaction we restored via catch above
            // Ensure flags restored
            if (handlerWas) { Lock.EnterWriteLock(); try { _modified = true; } finally { Lock.ExitWriteLock(); } }
            if (transWas) { Lock2.EnterWriteLock(); try { _modified2 = true; } finally { Lock2.ExitWriteLock(); } }
            if (doorsWas) { Lock3.EnterWriteLock(); try { _modified3 = true; } finally { Lock3.ExitWriteLock(); } }
            foreach (var a in clearedAreas) { a.Lock.EnterWriteLock(); try { a.IsModified = true; } finally { a.Lock.ExitWriteLock(); } }
            foreach (var g in clearedGrids) { g.Lock.EnterWriteLock(); try { g.IsModified = true; } finally { g.Lock.ExitWriteLock(); } }
            foreach (var n in clearedNodes) n.IsModified = true;
            return;
        }
        catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping node save: {ex.Message}");
            if (handlerWas) { Lock.EnterWriteLock(); try { _modified = true; } finally { Lock.ExitWriteLock(); } }
            if (transWas) { Lock2.EnterWriteLock(); try { _modified2 = true; } finally { Lock2.ExitWriteLock(); } }
            if (doorsWas) { Lock3.EnterWriteLock(); try { _modified3 = true; } finally { Lock3.ExitWriteLock(); } }
            foreach (var a in clearedAreas) { a.Lock.EnterWriteLock(); try { a.IsModified = true; } finally { a.Lock.ExitWriteLock(); } }
            foreach (var g in clearedGrids) { g.Lock.EnterWriteLock(); try { g.IsModified = true; } finally { g.Lock.ExitWriteLock(); } }
            foreach (var n in clearedNodes) n.IsModified = true;
            return;
        }
        MarkHandlerClean();
    }

    // DTO helpers for JSON persistence
    private sealed class NodeDto
    {
        public Coord Coord { get; set; }
        public string Name { get; set; } = "";
        public string Desc { get; set; } = "";
        public string Theme { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string? LegendDesc { get; set; }
        public List<NodeLink> Links { get; set; } = [];
        public Dictionary<string,string> Nouns { get; set; } = new();
        public int Id { get; set; }
        public HashSet<int> Scripts { get; set; } = new();
        public string? ObjectType { get; set; }
    }
    private sealed class NodeGridDto
    {
        public string Area { get; set; } = "";
        public int Z { get; set; }
        public Dictionary<string, NodeDto> Nodes { get; set; } = new();
        public Dictionary<string, JsonElement> Data { get; set; } = new();
    }
    private sealed class NodeAreaDto
    {
        public string Name { get; set; } = "";
        public string Theme { get; set; } = "";
        public Dictionary<int, NodeGridDto> Grids { get; set; } = new();
        public Dictionary<string, JsonElement> Data { get; set; } = new();
        public HashSet<string>? LinkedAreas { get; set; }
        public NodeArea ToDomain()
        {
            var area=new NodeArea(Name, Theme){ Data=Data, LinkedAreas=LinkedAreas, IsModified=false };
            foreach(var (z,gdto) in Grids)
            {
                var grid=new NodeGrid(gdto.Area, gdto.Z, gdto.Data){ IsModified=false };
                foreach(var kv in gdto.Nodes)
                {
                    var nd=kv.Value;
                    Node node;
                    // Preserve concrete Node subclass via ObjectType (dill-like fidelity) — mirrors GameObject.FromDto __object_type handling
                    if (!string.IsNullOrEmpty(nd.ObjectType))
                    {
                        Type? t = null;
                        try { t = Type.GetType(nd.ObjectType!); } catch {}
                        if (t == null)
                        {
                            try { t = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=> { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }).FirstOrDefault(x=> x.FullName==nd.ObjectType || x.Name==nd.ObjectType || x.AssemblyQualifiedName==nd.ObjectType); } catch {}
                        }
                        if (t != null && typeof(Node).IsAssignableFrom(t))
                        {
                            Node? inst = null;
                            try { inst = (Node?)Activator.CreateInstance(t, new object[]{ nd.Coord }); } catch {}
                            if (inst == null) try { inst = (Node?)Activator.CreateInstance(t, nonPublic:true); } catch {}
                            if (inst != null)
                            {
                                // Remove from ObjectRegistry the auto-registered instance's temporary id collision
                                try { ObjectRegistry.RemoveObject(inst); } catch {}
                                inst.Coord = nd.Coord;
                                inst.Desc = nd.Desc;
                                // base Name is coord string for Node, but preserve if needed
                                inst.Theme = nd.Theme ?? "";
                                inst.Symbol = nd.Symbol ?? "";
                                inst.LegendDesc = nd.LegendDesc;
                                inst.Links = nd.Links ?? new List<NodeLink>();
                                inst.Nouns = nd.Nouns ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                                inst.Id = nd.Id;
                                // Restore scripts into both _nodeScripts and base _scripts via reflection (Node's private field)
                                if (nd.Scripts != null && nd.Scripts.Count > 0)
                                {
                                    try
                                    {
                                        var f = typeof(Node).GetField("_nodeScripts", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                        var hs = f?.GetValue(inst) as HashSet<int>;
                                        if (hs != null) { hs.Clear(); foreach(var sid in nd.Scripts) hs.Add(sid); }
                                        var bf = typeof(GameObject).GetField("_scripts", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                                        var bhs = bf?.GetValue(inst) as HashSet<int>;
                                        if (bhs != null) { bhs.Clear(); foreach(var sid in nd.Scripts) bhs.Add(sid); }
                                    } catch {}
                                }
                                inst.IsModified=false;
                                node = inst;
                                grid.Nodes[(nd.Coord.X, nd.Coord.Y)] = node;
                                continue;
                            }
                        }
                    }
                    node=new Node(nd.Coord, nd.Name, nd.Desc, nd.Theme, nd.Symbol)
                    {
                        LegendDesc=nd.LegendDesc,
                        Links=nd.Links,
                        Nouns=nd.Nouns,
                    };
                    // Remove auto-registered temp node from registry (Node ctor adds)
                    try { ObjectRegistry.RemoveObject(node); } catch {}
                    node.Id=nd.Id;
                    if (nd.Scripts != null && nd.Scripts.Count > 0)
                    {
                        try
                        {
                            var f = typeof(Node).GetField("_nodeScripts", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                            var hs = f?.GetValue(node) as HashSet<int>;
                            if (hs != null) { hs.Clear(); foreach(var sid in nd.Scripts) hs.Add(sid); }
                            var bf = typeof(GameObject).GetField("_scripts", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                            var bhs = bf?.GetValue(node) as HashSet<int>;
                            if (bhs != null) { bhs.Clear(); foreach(var sid in nd.Scripts) bhs.Add(sid); }
                        } catch {}
                    }
                    node.IsModified=false;
                    grid.Nodes[(nd.Coord.X, nd.Coord.Y)] = node;
                }
                area.Grids[z]=grid;
            }
            return area;
        }
    }

}
