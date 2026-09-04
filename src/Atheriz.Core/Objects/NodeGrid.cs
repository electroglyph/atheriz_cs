using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Objects;

// Port of atheriz/objects/nodes.py:969 NodeGrid
public sealed class NodeGrid
{
    public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);
    public string Area { get; set; }
    public int Z { get; set; }
    public bool IsModified { get; set; } = true;
    public Dictionary<(int X, int Y), Node> Nodes { get; } = new();
    public Dictionary<string, System.Text.Json.JsonElement> Data { get; set; } = new();

    // Port of nodes.py:971
    public NodeGrid(string area, int z, Dictionary<string, System.Text.Json.JsonElement>? data = null)
    {
        Area = area;
        Z = z;
        if (data != null) Data = data;
    }
    // Port of nodes.py:979
    public override string ToString() => $"NodeGrid(z={Z}, area={Area})";
    // Port of nodes.py:987 — Python dict == is order-insensitive; JsonElement has no
    // value equality in C#, so data compares by canonical raw text. Hash combines the
    // same components in sorted order so equal grids hash equal.
    public override bool Equals(object? obj)
    {
        if (obj is not NodeGrid o) return false;
        if (Area != o.Area || Z != o.Z) return false;
        if (Nodes.Count != o.Nodes.Count || Data.Count != o.Data.Count) return false;
        foreach (var kv in Nodes)
            if (!o.Nodes.TryGetValue(kv.Key, out var n) || !kv.Value.Equals(n)) return false;
        foreach (var kv in Data)
            if (!o.Data.TryGetValue(kv.Key, out var je) || kv.Value.GetRawText() != je.GetRawText()) return false;
        return true;
    }
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Area);
        h.Add(Z);
        foreach (var k in Nodes.Keys.OrderBy(k => k)) { h.Add(k); h.Add(Nodes[k]); }
        foreach (var k in Data.Keys.OrderBy(k => k, StringComparer.Ordinal)) { h.Add(k); h.Add(Data[k].GetRawText()); }
        return h.ToHashCode();
    }
    // Port of nodes.py:987
    public int Count { get { Lock.EnterReadLock(); try { return Nodes.Count; } finally { Lock.ExitReadLock(); } } }
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
            foreach (var k in Nodes.Keys.ToList())
                if (Nodes[k] is Node cur && cur.Id == replacement.Id && !ReferenceEquals(cur, replacement))
                    Nodes[k] = replacement;
        }
        finally { Lock.ExitWriteLock(); }
    }
    // Port of nodes.py:990
    public void SetData(string key, System.Text.Json.JsonElement value)
    {
        Lock.EnterWriteLock();
        try { Data[key] = value; IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    // Port of nodes.py:996
    public System.Text.Json.JsonElement? GetData(string key)
    {
        Lock.EnterReadLock();
        try { return Data.TryGetValue(key, out var v) ? v : null; }
        finally { Lock.ExitReadLock(); }
    }
    // Port of nodes.py:1001
    public List<GameObject> FilterContents(Func<GameObject, bool> pred)
    {
        var res = new List<GameObject>();
        Lock.EnterReadLock();
        try
        {
            foreach (var v in Nodes.Values) res.AddRange(ContentUtils.FilterVisible(v.GetContents(), null).Where(pred));
        }
        finally { Lock.ExitReadLock(); }
        return res;
    }
    // Port of nodes.py:1008
    public Node? GetRandomNode()
    {
        Lock.EnterReadLock();
        try
        {
            if (Nodes.Count == 0) return null;
            var keys = Nodes.Keys.ToList();
            return Nodes[keys[Random.Shared.Next(keys.Count)]];
        }
        finally { Lock.ExitReadLock(); }
    }
    // Port of nodes.py:1015
    public void AddNode(Node node)
    {
        Node? old = null;
        List<NodeLink> linksSnap = [];
        Coord coordSnap = default;
        Lock.EnterWriteLock();
        try
        {
            Nodes.TryGetValue((node.Coord.X, node.Coord.Y), out old);
            Nodes[(node.Coord.X, node.Coord.Y)] = node;
            IsModified = true;
            if (node.Links.Count > 0) linksSnap = node.Links.ToList();
            coordSnap = node.Coord;
        }
        finally { Lock.ExitWriteLock(); }
        if (old != null && !ReferenceEquals(old, node))
        {
            try { Console.Error.WriteLine($"Warning: overwriting node at {(node.Coord.X, node.Coord.Y)}"); } catch { }
            try { AtherizLogger.LogWarning($"Overwriting node at {node.Coord}"); } catch { }
            old.IsDeleted = true;
            ObjectRegistry.RemoveObject(old);
        }
        if (linksSnap.Count > 0)
        {
            var nh = NodeHandler.GetCurrent();
            if (nh != null)
                foreach (var l in linksSnap)
                    if (Area != l.Coord.Area)
                        nh.AddTransition(new Transition(coordSnap, l.Coord, l.Name));
        }
    }
    // Port of nodes.py:1044
    public void RemoveNode((int X, int Y) coord)
    {
        Node? node = null;
        Lock.EnterWriteLock();
        try { Nodes.Remove(coord, out node); IsModified = true; }
        finally { Lock.ExitWriteLock(); }
        if (node != null && node.Links.Count > 0)
        {
            var nh = NodeHandler.GetCurrent();
            if (nh != null)
                foreach (var l in node.Links)
                    if (Area != l.Coord.Area) nh.RemoveTransition(l.Coord);
        }
    }
    // Port of nodes.py:1055
    public Node? GetNode((int X, int Y) coord)
    {
        Lock.EnterReadLock();
        try { return Nodes.TryGetValue(coord, out var n) ? n : null; }
        finally { Lock.ExitReadLock(); }
    }
    public Node? GetNode(int x, int y) => GetNode((x, y));

    // Port of nodes.py:1059 check_moves
    public HashSet<int> CheckMoves(List<((int X, int Y) src, (int X, int Y) dst)> moves, List<((int X, int Y) src, (int X, int Y) dst)>? context = null)
    {
        var failed = new HashSet<int>();
        Lock.EnterReadLock();
        try
        {
            var occupied = new HashSet<(int, int)>(Nodes.Keys);
            if (context != null)
                foreach (var (cs, cd) in context) { occupied.Remove(cs); occupied.Add(cd); }
            var sources = moves.Select(m => m.src).ToList();
            for (int i = 0; i < moves.Count; i++)
            {
                var (src, dst) = moves[i];
                if (sources.Take(i).Contains(src) || sources.Count(s => s.Equals(src)) > 1) { failed.Add(i); continue; }
                if (!occupied.Contains(src)) { failed.Add(i); continue; }
                if (occupied.Contains(dst) && !sources.Contains(dst)) failed.Add(i);
            }
        }
        finally { Lock.ExitReadLock(); }
        return failed;
    }

    // Port of nodes.py:1095 apply_moves
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
            var occupied = new HashSet<(int, int)>(Nodes.Keys);
            var sources = moves.Select(m => m.src).ToList();
            for (int i = 0; i < moves.Count; i++)
            {
                var (src, dst) = moves[i];
                if (sources.Take(i).Contains(src) || sources.Count(s => s.Equals(src)) > 1) { failed.Add(i); continue; }
                if (!occupied.Contains(src)) { failed.Add(i); continue; }
                if (occupied.Contains(dst) && !sources.Contains(dst)) { failed.Add(i); continue; }
            }
            var applied = moves.Where((_, i) => !failed.Contains(i)).ToList();
            if (applied.Count == 0) return failed.ToList();
            foreach (var (src, dst) in applied)
            {
                var node = Nodes[src];
                Nodes.Remove(src);
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
                Nodes[dst] = node;
            }
            // rewrite links inside this grid
            affected = moved.ToDictionary(m => m.node.Id, m => m.node);
            foreach (var other in Nodes.Values.ToList())
            {
                bool rewritten = false;
                other.NodeLock.EnterWriteLock();
                try
                {
                    for (int i = 0; i < other.Links.Count; i++)
                    {
                        var link = other.Links[i];
                        if (oldToNewFull.TryGetValue(link.Coord, out var hit))
                        {
                            other.Links[i] = new NodeLink(link.Name, hit, link.Aliases);
                            rewritten = true;
                        }
                    }
                    if (rewritten) other.IsModified = true;
                }
                finally { other.NodeLock.ExitWriteLock(); }
                if (rewritten) affected[other.Id] = other;
            }
        }
        finally { Lock.ExitWriteLock(); }

        // door remap via NodeHandler
        var nh = NodeHandler.GetCurrent();
        if (nh != null && oldToNewFull.Count > 0)
        {
            nh.RemapDoors(oldToNewFull, remap);
            nh.RemapTransitions(oldToNewFull);
        }

        // cross-area transitions
        List<(Node node, NodeLink link)> crossLinks = [];
        Lock.EnterReadLock();
        try
        {
            foreach (var node in Nodes.Values)
                foreach (var link in node.Links)
                    if (link.Coord.Area != Area) crossLinks.Add((node, link));
        }
        finally { Lock.ExitReadLock(); }
        if (nh != null)
            foreach (var (node, link) in crossLinks) nh.AddTransition(new Transition(node.Coord, link.Coord, link.Name));

        // rebuild ExitCommands
        foreach (var node in affected.Values)
            foreach (var obj in node.GetContents())
                try { node.AddExits(obj); } catch { }

        return failed.ToList();
    }

    // Port of nodes.py:1214
    public void Clear()
    {
        Lock.EnterWriteLock();
        try { Nodes.Clear(); }
        finally { Lock.ExitWriteLock(); }
    }
}
