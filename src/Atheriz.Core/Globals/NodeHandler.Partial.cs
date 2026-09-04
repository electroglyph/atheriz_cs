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
        try { if (door.Closed) door.MapClose(); else door.MapOpen(); } catch { }
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
            var mh = MapHandlerHolder.Get();
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
        catch { }
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
                    if (!_areas.TryGetValue(node.Coord.Area, out var raced)) { _areas[area.Name] = area; _modified = true; }
                    else area = raced;
                }
                finally { Lock.ExitWriteLock(); }
            }
        }
        finally { Lock.ExitUpgradeableReadLock(); }
        var grid=area.GetOrAddGrid(node.Coord.Z);
        grid.AddNode(node);
        Lock.EnterWriteLock();
        try { _modified=true; }
        finally { Lock.ExitWriteLock(); }
        ObjectRegistry.AddObject(node);
    }
    public void AddArea(NodeArea area)
    {
        Lock.EnterWriteLock();
        try { _areas[area.Name]=area; _modified=true; }
        finally { Lock.ExitWriteLock(); }
    }
    public void RemoveArea(string name)
    {
        NodeArea? area=null;
        Lock.EnterWriteLock();
        try { _areas.Remove(name,out area); _modified=true; }
        finally { Lock.ExitWriteLock(); }
        if(area!=null)
        {
            foreach(var g in area.Grids.Values)
                foreach(var n in g.Nodes.Values.ToList())
                    ObjectRegistry.RemoveObject(n);
            area.Clear();
        }
    }
    public void Clear()
    {
        Lock.EnterWriteLock();
        try
        {
            foreach(var a in _areas.Values)
                foreach(var g in a.Grids.Values)
                    foreach(var n in g.Nodes.Values)
                        ObjectRegistry.RemoveObject(n);
            _areas.Clear(); _modified=true;
        }
        finally { Lock.ExitWriteLock(); }
        Lock2.EnterWriteLock();
        try { _transitions.Clear(); _modified2=true; _transGen++; }
        finally { Lock2.ExitWriteLock(); }
        Lock3.EnterWriteLock();
        try { _doors.Clear(); _modified3=true; _doorGen++; }
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
        var area=GetArea(coord.Area);
        if(area==null) return null;
        var grid=area.GetGrid(coord.Z);
        return grid?.GetNode(coord.X,coord.Y);
    }
    public void RemoveNode(Coord coord)
    {
        var node=GetNode(coord);
        var area=GetArea(coord.Area);
        var grid=area?.GetGrid(coord.Z);
        grid?.RemoveNode((coord.X,coord.Y));
        if(node!=null) ObjectRegistry.RemoveObject(node);
        Lock.EnterWriteLock();
        try { _modified=true; }
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
        try { _transitions[t.ToCoord]=t; _modified2=true; _transGen++; }
        finally { Lock2.ExitWriteLock(); }
    }
    public void RemoveTransition(Coord dest)
    {
        Lock2.EnterWriteLock();
        try { _transitions.Remove(dest); _modified2=true; _transGen++; }
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
                }
            }
            foreach (var (newFull, doorsDict) in relocated)
            {
                if (!_doors.TryGetValue(newFull, out var existing))
                    _doors[newFull] = doorsDict;
                else
                    foreach (var kv in doorsDict) existing[kv.Key] = kv.Value;
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
                        int dx = 0, dy = 0;
                        if (oldToNewFull.TryGetValue(door.FromCoord, out var newFrom))
                        {
                            dx = newFrom.X - door.FromCoord.X;
                            dy = newFrom.Y - door.FromCoord.Y;
                            door.FromCoord = newFrom;
                        }
                        if (oldToNewFull.TryGetValue(door.ToCoord, out var newTo))
                        {
                            dx = newTo.X - door.ToCoord.X;
                            dy = newTo.Y - door.ToCoord.Y;
                            door.ToCoord = newTo;
                        }
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
            foreach (var (oldFull, newFull) in oldToNewFull)
            {
                if (_transitions.TryGetValue(oldFull, out var trans))
                {
                    _transitions.Remove(oldFull);
                    trans.Lock.EnterWriteLock();
                    try { trans.ToCoord = newFull; } finally { trans.Lock.ExitWriteLock(); }
                    _transitions[newFull] = trans;
                    _modified2 = true;
                    _transGen++;
                }
            }
        }
        finally { Lock2.ExitWriteLock(); }
    }
}
