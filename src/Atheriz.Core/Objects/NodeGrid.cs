using Atheriz.Core;

namespace Atheriz.Core.Objects;

public sealed class NodeGrid
{
    public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);
    public string Area { get; set; }
    public int Z { get; set; }
    public bool IsModified { get; set; } = true;
    // Snapshot copies: the getter returns a copy under the read lock, so a
    // reader enumerating the copy never races a concurrent mutator. Lock-held
    // internals below touch _nodes/_data directly.
    private Dictionary<(int X, int Y), Node> _nodes = new();
    public Dictionary<(int X, int Y), Node> Nodes
    {
        get { Lock.EnterReadLock(); try { return new Dictionary<(int X, int Y), Node>(_nodes); } finally { Lock.ExitReadLock(); } }
    }
    private Dictionary<string, System.Text.Json.JsonElement> _data = new();
    public Dictionary<string, System.Text.Json.JsonElement> Data
    {
        get { Lock.EnterReadLock(); try { return new Dictionary<string, System.Text.Json.JsonElement>(_data); } finally { Lock.ExitReadLock(); } }
        set { Lock.EnterWriteLock(); try { _data = value is null ? new() : new Dictionary<string, System.Text.Json.JsonElement>(value); IsModified = true; } finally { Lock.ExitWriteLock(); } }
    }

    public NodeGrid(string area, int z, Dictionary<string, System.Text.Json.JsonElement>? data = null)
    {
        Area = area;
        Z = z;
        if (data is not null) Data = data;
    }
    public override string ToString() => $"NodeGrid(z={Z}, area={Area})";
// Python dict == is order-insensitive; JsonElement has no
    // value equality in C#, so data compares by canonical raw text. Hash combines the
    // same components in sorted order so equal grids hash equal.
    public override bool Equals(object? obj)
    {
        if (obj is not NodeGrid o) return false;
        if (Area != o.Area || Z != o.Z) return false;
        // Snapshot each side under its own read lock (taking both grid locks
        // here would order them arbitrarily).
        var myNodes = Nodes; var otherNodes = o.Nodes;
        if (myNodes.Count != otherNodes.Count) return false;
        var myData = Data; var otherData = o.Data;
        if (myData.Count != otherData.Count) return false;
        foreach (var kv in myNodes)
            if (!otherNodes.TryGetValue(kv.Key, out var n) || !kv.Value.Equals(n)) return false;
        foreach (var kv in myData)
            if (!otherData.TryGetValue(kv.Key, out var je) || kv.Value.GetRawText() != je.GetRawText()) return false;
        return true;
    }
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Area);
        h.Add(Z);
        var nodes = Nodes; var data = Data;
        foreach (var k in nodes.Keys.OrderBy(k => k)) { h.Add(k); h.Add(nodes[k]); }
        foreach (var k in data.Keys.OrderBy(k => k, StringComparer.Ordinal)) { h.Add(k); h.Add(data[k].GetRawText()); }
        return h.ToHashCode();
    }
    public int Count { get { Lock.EnterReadLock(); try { return _nodes.Count; } finally { Lock.ExitReadLock(); } } }
    /// <summary>
    /// Hot-reload rewire: swap a stale node instance for its replacement (matched
    /// by id, kept at its coord key). Python's __class__ swap preserves identity;
    /// C# must rewire direct refs after AddObject replaces the id.
    /// </summary>
    public void ReplaceNodeValue(Node replacement)
    {
        Lock.EnterWriteLock();
        try
        {
            // The Keys snapshot stays: assigning an existing key still bumps
            // the dictionary version, so enumerating Keys directly would throw
            // InvalidOperationException. Only the double lookup collapses via
            // TryGetValue.
            foreach (var k in _nodes.Keys.ToList())
                if (_nodes.TryGetValue(k, out var cur) && cur is not null && cur.Id == replacement.Id && !ReferenceEquals(cur, replacement))
                    _nodes[k] = replacement;
        }
        finally { Lock.ExitWriteLock(); }
    }
    public void SetData(string key, System.Text.Json.JsonElement value)
    {
        Lock.EnterWriteLock();
        try { _data[key] = value; IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    public System.Text.Json.JsonElement? GetData(string key)
    {
        Lock.EnterReadLock();
        try { return _data.TryGetValue(key, out var v) ? v : null; }
        finally { Lock.ExitReadLock(); }
    }
    public List<GameObject> FilterContents(Func<GameObject, bool> pred)
    {
        List<GameObject> res = [];
        Lock.EnterReadLock();
        try
        {
            foreach (var v in _nodes.Values) res.AddRange(ContentUtils.FilterVisible(v.GetContents(), null).Where(pred));
        }
        finally { Lock.ExitReadLock(); }
        return res;
    }
    public Node? GetRandomNode()
    {
        Lock.EnterReadLock();
        try
        {
            if (_nodes.Count == 0) return null;
            var keys = _nodes.Keys.ToList();
            return _nodes[keys[Random.Shared.Next(keys.Count)]];
        }
        finally { Lock.ExitReadLock(); }
    }
    public void AddNode(Node node)
    {        Node? old = null;
        List<NodeLink> linksSnap = [];
        Coord coordSnap = default;
        Lock.EnterWriteLock();
        try
        {
            _nodes.TryGetValue((node.Coord.X, node.Coord.Y), out old);
            _nodes[(node.Coord.X, node.Coord.Y)] = node;
            IsModified = true;
            // Snapshot through the node-locked getter — reading the raw list here
            // (grid lock only) raced a concurrent AddLink into
            // InvalidOperationException mid-enumeration. Grid → node-read nests
            // the same way the load graft does (handler → grid → node,
            // NodeHandler.cs:125-147), so no new lock order is introduced.
            linksSnap = node.GetLinks();
            coordSnap = node.Coord;
        }
        finally { Lock.ExitWriteLock(); }
        if (old is not null && !ReferenceEquals(old, node))
        {
            try { AtherizLogger.LogWarning($"Overwriting node at {node.Coord}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeGrid.AddNode: " + logEx.Message, "NodeGrid"); }
            old.IsDeleted = true;
            ObjectRegistry.RemoveObject(old);
        }
        SyncCrossAreaTransitions(linksSnap, coordSnap, isAdd: true);
    }
    // Lock-free insert for load/hydrate/seed paths that already hold the grid
    // write lock (or run single-threaded at startup): plain dict insert, no
    // overwrite bookkeeping, no transition publish — the caller owns all that.
    internal void AddNodeRaw(Node node)
    {
        _nodes[(node.Coord.X, node.Coord.Y)] = node;
        IsModified = true;
    }
    public void RemoveNode((int X, int Y) coord)
    {
        Node? node = null;
        Lock.EnterWriteLock();
        try { _nodes.Remove(coord, out node); IsModified = true; }
        finally { Lock.ExitWriteLock(); }
        // Enumerate a node-locked snapshot — the raw list used to be walked here
        // with no lock at all.
        var linksSnap = node?.GetLinks() ?? [];
        SyncCrossAreaTransitions(linksSnap, default, isAdd: false);
    }
    // Cross-area transition sync shared by AddNode/RemoveNode, parametrized by
    // direction because the loops differ: adds publish a transition from the
    // node's own coord snapshot, removes withdraw by link coord. Both callers
    // invoke it after releasing the grid write lock, so lock order is
    // identical to the inlined loops it replaces.
    private void SyncCrossAreaTransitions(List<NodeLink> linksSnap, Coord coordSnap, bool isAdd)
    {
        if (linksSnap.Count == 0) return;
        var nh = NodeHandler.GetCurrent();
        if (nh is null) return;
        foreach (var l in linksSnap)
            if (Area != l.Coord.Area)
            {
                if (isAdd) nh.AddTransition(new Transition(coordSnap, l.Coord, l.Name));
                else nh.RemoveTransition(l.Coord);
            }
    }
    public Node? GetNode((int X, int Y) coord)
    {
        Lock.EnterReadLock();
        try { return _nodes.TryGetValue(coord, out var n) ? n : null; }
        finally { Lock.ExitReadLock(); }
    }
    public Node? GetNode(int x, int y) => GetNode((x, y));

    // Shared batch validator for CheckMoves/ApplyMoves (ports nodes.py
    // check_moves/apply_moves validation loops verbatim: same three
    // predicates in the same order). Source multiplicities are precomputed
    // once instead of rescanned per index — identical results, linear time.
    private static HashSet<int> ValidateMoves(
        List<((int X, int Y) src, (int X, int Y) dst)> moves,
        HashSet<(int, int)> occupied)
    {
        HashSet<int> failed = [];
        // Counts built straight from moves: the old sources.Take(i) arm could
        // only fire when a duplicate source existed, which already implies
        // sourceCounts[src] > 1, so it never decided anything on its own.
        // sourceSet stays: it backs the dst-occupied check below.
        Dictionary<(int, int), int> sourceCounts = [];
        foreach (var m in moves)
            sourceCounts[m.src] = sourceCounts.TryGetValue(m.src, out var n) ? n + 1 : 1;
        // Duplicate destinations: two moves into the same empty cell both pass
        // the occupied checks below, then ApplyMoves overwrites — the first
        // node is evicted from the dict but keeps its stale coord (lost).
        // Fail every move sharing a destination, like duplicate sources.
        Dictionary<(int, int), int> destCounts = [];
        foreach (var m in moves)
            destCounts[m.dst] = destCounts.TryGetValue(m.dst, out var dn) ? dn + 1 : 1;
        var sourceSet = new HashSet<(int, int)>(moves.Select(m => m.src));
        for (int i = 0; i < moves.Count; i++)
        {
            var (src, dst) = moves[i];
            if (sourceCounts[src] > 1) { failed.Add(i); continue; }
            if (destCounts[dst] > 1) { failed.Add(i); continue; }
            if (!occupied.Contains(src)) { failed.Add(i); continue; }
            if (occupied.Contains(dst) && !sourceSet.Contains(dst)) failed.Add(i);
        }
        return failed;
    }

    public HashSet<int> CheckMoves(List<((int X, int Y) src, (int X, int Y) dst)> moves, List<((int X, int Y) src, (int X, int Y) dst)>? context = null)
    {
        Lock.EnterReadLock();
        try
        {
            var occupied = new HashSet<(int, int)>(_nodes.Keys);
            if (context is not null)
                foreach (var (cs, cd) in context) { occupied.Remove(cs); occupied.Add(cd); }
            return ValidateMoves(moves, occupied);
        }
        finally { Lock.ExitReadLock(); }
    }

    public List<int> ApplyMoves(List<((int X, int Y) src, (int X, int Y) dst)> moves)
    {
        Dictionary<(int, int), (int, int)> remap = new();
        Dictionary<Coord, Coord> oldToNewFull = new();
        List<(Node node, (int X, int Y) dst)> moved = new();
        HashSet<int> failed = [];
        Dictionary<int, Node> affected = new();
        Lock.EnterWriteLock();
        try
        {
            var occupied = new HashSet<(int, int)>(_nodes.Keys);
            foreach (var i in ValidateMoves(moves, occupied)) failed.Add(i);
            var applied = moves.Where((_, i) => !failed.Contains(i)).ToList();
            if (applied.Count == 0) return failed.ToList();
            foreach (var (src, dst) in applied)
            {
                var node = _nodes[src];
                _nodes.Remove(src);
                moved.Add((node, dst));
                remap[src] = dst;
            }
            IsModified = true;
            foreach (var (node, dst) in moved)
            {
                var oldCoord = node.Coord;
                var newCoord = new Coord(Area, dst.X, dst.Y, Z);
                oldToNewFull[oldCoord] = newCoord;
                node.Coord = newCoord;
                _nodes[dst] = node;
                // Members ride the node — their CoordLocation still names
                // the old cell, so lookups by the new coord ghost them (empty
                // look, stale exits). Remap exact-old-coord members to the new
                // coord; object-inventory members (ObjectLocation) are untouched.
                try
                {
                    foreach (var cid in node.ContentsSnapshot)
                    {
                        var member = ObjectRegistry.GetSingle(cid);
                        if (member is null) continue;
                        if (member.Location is Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation cl
                            && cl.Coord.Equals(oldCoord))
                        {
                            try { member.Location = Atheriz.Core.Persistence.Dto.LocationRef.FromCoord(newCoord); }
                            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeGrid.ApplyMoves member remap: " + logEx.Message, "NodeGrid"); }
                        }
                    }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeGrid.ApplyMoves member scan: " + logEx.Message, "NodeGrid"); }
            }
            // rewrite links inside this grid
            affected = moved.ToDictionary(m => m.node.Id, m => m.node);
            // Direct enumeration: the loop rewrites node internals under node
            // locks and never mutates the grid dict, so no snapshot is needed.
            foreach (var other in _nodes.Values)
            {
                bool rewritten;
                other.NodeLock.EnterWriteLock();
                try { rewritten = other.RemapLinkCoordsNoLock(oldToNewFull); }
                finally { other.NodeLock.ExitWriteLock(); }
                if (rewritten) affected[other.Id] = other;
            }
        }
        finally { Lock.ExitWriteLock(); }

        // door remap via NodeHandler
        var nh = NodeHandler.GetCurrent();
        if (nh is not null && oldToNewFull.Count > 0)
        {
            nh.RemapDoors(oldToNewFull, remap);
            nh.RemapTransitions(oldToNewFull);
        }

        // cross-area transitions
        List<(Node node, NodeLink link)> crossLinks = [];
        Lock.EnterReadLock();
        try
        {
            // Snapshot through the node-locked getter — reading the raw list
            // here (grid lock only) raced a concurrent AddLink into
            // InvalidOperationException mid-enumeration. Grid → node-read
            // nests the same way AddNode does, so no new lock order.
            foreach (var node in _nodes.Values)
                foreach (var link in node.GetLinks())
                    if (link.Coord.Area != Area) crossLinks.Add((node, link));
        }
        finally { Lock.ExitReadLock(); }
        if (nh is not null)
            foreach (var (node, link) in crossLinks) nh.AddTransition(new Transition(node.Coord, link.Coord, link.Name));

        // rebuild ExitCommands
        foreach (var node in affected.Values)
            foreach (var obj in node.GetContents())
                try { node.AddExits(obj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NodeGrid.ApplyMoves: " + logEx.Message, "NodeGrid"); }

        return failed.ToList();
    }

    public void Clear()
    {
        Lock.EnterWriteLock();
        try { _nodes.Clear(); IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
}
