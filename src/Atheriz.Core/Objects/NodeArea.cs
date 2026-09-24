
namespace Atheriz.Core.Objects;

public sealed class NodeArea
{
    public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);
    public string Name { get; set; }
    public string? Theme { get; set; }
    public bool IsModified { get; set; } = true;
    // Snapshot copies: the getters return copies under the read lock, so a
    // reader enumerating the copy never races a concurrent mutator. Lock-held
    // internals below touch _grids/_data directly.
    private Dictionary<int, NodeGrid> _grids = new();
    public Dictionary<int, NodeGrid> Grids
    {
        get { Lock.EnterReadLock(); try { return new Dictionary<int, NodeGrid>(_grids); } finally { Lock.ExitReadLock(); } }
    }
    private Dictionary<string, JsonElement> _data = new();
    public Dictionary<string, JsonElement> Data
    {
        get { Lock.EnterReadLock(); try { return new Dictionary<string, JsonElement>(_data); } finally { Lock.ExitReadLock(); } }
        set { Lock.EnterWriteLock(); try { _data = value is null ? new() : new Dictionary<string, JsonElement>(value); IsModified = true; } finally { Lock.ExitWriteLock(); } }
    }
    public HashSet<string>? LinkedAreas { get; set; }

    public NodeArea(string name, string? theme = null)
    {
        Name = name;
        Theme = theme;
    }
    public int Count { get { Lock.EnterReadLock(); try { return _grids.Count; } finally { Lock.ExitReadLock(); } } }
    public override string ToString()
    {
        Lock.EnterReadLock();
        try
        {
            var grids = string.Join(", ", _grids.Select(kv => $"Grid(z={kv.Key}, len={kv.Value.Count})"));
            return $"Area {Name}: {grids}";
        }
        finally { Lock.ExitReadLock(); }
    }
// Python dict == is order-insensitive; JsonElement has no
    // value equality in C#, so data compares by canonical raw text. Hash combines the
    // same components in sorted order so equal areas hash equal.
    public override bool Equals(object? obj)
    {
        if (obj is not NodeArea o) return false;
        if (Name != o.Name || Theme != o.Theme) return false;
        // Snapshot each side under its own read lock (taking both area locks
        // here would order them arbitrarily).
        var myGrids = Grids; var otherGrids = o.Grids;
        if (myGrids.Count != otherGrids.Count) return false;
        var myData = Data; var otherData = o.Data;
        if (myData.Count != otherData.Count) return false;
        foreach (var kv in myGrids)
            if (!otherGrids.TryGetValue(kv.Key, out var g) || !kv.Value.Equals(g)) return false;
        foreach (var kv in myData)
            if (!otherData.TryGetValue(kv.Key, out var je) || kv.Value.GetRawText() != je.GetRawText()) return false;
        return (LinkedAreas is null && o.LinkedAreas is null) || (LinkedAreas is not null && o.LinkedAreas is not null && LinkedAreas.SetEquals(o.LinkedAreas));
    }
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Name);
        h.Add(Theme);
        var grids = Grids; var data = Data;
        foreach (var k in grids.Keys.OrderBy(k => k)) { h.Add(k); h.Add(grids[k]); }
        foreach (var k in data.Keys.OrderBy(k => k, StringComparer.Ordinal)) { h.Add(k); h.Add(data[k].GetRawText()); }
        if (LinkedAreas is not null) foreach (var a in LinkedAreas.OrderBy(a => a, StringComparer.Ordinal)) h.Add(a);
        return h.ToHashCode();
    }

    public List<Node> GetNodes(IEnumerable<(int X, int Y, int Z)> coords)
    {
        List<Node> res = [];
        Lock.EnterReadLock();
        try
        {
            foreach (var (x, y, z) in coords)
            {
                if (_grids.TryGetValue(z, out var g))
                {
                    var n = g.GetNode(x, y);
                    if (n is not null) res.Add(n);
                }
            }
        }
        finally { Lock.ExitReadLock(); }
        return res;
    }
    public List<Node> GetNodes(IEnumerable<Coord> coords)
    {
        return GetNodes(coords.Select(c => (c.X, c.Y, c.Z)));
    }

    public List<Node> GetNodesInSphere((int X, int Y, int Z) center, double radius, bool ignoreCenter = false)
    {
        const int maxR = 100;
        if (radius < 0 || radius > maxR) throw new ArgumentOutOfRangeException(nameof(radius), $"radius {radius} out of bounds [0, {maxR}]");
        var (cx, cy, cz) = center;
        var r2 = radius * radius;
        var ri = (int)radius;
        List<Node> result = [];
        Lock.EnterReadLock();
        try
        {
            for (int z = cz - ri; z <= cz + ri; z++)
            {
                int dz = z - cz; if ((double)dz * dz > r2) continue;
                if (!_grids.TryGetValue(z, out var g)) continue;
                double maxDxy2 = r2 - dz * dz;
                int maxDxy = (int)Math.Sqrt(maxDxy2);
                g.Lock.EnterReadLock();
                try
                {
                    for (int x = cx - maxDxy; x <= cx + maxDxy; x++)
                    {
                        int dx2 = (x - cx) * (x - cx);
                        double remaining = maxDxy2 - dx2;
                        if (remaining < 0) continue;
                        int maxDy = (int)Math.Sqrt(remaining);
                        for (int y = cy - maxDy; y <= cy + maxDy; y++)
                        {
                            if (ignoreCenter && x == cx && y == cy && z == cz) continue;
                            // Locked GetNode (the grid lock is already held,
                            // SupportsRecursion) instead of touching the live dict.
                            if (g.GetNode(x, y) is { } n) result.Add(n);
                        }
                    }
                }
                finally { g.Lock.ExitReadLock(); }
            }
        }
        finally { Lock.ExitReadLock(); }
        return result;
    }
    public List<Node> GetNodesInSphere(Coord center, int radius, bool ignoreCenter = false)
        => GetNodesInSphere((center.X, center.Y, center.Z), radius, ignoreCenter);

    public List<List<Node>> GetRaysInSphere((int X, int Y, int Z) center, double radius, bool ignoreCenter = true)
    {
        var nodes = GetNodesInSphere(center, radius, ignoreCenter);
        var (cx, cy, cz) = center;
        Dictionary<(int, int, int), List<(int distSq, Node node)>> rays = [];
        foreach (var n in nodes)
        {
            int nx = n.Coord.X, ny = n.Coord.Y, nz = n.Coord.Z;
            int dx = nx - cx, dy = ny - cy, dz = nz - cz;
            if (dx == 0 && dy == 0 && dz == 0) continue;
            int g = Gcd(Gcd(Math.Abs(dx), Math.Abs(dy)), Math.Abs(dz));
            var dir = (dx / g, dy / g, dz / g);
            int distSq = dx * dx + dy * dy + dz * dz;
            if (!rays.TryGetValue(dir, out var bucket)) { bucket = []; rays[dir] = bucket; }
            bucket.Add((distSq, n));
        }
        List<List<Node>> result = [];
        foreach (var bucket in rays.Values)
        {
            bucket.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            result.Add(bucket.Select(t => t.node).ToList());
        }
        return result;
    }
    public List<List<Node>> GetRaysInSphere(Coord center, int radius, bool ignoreCenter = true)
        => GetRaysInSphere((center.X, center.Y, center.Z), radius, ignoreCenter);

    private static int Gcd(int a, int b) { while (b != 0) { int t = b; b = a % b; a = t; } return a == 0 ? 1 : a; }

    // Six face-neighbor offsets shared by every GetNeighbors call. Private and
    // never exposed: the method builds a fresh result list from it per call,
    // so no caller can observe or mutate the table.
    private static readonly (int Dx, int Dy, int Dz)[] NeighborOffsets = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    public List<Node> GetNeighbors((int X, int Y, int Z) coord)
    {
        var (x, y, z) = coord;
        List<Node> neighbors = [];
        Lock.EnterReadLock();
        try
        {
            foreach (var (dx, dy, dz) in NeighborOffsets)
            {
                if (_grids.TryGetValue(z + dz, out var g))
                {
                    var n = g.GetNode(x + dx, y + dy);
                    if (n is not null) neighbors.Add(n);
                }
            }
        }
        finally { Lock.ExitReadLock(); }
        return neighbors;
    }
    public List<Node> GetNeighbors(Coord coord) => GetNeighbors((coord.X, coord.Y, coord.Z));

    public void SetData(string key, JsonElement value)
    {
        Lock.EnterWriteLock();
        try { _data[key] = value; IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    public JsonElement? GetData(string key)
    {
        Lock.EnterReadLock();
        try { return _data.TryGetValue(key, out var v) ? v : null; }
        finally { Lock.ExitReadLock(); }
    }
    public void RemoveData(string key)
    {
        Lock.EnterWriteLock();
        try { _data.Remove(key); IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    public void RemoveLinkedArea(string area)
    {
        bool removed = false;
        Lock.EnterWriteLock();
        try
        {
            if (LinkedAreas is not null && LinkedAreas.Contains(area))
            {
                LinkedAreas.Remove(area);
                IsModified = true;
                removed = true;
            }
        }
        finally { Lock.ExitWriteLock(); }
        if (removed)
        {
            var nh = NodeHandler.GetCurrent();
            var a = nh?.GetArea(area);
            a?.RemoveLinkedArea(Name);
        }
    }
    public void AddLinkedArea(string area)
    {
        bool added = false;
        Lock.EnterWriteLock();
        try
        {
            if (LinkedAreas is null) { LinkedAreas = new HashSet<string> { area }; IsModified = true; added = true; }
            else if (!LinkedAreas.Contains(area)) { LinkedAreas.Add(area); IsModified = true; added = true; }
        }
        finally { Lock.ExitWriteLock(); }
        if (added)
        {
            var nh = NodeHandler.GetCurrent();
            var a = nh?.GetArea(area);
            a?.AddLinkedArea(Name);
        }
    }
    public void AddGrid(NodeGrid grid)
    {
        grid.Area = Name;
        Lock.EnterWriteLock();
        try { _grids[grid.Z] = grid; IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    // Lock-free insert for load/hydrate paths that already hold the area
    // write lock (or run single-threaded at startup): sets the back-pointer
    // and inserts, no locking.
    internal void AddGridRaw(NodeGrid grid)
    {
        grid.Area = Name;
        _grids[grid.Z] = grid;
        IsModified = true;
    }
    public NodeGrid? GetGrid(int z)
    {
        Lock.EnterReadLock();
        try { return _grids.TryGetValue(z, out var g) ? g : null; }
        finally { Lock.ExitReadLock(); }
    }
    public NodeGrid GetOrAddGrid(int z)
    {
        Lock.EnterUpgradeableReadLock();
        try
        {
            if (!_grids.TryGetValue(z, out var g))
            {
                Lock.EnterWriteLock();
                try
                {
                    if (!_grids.TryGetValue(z, out g))
                    {
                        g = new NodeGrid(Name, z);
                        _grids[z] = g;
                        IsModified = true;
                    }
                }
                finally { Lock.ExitWriteLock(); }
            }
            return g;
        }
        finally { Lock.ExitUpgradeableReadLock(); }
    }
    public void RemoveGrid(int z)
    {
        Lock.EnterWriteLock();
        try
        {
            if (_grids.Remove(z, out var m)) m.Clear();
            IsModified = true;
        }
        finally { Lock.ExitWriteLock(); }
    }
    public void Clear()
    {
        Lock.EnterWriteLock();
        try
        {
            foreach (var v in _grids.Values) v.Clear();
            _grids.Clear();
            IsModified = true;
        }
        finally { Lock.ExitWriteLock(); }
    }
}
