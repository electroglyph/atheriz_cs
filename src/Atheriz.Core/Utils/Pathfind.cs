
namespace Atheriz.Core.Utils;

// A* pathfinding
public static class Pathfind
{
    private static List<Node> GetPath(PathNode currentNode)
    {
        List<Node> path = [];
        var current = currentNode;
        while (current is not null)
        {
            path.Add(current.Position);
            current = current.Parent;
        }
        path.Reverse();
        return path;
    }

    // Shared NodeLock snapshot + resolve core for both GetLinkNodes variants.
    // Door filtering stays on the caller path — the two variants filter
    // differently, so only the snapshot + resolve is shared.
    private static List<NodeLink> SnapshotLinks(Node node)
    {
        node.NodeLock.EnterReadLock();
        try { return node.Links is not null ? new List<NodeLink>(node.Links) : []; }
        finally { node.NodeLock.ExitReadLock(); }
    }

    // Shared override-or-global-or-50000 cap resolution for AStar + FindPath.
    private static int ResolveMaxIterations(int? maxIterationsOverride)
    {
        if (maxIterationsOverride.HasValue) return maxIterationsOverride.Value;
        try { return AtherizSettings.Global.MaxAstarIterations; }
        catch { return 50000; }
    }

    private static List<Node> GetLinkNodes(Node node, NodeHandler handler)
    {
        var links = SnapshotLinks(node);
        List<Node> result = [];
        foreach (var l in links)
        {
            var n = handler.GetNode(l.Coord);
            if (n is not null) result.Add(n);
        }
        return result;
    }

    private static List<Node> GetLinkNodesCaller(Node node, NodeHandler handler, GameObject? caller)
    {
        var links = SnapshotLinks(node);
        if (links.Count == 0) return [];
        var doors = handler.GetDoors(node.Coord);
        List<Node> result = [];
        foreach (var l in links)
        {
            if (doors is not null)
            {
                if (doors.TryGetValue(l.Name, out var d) && d is not null)
                {
                    bool closed, locked;
                    try
                    {
                        d.Lock.EnterReadLock();
                        try { closed = d.Closed; locked = d.Locked; }
                        finally { d.Lock.ExitReadLock(); }
                    }
                    catch { closed = d.Closed; locked = d.Locked; }
                    if (closed)
                    {
                        if (locked && !d.Access(caller, "unlock")) continue;
                        if (!d.Access(caller, "open")) continue;
                    }
                }
            }
            var n = handler.GetNode(l.Coord);
            if (n is not null) result.Add(n);
        }
        return result;
    }

    public static (bool Found, List<Node> Path, List<Coord> ClosedSet) AStar(Node start, Node end, GameObject? caller = null, NodeHandler? handler = null, int? maxIterationsOverride = null)
    {
        var nh = handler ?? NodeHandler.GetCurrent();
        if (nh is null) return (false, [], []);
        var startNode = new PathNode(null, start);
        var endNode = new PathNode(null, end);

        // The queue orders nodes via CompareTo, like heapq via __lt__.
        var openQueue = new PriorityQueue<PathNode, PathNode>();
        HashSet<Coord> closedSet = [];
        Dictionary<Coord, PathNode> openByPos = [];
        int iterations = 0;
        var grid = start.Grid;
        if (grid is null) return (false, [], []);
        int maxIterations = ResolveMaxIterations(maxIterationsOverride);
        openQueue.Enqueue(startNode, startNode);
        openByPos[start.Coord] = startNode;
        // The start entry is dequeued up front so the loop body below always
        // works on a queue-issued node, never a pre-loop stand-in.
        var currentNode = openQueue.Dequeue();
        while (true)
        {
            iterations += 1;
            closedSet.Add(currentNode.Position.Coord);
            if (currentNode.Position.Coord.Equals(endNode.Position.Coord))
                return (true, GetPath(currentNode), closedSet.ToList());
            if (iterations > maxIterations)
                return (false, [], closedSet.ToList());
            // door-aware expansion otherwise — AStar and GetNeighbors share
            // this dispatch so neighbor lists agree with pathfinding.
            // Children are scored inline in expansion order (no intermediate
            // list): A* correctness depends on visiting neighbors in link order.
            var nodes = caller is null
                ? GetLinkNodes(currentNode.Position, nh)
                : GetLinkNodesCaller(currentNode.Position, nh, caller);
            foreach (var n in nodes)
            {
                var child = new PathNode(currentNode, n);
                if (closedSet.Contains(child.Position.Coord)) continue;
                child.G = currentNode.G + 1;
                if (child.Position.Coord.Area == endNode.Position.Coord.Area)
                {
                    child.H = Math.Abs(child.Position.Coord.X - endNode.Position.Coord.X)
                            + Math.Abs(child.Position.Coord.Y - endNode.Position.Coord.Y)
                            + Math.Abs(child.Position.Coord.Z - endNode.Position.Coord.Z);
                }
                else child.H = 0;
                child.F = child.G + child.H;
                if (openByPos.TryGetValue(child.Position.Coord, out var existing))
                {
                    if (child.G < existing.G)
                    {
                        openQueue.Enqueue(child, child);
                        openByPos[child.Position.Coord] = child;
                    }
                }
                else
                {
                    openQueue.Enqueue(child, child);
                    openByPos[child.Position.Coord] = child;
                }
            }
            if (openQueue.Count == 0)
                return (false, [], closedSet.ToList());
            currentNode = openQueue.Dequeue();
            while (!openByPos.TryGetValue(currentNode.Position.Coord, out var cur) || !ReferenceEquals(cur, currentNode))
            {
                if (openQueue.Count == 0) return (false, [], closedSet.ToList());
                currentNode = openQueue.Dequeue();
            }
            openByPos.Remove(currentNode.Position.Coord);
        }
    }

    public static List<Coord>? FindPath(Coord start, Coord goal, NodeHandler handler, int? maxIterations = null)
        => FindPath(start, goal, handler, null, maxIterations);

    // Overload with caller for door-aware pathfind — preserves pathfind.py:39 caller param
    public static List<Coord>? FindPath(Coord start, Coord goal, NodeHandler handler, GameObject? caller, int? maxIterations = null)
    {
        if (handler is null) handler = NodeHandler.GetCurrent()!;
        if (handler is null) return null;
        var s = handler.GetNode(start);
        var e = handler.GetNode(goal);
        if (s is null || e is null) return null;
// an explicit cap wins; otherwise the
        // configured value (null marks "no explicit cap", so any configured
        // value, including the default, is honored as-is).
        int effective = ResolveMaxIterations(maxIterations);
        var (found, path, _) = AStar(s, e, caller, handler, effective);
        if (!found) return null;
        return path.Select(n => n.Coord).ToList();
    }

    // Neighbor listing shares AStar's caller dispatch (blind for
    // caller=None, door-aware otherwise) so the two never diverge.
    public static List<Coord> GetNeighbors(Coord c, NodeHandler? handler = null, GameObject? caller = null)
    {
        var nh = handler ?? NodeHandler.GetCurrent();
        if (nh is null) return [];
        var node = nh.GetNode(c);
        if (node is null) return [];
        var neighbors = caller is null
            ? GetLinkNodes(node, nh)
            : GetLinkNodesCaller(node, nh, caller);
        return neighbors.Select(n => n.Coord).ToList();
    }

    // ClosedSet helper expsed for spec: HashSet<Coord> kept internally in AStar
}
