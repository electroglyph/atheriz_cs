using System.Text.Json;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Objects;

// Port of atheriz/objects/nodes.py:1229 NodeArea
public sealed class NodeArea
{
    public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);
    public string Name { get; set; }
    public string? Theme { get; set; }
    public bool IsModified { get; set; } = true;
    public Dictionary<int, NodeGrid> Grids { get; } = new();
    public Dictionary<string, JsonElement> Data { get; set; } = new();
    public HashSet<string>? LinkedAreas { get; set; }

    // Port of nodes.py:1231
    public NodeArea(string name, string? theme = null)
    {
        Name = name;
        Theme = theme;
    }
    public int Count { get { Lock.EnterReadLock(); try { return Grids.Count; } finally { Lock.ExitReadLock(); } } }
    // Port of nodes.py:1242
    public override string ToString()
    {
        Lock.EnterReadLock();
        try
        {
            var grids = string.Join(", ", Grids.Select(kv => $"Grid(z={kv.Key}, len={kv.Value.Count})"));
            return $"Area {Name}: {grids}";
        }
        finally { Lock.ExitReadLock(); }
    }
    public override bool Equals(object? obj)
    {
        if (obj is not NodeArea o) return false;
        return Name == o.Name && Theme == o.Theme && Grids.SequenceEqual(o.Grids) && Data.SequenceEqual(o.Data) && (LinkedAreas == null && o.LinkedAreas == null || LinkedAreas != null && o.LinkedAreas != null && LinkedAreas.SetEquals(o.LinkedAreas));
    }
    public override int GetHashCode() => HashCode.Combine(Name, Theme);

    // Port of nodes.py:1258 get_nodes
    public List<Node> GetNodes(List<(int X, int Y, int Z)> coords)
    {
        var res = new List<Node>();
        Lock.EnterReadLock();
        try
        {
            foreach (var (x, y, z) in coords)
            {
                if (Grids.TryGetValue(z, out var g))
                {
                    var n = g.GetNode(x, y);
                    if (n != null) res.Add(n);
                }
            }
        }
        finally { Lock.ExitReadLock(); }
        return res;
    }
    public List<Node> GetNodes(IEnumerable<Coord> coords)
    {
        var list = coords.Select(c => (c.X, c.Y, c.Z)).ToList();
        return GetNodes(list);
    }

    // Port of nodes.py:1271 get_nodes_in_sphere
    public List<Node> GetNodesInSphere((int X, int Y, int Z) center, double radius, bool ignoreCenter = false)
    {
        const int maxR = 100;
        if (radius < 0 || radius > maxR) throw new ArgumentOutOfRangeException(nameof(radius), $"radius {radius} out of bounds [0, {maxR}]");
        var (cx, cy, cz) = center;
        var r2 = radius * radius;
        var ri = (int)radius;
        var result = new List<Node>();
        Lock.EnterReadLock();
        try
        {
            for (int z = cz - ri; z <= cz + ri; z++)
            {
                int dz = z - cz; if ((double)dz * dz > r2) continue;
                if (!Grids.TryGetValue(z, out var g)) continue;
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
                            if (g.Nodes.TryGetValue((x, y), out var n)) result.Add(n);
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

    // Port of nodes.py:1308 get_rays_in_sphere
    public List<List<Node>> GetRaysInSphere((int X, int Y, int Z) center, double radius, bool ignoreCenter = true)
    {
        var nodes = GetNodesInSphere(center, radius, ignoreCenter);
        var (cx, cy, cz) = center;
        var rays = new Dictionary<(int, int, int), List<(int distSq, Node node)>>();
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
        var result = new List<List<Node>>();
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

    // Port of nodes.py:1333 get_neighbors
    public List<Node> GetNeighbors((int X, int Y, int Z) coord)
    {
        var (x, y, z) = coord;
        var neighbors = new List<Node>();
        Lock.EnterReadLock();
        try
        {
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                if (Grids.TryGetValue(z + dz, out var g))
                {
                    var n = g.GetNode(x + dx, y + dy);
                    if (n != null) neighbors.Add(n);
                }
            }
        }
        finally { Lock.ExitReadLock(); }
        return neighbors;
    }
    public List<Node> GetNeighbors(Coord coord) => GetNeighbors((coord.X, coord.Y, coord.Z));

    // Port of nodes.py:1346 set_data
    public void SetData(string key, JsonElement value)
    {
        Lock.EnterWriteLock();
        try { Data[key] = value; IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    // Port of nodes.py:1352
    public JsonElement? GetData(string key)
    {
        Lock.EnterReadLock();
        try { return Data.TryGetValue(key, out var v) ? v : null; }
        finally { Lock.ExitReadLock(); }
    }
    // Port of nodes.py:1357
    public void RemoveData(string key)
    {
        Lock.EnterWriteLock();
        try { Data.Remove(key); IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    // Port of nodes.py:1362 remove_linked_area
    public void RemoveLinkedArea(string area)
    {
        bool removed = false;
        Lock.EnterWriteLock();
        try
        {
            if (LinkedAreas != null && LinkedAreas.Contains(area))
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
    // Port of nodes.py:1375 add_linked_area
    public void AddLinkedArea(string area)
    {
        bool added = false;
        Lock.EnterWriteLock();
        try
        {
            if (LinkedAreas == null) { LinkedAreas = new HashSet<string> { area }; IsModified = true; added = true; }
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
    // Port of nodes.py:1392 add_grid
    public void AddGrid(NodeGrid grid)
    {
        grid.Area = Name;
        Lock.EnterWriteLock();
        try { Grids[grid.Z] = grid; IsModified = true; }
        finally { Lock.ExitWriteLock(); }
    }
    // Port of nodes.py:1398 get_grid
    public NodeGrid? GetGrid(int z)
    {
        Lock.EnterReadLock();
        try { return Grids.TryGetValue(z, out var g) ? g : null; }
        finally { Lock.ExitReadLock(); }
    }
    public NodeGrid GetOrCreateGrid(int z)
    {
        Lock.EnterWriteLock();
        try
        {
            if (!Grids.TryGetValue(z, out var g))
            {
                g = new NodeGrid(Name, z);
                Grids[z] = g;
                IsModified = true;
            }
            return g;
        }
        finally { Lock.ExitWriteLock(); }
    }
    public NodeGrid GetOrAddGrid(int z) => GetOrCreateGrid(z);
    // Port of nodes.py:1402 remove_grid
    public void RemoveGrid(int z)
    {
        Lock.EnterWriteLock();
        try
        {
            if (Grids.Remove(z, out var m)) m.Clear();
            IsModified = true;
        }
        finally { Lock.ExitWriteLock(); }
    }
    // Port of nodes.py:1409 clear
    public void Clear()
    {
        Lock.EnterWriteLock();
        try
        {
            foreach (var v in Grids.Values) v.Clear();
            Grids.Clear();
            IsModified = true;
        }
        finally { Lock.ExitWriteLock(); }
    }
}
