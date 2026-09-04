using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Utils;

// Port of atheriz/pathfind.py:12 PathNode
internal sealed class PathNode : IComparable<PathNode>
{
    // Port of pathfind.py:13 __init__ parent/position + g/h/f =0
    public PathNode? Parent { get; }
    public Node Position { get; }
    public int G { get; set; }
    public int H { get; set; }
    public int F { get; set; }
    private readonly long _seq;
    private static long _nextSeq;
    public PathNode(PathNode? parent, Node position)
    {
        Parent = parent;
        Position = position;
        G = 0; H = 0; F = 0;
        _seq = System.Threading.Interlocked.Increment(ref _nextSeq);
    }
    // Port of pathfind.py:20 __eq__ via position
    public override bool Equals(object? obj) => obj is PathNode o && Position.Coord.Equals(o.Position.Coord);
    public override int GetHashCode() => Position.Coord.GetHashCode();
    // Port of pathfind.py:23 __lt__/__gt__ via f
    public int CompareTo(PathNode? other)
    {
        if (other is null) return -1;
        int c = F.CompareTo(other.F);
        if (c != 0) return c;
        c = H.CompareTo(other.H);
        if (c != 0) return c;
        return _seq.CompareTo(other._seq);
    }
}

// Port of atheriz/pathfind.py — A* pathfinding
public static class Pathfind
{
    // Port of pathfind.py:30 get_path
    private static List<Node> GetPath(PathNode currentNode)
    {
        var path = new List<Node>();
        var current = currentNode;
        while (current != null)
        {
            path.Add(current.Position);
            current = current.Parent;
        }
        path.Reverse();
        return path;
    }

    // Port of pathfind.py:58 get_link_nodes — without caller (no door check)
    private static List<Node> GetLinkNodes(Node node, NodeHandler handler)
    {
        List<NodeLink> links;
        node.NodeLock.EnterReadLock();
        try { links = node.Links != null ? new List<NodeLink>(node.Links) : []; }
        finally { node.NodeLock.ExitReadLock(); }
        var result = new List<Node>();
        foreach (var l in links)
        {
            var n = handler.GetNode(l.Coord);
            if (n != null) result.Add(n);
        }
        return result;
    }

    // Port of pathfind.py:68 get_link_nodes_caller — with door closed/locked + access checks
    private static List<Node> GetLinkNodesCaller(Node node, NodeHandler handler, GameObject? caller)
    {
        List<NodeLink> links;
        node.NodeLock.EnterReadLock();
        try { links = node.Links != null ? new List<NodeLink>(node.Links) : []; }
        finally { node.NodeLock.ExitReadLock(); }
        if (links.Count == 0) return [];
        var doors = handler.GetDoors(node.Coord); // Port of pathfind.py:73 doors = nh.get_doors(node.coord)
        var result = new List<Node>();
        foreach (var l in links)
        {
            if (doors != null)
            {
                if (doors.TryGetValue(l.Name, out var d) && d != null)
                {
                    bool closed, locked;
                    // Port of pathfind.py:79-85 with d.lock: closed/locked + fallback
                    try
                    {
                        d.Lock.EnterReadLock();
                        try { closed = d.Closed; locked = d.Locked; }
                        finally { d.Lock.ExitReadLock(); }
                    }
                    catch { closed = d.Closed; locked = d.Locked; }
                    if (closed)
                    {
                        // Port of pathfind.py:88-92 locked -> need unlock, else need open
                        if (locked && !d.Access(caller, "unlock")) continue;
                        if (!d.Access(caller, "open")) continue;
                    }
                }
            }
            var n = handler.GetNode(l.Coord);
            if (n != null) result.Add(n);
        }
        return result;
    }

    // Port of pathfind.py:39 astar(start: Node, end: Node, caller: Object|None)
    public static (bool Found, List<Node> Path, List<Coord> ClosedSet) AStar(Node start, Node end, GameObject? caller = null, NodeHandler? handler = null, int? maxIterationsOverride = null)
    {
        var nh = handler ?? NodeHandler.GetCurrent();
        if (nh == null) return (false, [], []);
        // Port of pathfind.py:98 start_node/end_node
        var startNode = new PathNode(null, start);
        startNode.G = startNode.H = startNode.F = 0;
        var endNode = new PathNode(null, end);
        endNode.G = endNode.H = endNode.F = 0;

        // Port of pathfind.py:102 open_list + closed_set + open_by_pos
        var openQueue = new PriorityQueue<PathNode, int>();
        var closedSet = new HashSet<Coord>();
        var openByPos = new Dictionary<Coord, PathNode>();
        int iterations = 0;
        // Port of pathfind.py:106 grid = start.grid
        var grid = start.Grid;
        if (grid == null) return (false, [], []);
        // Port of pathfind.py:109 max_iterations = settings.MAX_ASTAR_ITERATIONS (honor env via AtherizSettings)
        int maxIterations;
        if (maxIterationsOverride.HasValue) maxIterations = maxIterationsOverride.Value;
        else
        {
            // honor MAX_ASTAR_ITERATIONS env override via AtherizSettings
            var env = Environment.GetEnvironmentVariable("MAX_ASTAR_ITERATIONS")
                   ?? Environment.GetEnvironmentVariable("ATHERIZ_MAX_ASTAR_ITERATIONS");
            if (env != null && int.TryParse(env, out var v)) maxIterations = v;
            else
            {
                try { maxIterations = AtherizSettings.Global.MaxAstarIterations; }
                catch { maxIterations = 50000; }
            }
        }
        // Port of pathfind.py:110 heapify + heappush start
        openQueue.Enqueue(startNode, startNode.F);
        openByPos[start.Coord] = startNode;
        var currentNode = startNode;
        // Port of pathfind.py:114 while True:
        while (true)
        {
            iterations += 1;
            // Port of pathfind.py:116 closed_set.add(current.position.coord)
            closedSet.Add(currentNode.Position.Coord);
            // Port of pathfind.py:117 if current.coord == end.coord: return True, get_path...
            if (currentNode.Position.Coord.Equals(endNode.Position.Coord))
                return (true, GetPath(currentNode), closedSet.ToList());
            // Port of pathfind.py:119 if iterations > max_iterations: return False
            if (iterations > maxIterations)
                return (false, [], closedSet.ToList());
            // Port of pathfind.py:121 children = [] + nodes = get_link_nodes(...)
            var children = new List<PathNode>();
            var nodes = caller == null
                ? GetLinkNodes(currentNode.Position, nh) // Port of pathfind.py:122-124 caller is None branch
                : GetLinkNodesCaller(currentNode.Position, nh, caller); // Port of pathfind.py:125 else branch
            foreach (var n in nodes)
            {
                var node = new PathNode(currentNode, n);
                children.Add(node);
            }
            // Port of pathfind.py:130 for child in children:
            foreach (var child in children)
            {
                // Port of pathfind.py:131 if child.coord in closed_set: continue
                if (closedSet.Contains(child.Position.Coord)) continue;
                // Port of pathfind.py:133 child.g = current.g +1
                child.G = currentNode.G + 1;
                // Port of pathfind.py:134 if same area else 0
                if (child.Position.Coord.Area == endNode.Position.Coord.Area)
                {
                    // Port of pathfind.py:135-139 Manhattan h
                    child.H = Math.Abs(child.Position.Coord.X - endNode.Position.Coord.X)
                            + Math.Abs(child.Position.Coord.Y - endNode.Position.Coord.Y)
                            + Math.Abs(child.Position.Coord.Z - endNode.Position.Coord.Z);
                }
                else child.H = 0;
                // Port of pathfind.py:142 child.f = g+h
                child.F = child.G + child.H;
                // Port of pathfind.py:143 existing = open_by_pos.get(...)
                if (openByPos.TryGetValue(child.Position.Coord, out var existing))
                {
                    // Port of pathfind.py:145 if child.g < existing.g: push + update
                    if (child.G < existing.G)
                    {
                        openQueue.Enqueue(child, child.F);
                        openByPos[child.Position.Coord] = child;
                    }
                }
                else
                {
                    // Port of pathfind.py:148-150 else push
                    openQueue.Enqueue(child, child.F);
                    openByPos[child.Position.Coord] = child;
                }
            }
            // Port of pathfind.py:151 if len(open_list)==0: return False
            if (openQueue.Count == 0)
                return (false, [], closedSet.ToList());
            // Port of pathfind.py:153 current = heappop
            currentNode = openQueue.Dequeue();
            // Port of pathfind.py:154 while open_by_pos.get(...) is not current: pop next or return False
            while (!openByPos.TryGetValue(currentNode.Position.Coord, out var cur) || !ReferenceEquals(cur, currentNode))
            {
                if (openQueue.Count == 0) return (false, [], closedSet.ToList());
                currentNode = openQueue.Dequeue();
            }
            // Port of pathfind.py:158 del open_by_pos[current.coord]
            openByPos.Remove(currentNode.Position.Coord);
        }
    }

    // Spec wrapper — Port of pathfind.py:39 FindPath(Coord start, Coord goal, handler, maxIterations=50000)
    public static List<Coord>? FindPath(Coord start, Coord goal, NodeHandler handler, int maxIterations = 50000)
        => FindPath(start, goal, handler, null, maxIterations);

    // Overload with caller for door-aware pathfind — preserves pathfind.py:39 caller param
    public static List<Coord>? FindPath(Coord start, Coord goal, NodeHandler handler, GameObject? caller, int maxIterations = 50000)
    {
        // Port of pathfind.py:39 + honors MAX_ASTAR_ITERATIONS via AtherizSettings
        if (handler == null) handler = NodeHandler.GetCurrent()!;
        if (handler == null) return null;
        var s = handler.GetNode(start);
        var e = handler.GetNode(goal);
        if (s == null || e == null) return null;
        // honor env override if default untouched
        int effective = maxIterations;
        if (maxIterations == 50000)
        {
            var env = Environment.GetEnvironmentVariable("MAX_ASTAR_ITERATIONS")
                   ?? Environment.GetEnvironmentVariable("ATHERIZ_MAX_ASTAR_ITERATIONS");
            if (env != null && int.TryParse(env, out var v)) effective = v;
            else
            {
                try
                {
                    var cfg = AtherizSettings.Global.MaxAstarIterations;
                    if (cfg != 50000) effective = cfg;
                }
                catch { }
            }
        }
        var (found, path, _) = AStar(s, e, caller, handler, effective);
        if (!found) return null;
        return path.Select(n => n.Coord).ToList();
    }

    // Port of pathfind.py neighbors via Links + doors
    public static List<Coord> GetNeighbors(Coord c, NodeHandler? handler = null)
    {
        var nh = handler ?? NodeHandler.GetCurrent();
        if (nh == null) return [];
        var node = nh.GetNode(c);
        if (node == null) return [];
        var neighbors = GetLinkNodes(node, nh);
        return neighbors.Select(n => n.Coord).ToList();
    }

    // ClosedSet helper expsed for spec: HashSet<Coord> kept internally in AStar
}
