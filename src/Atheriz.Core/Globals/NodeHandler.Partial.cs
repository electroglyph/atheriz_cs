using Atheriz.Core.Objects;
namespace Atheriz.Core.Globals;

public partial class NodeHandler
{
    // --- simple helpers ---
    public Dictionary<string,Door>? GetDoors(Coord coord)
    {
        Lock3.EnterReadLock();
        try { return _doors.TryGetValue(coord,out var d) ? new Dictionary<string,Door>(d): null; }
        finally { Lock3.ExitReadLock(); }
    }
    public void AddDoor(Door door)
    {
        Lock3.EnterWriteLock();
        try
        {
            if (!_doors.TryGetValue(door.FromCoord,out var d)) { d=new(); _doors[door.FromCoord]=d; }
            d[door.FromExit]=door;
            if (!_doors.TryGetValue(door.ToCoord,out var d2)) { d2=new(); _doors[door.ToCoord]=d2; }
            d2[door.ToExit]=door;
            _modified3=true;
            _doorGen++;
        }
        finally { Lock3.ExitWriteLock(); }
        // Port of node.py add_door map stamp (after releasing Lock3: no lock
        // nesting). MapClose/MapOpen already implement the post_grid (+pre_grid
        // if non-empty) stamp + render; the MapEnabled gate only skips render
        // work when maps are disabled.
        try { if (door.Closed) door.MapClose(); else door.MapOpen(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.AddDoor: " + logEx.Message, "NodeHandler"); }
    }
    // Port of node.py remove_door: entries are removed by VALUE (v == door),
    // not by exit-name key.
    private static void RemoveDoorValue(Dictionary<string,Door> d, Door door)
    {
        List<string>? rem = null;
        foreach (var kv in d) if (Equals(kv.Value, door)) (rem ??= new()).Add(kv.Key);
        if (rem != null) foreach (var k in rem) d.Remove(k);
    }
    public void RemoveDoor(Door door)
    {
        Lock3.EnterWriteLock();
        try
        {
            if(_doors.TryGetValue(door.FromCoord,out var d)) RemoveDoorValue(d, door);
            if(_doors.TryGetValue(door.ToCoord,out var d2)) RemoveDoorValue(d2, door);
            _modified3=true;
            // Gen bump (AddDoor parity — see RemoveTransition). No tombstone:
            // doors are values inside the per-coord dict row, which Save
            // rewrites wholesale, so the removal persists without a delete.
            _doorGen++;
        }
        finally { Lock3.ExitWriteLock(); }
        var fromNode=GetNode(door.FromCoord);
        fromNode?.RemoveLink(door.FromExit);
        var toNode=GetNode(door.ToCoord);
        toNode?.RemoveLink(door.ToExit);
        // Port of node.py remove_door map cleanup (update_grid(symbol," ") +
        // render per (area,z)); after releasing Lock3, no lock nesting.
        try
        {
            var mh = MapHandlerSingleton.Get();
            if (mh != null && door.SymbolCoord != null)
            {
                var seen = new HashSet<(string, int)>();
                foreach (var coord in new[] { door.FromCoord, door.ToCoord })
                {
                    if (coord.Equals(default(Coord))) continue;
                    if (!seen.Add((coord.Area, coord.Z))) continue;
                    mh.GetMapInfo(coord.Area, coord.Z)?.UpdateGrid(door.SymbolCoord.Value, " ");
                }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.RemoveDoor: " + logEx.Message, "NodeHandler"); }
    }
    public void AddNode(Node node)
    {
        // Upgradeable read: two threads creating the same area concurrently
        // used to build duplicate NodeAreas (last-wins lost a grid).
        NodeArea? area;
        Lock.EnterUpgradeableReadLock();
        try
        {
            if (!_areas.TryGetValue(node.Coord.Area, out area))
            {
                area = new NodeArea(node.Coord.Area);
                Lock.EnterWriteLock();
                try
                {
                    if (!_areas.TryGetValue(node.Coord.Area, out var raced)) { _areas[area.Name] = area; _modified = true; _areaGen++; }
                    else area = raced;
                }
                finally { Lock.ExitWriteLock(); }
            }
        }
        finally { Lock.ExitUpgradeableReadLock(); }
        var grid=area.GetOrAddGrid(node.Coord.Z);
        grid.AddNode(node);
        Lock.EnterWriteLock();
        try { _modified=true; _areaGen++; }
        finally { Lock.ExitWriteLock(); }
        ObjectRegistry.AddObject(node);
    }
    public void AddArea(NodeArea area)
    {
        Lock.EnterWriteLock();
        try { _areas[area.Name]=area; _modified=true; _areaGen++; }
        finally { Lock.ExitWriteLock(); }
    }
    public void ReplaceArea(NodeArea area)
    {
        // Evict-then-install: a blind overwrite orphans the prior
        // generation's nodes in the registry (stale ids with no tombstone).
        // Collect under the handler lock, evict after release (RemoveObject
        // takes the registry AllLock) — same order as Clear().
        List<Node> evict = new();
        Lock.EnterWriteLock();
        try
        {
            if (_areas.TryGetValue(area.Name, out var old))
                foreach (var g in old.Grids.Values)
                    foreach (var n in g.Nodes.Values)
                        evict.Add(n);
            _areas[area.Name]=area; _modified=true; _areaGen++;
        }
        finally { Lock.ExitWriteLock(); }
        foreach (var n in evict)
            try { ObjectRegistry.RemoveObject(n); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.ReplaceArea: " + logEx.Message, "NodeHandler"); }
    }
    public void RemoveArea(string name)
    {
        NodeArea? area=null;
        Lock.EnterWriteLock();
        try { _areas.Remove(name,out area); _modified=true; _areaGen++; _removedAreas.Add(name); }
        finally { Lock.ExitWriteLock(); }
        // Port of node.py:639-644 — pop + area.clear() only. Nodes stay
        // registered (Python leaks them from _ALL_OBJECTS); only clear()
        // evicts, mirroring Python.
        area?.Clear();
    }
    public void Clear()
    {
        // Lock order handler -> registry: collect under the handler lock,
        // evict after release (RemoveObject takes the registry AllLock).
        List<Node> evict;
        Lock.EnterWriteLock();
        try
        {
            evict = new List<Node>();
            foreach (var a in _areas.Values)
                foreach (var g in a.Grids.Values)
                    foreach (var n in g.Nodes.Values)
                        evict.Add(n);
            foreach (var k in _areas.Keys) _removedAreas.Add(k);
            _areas.Clear(); _modified = true; _areaGen++;
        }
        finally { Lock.ExitWriteLock(); }
        foreach (var n in evict)
            try { ObjectRegistry.RemoveObject(n); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.Clear: " + logEx.Message, "NodeHandler"); }
        Lock2.EnterWriteLock();
        try { foreach (var k in _transitions.Keys) _removedTrans.Add(k); _transitions.Clear(); _modified2=true; _transGen++; }
        finally { Lock2.ExitWriteLock(); }
        Lock3.EnterWriteLock();
        try { foreach (var k in _doors.Keys) _removedDoors.Add(k); _doors.Clear(); _modified3=true; _doorGen++; }
        finally { Lock3.ExitWriteLock(); }
    }
    public NodeArea? GetArea(string name)
    {
        Lock.EnterReadLock();
        try { return _areas.TryGetValue(name,out var a)?a:null; }
        finally { Lock.ExitReadLock(); }
    }
    public List<NodeArea> GetAreas()
    {
        Lock.EnterReadLock();
        try { return _areas.Values.ToList(); }
        finally { Lock.ExitReadLock(); }
    }
    public Node? GetNode(Coord coord)
    {
        // Single-hold snapshot: area→grid→node resolve under one handler read
        // hold, so Clear/RemoveArea cannot swap the generation mid-traversal
        // and hand back a node from a discarded area. The nested area/grid
        // takes follow the established handler→area→grid order and re-enter
        // cleanly (all three locks allow recursion).
        Lock.EnterReadLock();
        try
        {
            if (!_areas.TryGetValue(coord.Area, out var area)) return null;
            var grid = area.GetGrid(coord.Z);
            return grid?.GetNode(coord.X, coord.Y);
        }
        finally { Lock.ExitReadLock(); }
    }
    public void RemoveNode(Coord coord)
    {
        // resolve + remove + evict under one write hold. The old
        // GetNode/GetArea/RemoveNode ran in 3 separate acquisitions, so a
        // coord replaced mid-sequence leaked one instance / dropped the other.
        // (SupportsRecursion: the nested read locks below are re-entrant.)
        Lock.EnterWriteLock();
        try
        {
            var node = GetNode(coord);
            var area = GetArea(coord.Area);
            var grid = area?.GetGrid(coord.Z);
            grid?.RemoveNode((coord.X, coord.Y));
            if (node != null) ObjectRegistry.RemoveObject(node);
            _modified = true; _areaGen++;
        }
        finally { Lock.ExitWriteLock(); }
    }
    public List<Node> GetNodes(List<Coord> coords)
    {
        var res=new List<Node>();
        foreach(var c in coords){ var n=GetNode(c); if(n!=null) res.Add(n); }
        return res;
    }
    public void AddTransition(Transition t)
    {
        Lock2.EnterWriteLock();
        try { _transitions[(t.FromCoord, t.ToCoord)] = t; _modified2 = true; _transGen++; }
        finally { Lock2.ExitWriteLock(); }
    }
    // Destination-only removal drops every fan-in edge to dest (previously at
    // most one could exist); tombstones record the exact (from,to) keys.
    public void RemoveTransition(Coord dest)
    {
        Lock2.EnterWriteLock();
        // Gen bump (AddTransition parity): without it a concurrent save can
        // snapshot, then clear _modified2 post-commit and lose this removal.
        try
        {
            foreach (var k in _transitions.Keys.Where(k => k.To.Equals(dest)).ToList())
            {
                _transitions.Remove(k);
                _removedTrans.Add(k);
            }
            _modified2 = true;
            _transGen++;
        }
        finally { Lock2.ExitWriteLock(); }
    }
    public List<Transition> FindTransitions(int? fromZ=null,int? toZ=null,string? fromArea=null,string? toArea=null)
    {
        int req=0;
        if(fromZ!=null) req++; if(toZ!=null) req++; if(fromArea!=null) req++; if(toArea!=null) req++;
        var res=new List<Transition>();
        Lock2.EnterReadLock();
        try
        {
            foreach(var t in _transitions.Values)
            {
                int m=0;
                if(fromZ!=null && t.FromCoord.Z==fromZ) m++;
                if(toZ!=null && t.ToCoord.Z==toZ) m++;
                if(fromArea!=null && t.FromCoord.Area==fromArea) m++;
                if(toArea!=null && t.ToCoord.Area==toArea) m++;
                if(m==req) res.Add(t);
            }
        }
        finally { Lock2.ExitReadLock(); }
        return res;
    }

    // Port of nodes.py:1147 door re-key for ApplyMoves
    public void RemapDoors(Dictionary<Coord, Coord> oldToNewFull, Dictionary<(int,int),(int,int)> remap)
    {
        Lock3.EnterWriteLock();
        try
        {
            var relocated = new Dictionary<Coord, Dictionary<string, Door>>();
            foreach (var oldFull in oldToNewFull.Keys.ToList())
            {
                if (_doors.TryGetValue(oldFull, out var dict))
                {
                    _doors.Remove(oldFull);
                    relocated[oldToNewFull[oldFull]] = dict;
                    // The old-coord row must die in the DB or the next Load
                    // resurrects the pre-move dict alongside the relocated one.
                    _removedDoors.Add(oldFull);
                }
            }
            foreach (var (newFull, doorsDict) in relocated)
            {
                if (!_doors.TryGetValue(newFull, out var existing))
                    _doors[newFull] = doorsDict;
                else
                    // First-wins like RemapTransitions below: the pre-existing dict
                    // stays, per-name newcomers are dropped loudly (owner decision
                    // 2026-09-08 — never silently clobber on a merge).
                    foreach (var kv in doorsDict)
                    {
                        if (!existing.ContainsKey(kv.Key)) existing[kv.Key] = kv.Value;
                        else try { AtherizLogger.LogWarning($"RemapDoors: dropping relocated door '{kv.Key}' at {newFull} (destination already has one)."); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.RemapDoors: " + logEx.Message, "NodeHandler"); }
                    }
            }
            var seenRef = new HashSet<Door>();
            foreach (var doorsDict in relocated.Values)
            {
                foreach (var door in doorsDict.Values)
                {
                    if (!seenRef.Add(door)) continue;
                    door.Lock.EnterWriteLock();
                    try
                    {
                        int fdx = 0, fdy = 0, tdx = 0, tdy = 0;
                        var from = door.FromCoord;
                        var to = door.ToCoord;
                        if (oldToNewFull.TryGetValue(from, out var newFrom))
                        {
                            fdx = newFrom.X - from.X;
                            fdy = newFrom.Y - from.Y;
                            from = newFrom;
                        }
                        if (oldToNewFull.TryGetValue(to, out var newTo))
                        {
                            tdx = newTo.X - to.X;
                            tdy = newTo.Y - to.Y;
                            to = newTo;
                        }
                        // Paired publish (see Door.SetEndpoints): two separate
                        // sets would strand a mixed generation for concurrent
                        // GetNodes snapshots.
                        door.SetEndpoints(from, to);
                        // The symbol cell is stamped into both endpoint grids,
                        // but it is a single coord: it cannot follow two
                        // different deltas. The From side anchors door identity
                        // (exit naming, MapClose order), so its delta wins;
                        // the To delta applies only when From did not move.
                        int dx = (fdx != 0 || fdy != 0) ? fdx : tdx;
                        int dy = (fdx != 0 || fdy != 0) ? fdy : tdy;
                        if (door.SymbolCoord != null && (dx != 0 || dy != 0))
                            door.SymbolCoord = (door.SymbolCoord.Value.X + dx, door.SymbolCoord.Value.Y + dy);
                    }
                    finally { door.Lock.ExitWriteLock(); }
                }
            }
            if (relocated.Count > 0) { _modified3 = true; _doorGen++; }
        }
        finally { Lock3.ExitWriteLock(); }
    }
    // Overload for NodeGrid call with only Coord map
    public void RemapDoors(Dictionary<Coord, Coord> oldToNewFull)
        => RemapDoors(oldToNewFull, new Dictionary<(int,int),(int,int)>());

    // Port of nodes.py:1194 transition remap
    public void RemapTransitions(Dictionary<Coord, Coord> oldToNewFull)
    {
        Lock2.EnterWriteLock();
        try
        {
            // Snapshot the caller dict: it may be mutated concurrently, and
            // half a remap must never persist .
            var moves = oldToNewFull.ToList();
            // Stale-from drop: ApplyMoves re-adds cross-area edges with fresh
            // From coords right after this call; without dest-keyed overwrite
            // the old (from,to) generation would survive alongside .
            foreach (var (oldFrom, _) in moves)
            {
                foreach (var key in _transitions.Keys.Where(k => k.From.Equals(oldFrom)).ToList())
                {
                    _transitions.Remove(key);
                    _removedTrans.Add(key);
                    _modified2 = true;
                    _transGen++;
                }
            }
            foreach (var (oldFull, newFull) in moves)
            {
                foreach (var key in _transitions.Keys.Where(k => k.To.Equals(oldFull)).ToList())
                {
                    var trans = _transitions[key];
                    _transitions.Remove(key);
                    trans.Lock.EnterWriteLock();
                    try { trans.ToCoord = newFull; } finally { trans.Lock.ExitWriteLock(); }
                    var newKey = (trans.FromCoord, newFull);
                    // Never silently clobber a pre-existing destination edge:
                    // first-wins, the dropped relocation is logged loudly.
                    if (!_transitions.ContainsKey(newKey))
                        _transitions[newKey] = trans;
                    else try { AtherizLogger.LogWarning($"RemapTransitions: dropping relocated edge to {newFull} (destination already has one)."); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeHandler.RemapTransitions: " + logEx.Message, "NodeHandler"); }
                    _modified2 = true;
                    _transGen++;
                    // Old-coord row must die in the DB (see RemapDoors).
                    _removedTrans.Add(key);
                }
            }
        }
        finally { Lock2.ExitWriteLock(); }
    }
}
