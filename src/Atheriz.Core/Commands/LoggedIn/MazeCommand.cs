// Port of atheriz/commands/loggedin/maze.py:17 — maze generation with legend and threadpool pathfind
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class MazeCommand : Command
{
    public override string Key => "maze";
    public override string Desc => "Generate a maze.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    public override bool UseParser => true;

    // For tests to inject pool
    public static Func<AsyncThreadPool?> ThreadPoolFactory = () => { try { return GlobalServices.GetAsyncThreadPool(); } catch { return null; } };
    public static Func<MapHandler> MapHandlerFactory = () =>
    {
        try { return GlobalServices.GetMapHandler(); }
        catch
        {
            // Same DB-discipline rule as the node fallback below: empty, never load.
            try { Atheriz.Core.AtherizLogger.LogWarning("MazeCommand: no map handler available; using an empty one (world may be incomplete).", "MazeCommand"); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed MazeCommand.MapHandlerFactory: " + logEx.Message, "MazeCommand"); }
            return new MapHandler(autoLoad:false);
        }
    };
    public static Func<NodeHandler> NodeHandlerFactory = () =>
    {
        try { return GlobalServices.GetNodeHandler(); }
        catch
        {
            // DB-discipline rule (AGENTS.md): never load mid-game. The fallback used to
            // autoLoad:true here (full DB load); an empty handler plus a loud warning
            // is the honest shape when no world exists.
            try { Atheriz.Core.AtherizLogger.LogWarning("MazeCommand: no node handler available; using an empty one (world may be incomplete).", "MazeCommand"); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed MazeCommand.NodeHandlerFactory: " + logEx.Message, "MazeCommand"); }
            return NodeHandler.GetCurrent() ?? new NodeHandler(autoLoad:false);
        }
    };

    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int width = 30, height = 30;
        var tuple1 = GenMapAndGrid(width, height, "maze1");
        var tuple2 = GenMapAndGrid(width, height, "maze2");
        var tuple3 = GenMapAndGrid(width, height, "maze3");
        int rooms = tuple1.grid.Nodes.Count + tuple2.grid.Nodes.Count + tuple3.grid.Nodes.Count;
        sw.Stop();
        go.Msg($"created 3 {width} x {height} mazes, {rooms} rooms, and lots of exits in: {sw.Elapsed.TotalMilliseconds:F2} milliseconds");
        // Use global singleton directly (faithful to get_node_handler/get_map_handler) – retains limbo maps
        NodeHandler nh;
        MapHandler mh;
        try
        {
            var globalNh = GlobalServices.GetNodeHandler();
            nh = ResolveHandler(globalNh, NodeHandlerFactory);
            if (!ReferenceEquals(nh, globalNh))
            {
                // Test injected custom handler via factory – honour it (keeps MazeMapsStoredInPreGrid passing)
                GlobalServices.SetNodeHandler(nh);
            }
        }
        catch { nh = NodeHandler.GetCurrent() ?? NodeHandlerFactory(); }
        // Ensure singleton set without losing existing areas
        NodeHandler.SetCurrent(nh);
        GlobalServices.SetNodeHandler(nh);
        var area1 = new NodeArea("maze1");
        var area2 = new NodeArea("maze2");
        var area3 = new NodeArea("maze3");
        area1.AddGrid(tuple1.grid);
        area2.AddGrid(tuple2.grid);
        area3.AddGrid(tuple3.grid);
        // Replace (not blind add): a repeat run must evict the prior
        // generation's nodes from the registry instead of orphaning them.
        nh.ReplaceArea(area1);
        nh.ReplaceArea(area2);
        nh.ReplaceArea(area3);
        // MapInfo with pre_grid and legend – use global MapHandler singleton directly
        try
        {
            var globalMh = GlobalServices.GetMapHandler();
            var picked = ResolveHandler(globalMh, MapHandlerFactory);
            // Test injected custom MapHandler – honour it to keep PortedMaze tests passing,
            // but if global already has limbo maps and factory is empty, prefer global to preserve limbo.
            mh = (!ReferenceEquals(picked, globalMh) && globalMh.Snapshot().Count > 0 && picked.Snapshot().Count == 0)
                ? globalMh
                : picked;
        }
        catch { mh = MapHandlerFactory(); }
        try { Atheriz.Core.Objects.MapHandlerSingleton.Set(mh); } catch (Exception) { }
        GlobalServices.SetMapHandler(mh);
        var maze1Exit = tuple1.grid.GetRandomNode();
        var maze2Exit = tuple2.grid.GetRandomNode();
        var maze3Exit = tuple3.grid.GetRandomNode();
        string exitSymbol = GameUtils.WrapXterm256("!", fg: 9);
        var mi1 = new MapInfo("maze1", tuple1.map, null, new List<LegendEntry> { new LegendEntry(exitSymbol, "to maze2", maze1Exit is not null ? (maze1Exit.Coord.X, maze1Exit.Coord.Y) : null) });
        var mi2 = new MapInfo("maze2", tuple2.map, null, new List<LegendEntry> { new LegendEntry(exitSymbol, "to maze3", maze2Exit is not null ? (maze2Exit.Coord.X, maze2Exit.Coord.Y) : null) });
        var mi3 = new MapInfo("maze3", tuple3.map, null, new List<LegendEntry> { new LegendEntry(exitSymbol, "to maze1", maze3Exit is not null ? (maze3Exit.Coord.X, maze3Exit.Coord.Y) : null) });
        mh.SetMapInfo("maze1", 0, mi1);
        mh.SetMapInfo("maze2", 0, mi2);
        mh.SetMapInfo("maze3", 0, mi3);
        if (maze1Exit is not null && maze2Exit is not null) maze1Exit.AddLink(new NodeLink("down", new Coord("maze2", 0, 0, 0), new List<string>{"d"}));
        if (maze2Exit is not null && maze3Exit is not null) maze2Exit.AddLink(new NodeLink("down", new Coord("maze3", 0, 0, 0), new List<string>{"d"}));
        if (maze3Exit is not null) maze3Exit.AddLink(new NodeLink("down", new Coord("maze1", 0, 0, 0), new List<string>{"d"}));
        var start = nh.GetNode(new Coord("maze1", 0, 0, 0));
        var end = maze1Exit;
        // Faithful to atheriz/commands/loggedin/maze.py:97-107 — queue do_pathfind first,
        // then the "moving to" message, map_enabled, and move_to, with no delay.
        // do_pathfind sends unbackground itself before background, so the highlight
        // can never be cleared by the area-change unbackground that move_to triggers.
        if (start is not null && end is not null)
        {
            var capturedConn = go.Session?.Connection;
            bool queued = false;
            try
            {
                var pool = ThreadPoolFactory();
                if (pool is not null)
                {
                    queued = pool.AddTask(() =>
                    {
                        try
                        {
                            var sw2 = System.Diagnostics.Stopwatch.StartNew();
                            var (found, path, dead) = Pathfind.AStar(start, end, go, nh);
                            sw2.Stop();
                            // Verbatim maze.py do_pathfind: unbackground first, then background.
                            try { capturedConn?.SendCommand("unbackground", new List<object?> { "" }, null); } catch (Exception) { }
                            if (found)
                            {
                                go.Msg($"path found in: {sw2.Elapsed.TotalMilliseconds:F2} milliseconds");
                                try { SendBackground(capturedConn, go, [83, 128, 56], path.Select(n => (object)new List<int> { n.Coord.X, n.Coord.Y }).ToList()); } catch (Exception) { }
                            }
                            else
                            {
                                go.Msg($"path not found in: {sw2.Elapsed.TotalMilliseconds:F2} milliseconds");
                                try { SendBackground(capturedConn, go, [90, 0, 0], dead.Select(c => (object)new List<int> { c.X, c.Y }).ToList()); } catch (Exception) { }
                            }
                        }
                        catch (Exception ex) { try { go.Msg($"pathfind error: {ex.Message}"); } catch (Exception) { } }
                    });
                }
            }
            catch (Exception) { }
            if (!queued) go.Msg("Pathfinding queue full; try again in a moment.");
            else go.Msg($"moving to: {start} ...");
            try { go.IsMapable = true; } catch (Exception) { }
            go.MapEnabled = true;
            if (!go.MoveTo(start))
                go.Msg($"Could not move to {start.Coord}.");
        }
        else if (start is null)
        {
            // No node at origin skip move (test expects not called)
        }
    }

    // Shared global-vs-factory reconcile: a factory handler that differs from
    // the global singleton wins (test-injected world); otherwise the global
    // stays. Pure selection — registration side-effects stay at the call sites.
    private static T ResolveHandler<T>(T global, Func<T?> factory) where T : class
    {
        T? injected = null;
        try { injected = factory(); } catch (Exception) { }
        return injected is not null && !ReferenceEquals(injected, global) ? injected : global;
    }

    // Shared pathfind-result background: one payload shape plus the same
    // captured-connection-then-session fallback for both outcomes.
    private static void SendBackground(Atheriz.Core.Network.BaseConnection? conn, GameObject go, List<int> color, List<object> coords)
    {
        var bgPayload = new Dictionary<string, object?> { ["color"] = color, ["coords"] = coords };
        try { conn?.SendCommand("background", new List<object?> { bgPayload }, null); } catch (Exception) { }
        if (conn is null) try { go.Session?.Connection?.SendCommand("background", new List<object?> { bgPayload }, null); } catch (Exception) { }
    }

    public static (Dictionary<(int,int), string> map, NodeGrid grid) GenMapAndGrid(int w, int h, string area)
    {
        var maze = CreateMaze(w, h);
        return CreateMap(maze, w, h, area);
    }
    public static Dictionary<(int,int), List<(int,int)>> CreateMaze(int width, int height)
    {
        HashSet<(int,int)> visited = [];
        List<(int,int)> GetValid((int,int) coord)
        {
            List<(int,int)> list = [];
            if (coord.Item1 > 0) list.Add((coord.Item1 - 1, coord.Item2));
            if (coord.Item1 < width - 1) list.Add((coord.Item1 + 1, coord.Item2));
            if (coord.Item2 > 0) list.Add((coord.Item1, coord.Item2 - 1));
            if (coord.Item2 < height - 1) list.Add((coord.Item1, coord.Item2 + 1));
            return list.Where(c => !visited.Contains(c)).ToList();
        }
        var start = (0,0);
        var valid = GetValid(start);
        var current = start;
        List<(int,int)> path = [];
        Dictionary<(int,int), List<(int,int)>> maze = [];
        var nodes = maze.GetValueOrDefault(current, []);
        bool done = false;
        while (!done)
        {
            if (valid.Count == 0) { if (path.Count == 0) { done = true; break; } path.RemoveAt(path.Count - 1); if (path.Count == 0) { done = true; break; } current = path.Last(); nodes = maze.GetValueOrDefault(current, []); valid = GetValid(current); continue; }
            var c = valid[Random.Shared.Next(valid.Count)];
            visited.Add(c);
            path.Add(c);
            if (nodes.Count == 0) maze[current] = new List<(int,int)>{c}; else { nodes.Add(c); maze[current] = nodes; }
            current = c;
            nodes = maze.GetValueOrDefault(current, []);
            valid = GetValid(current);
            while (valid.Count == 0)
            {
                path.RemoveAt(path.Count - 1);
                if (path.Count == 0) { done = true; break; }
                current = path.Last();
                nodes = maze.GetValueOrDefault(current, []);
                valid = GetValid(current);
            }
        }
        return maze;
    }
    public static (Dictionary<(int,int), string> map, NodeGrid grid) CreateMap(Dictionary<(int,int), List<(int,int)>> maze, int width, int height, string area)
    {
        Dictionary<(int,int), string> map = [];
        var grid = new NodeGrid(area, 0);
        foreach (var kv in maze)
        {
            var k = kv.Key; var v = kv.Value;
            bool n=false,s=false,e=false,w=false;
            foreach (var d in v)
            {
                if (d == (k.Item1+1, k.Item2)) e=true;
                if (d == (k.Item1-1, k.Item2)) w=true;
                if (d == (k.Item1, k.Item2+1)) n=true;
                if (d == (k.Item1, k.Item2-1)) s=true;
            }
            if (k.Item1 > 0 && maze.GetValueOrDefault((k.Item1-1,k.Item2), []).Contains(k)) w=true;
            if (k.Item1 < width-1 && maze.GetValueOrDefault((k.Item1+1,k.Item2), []).Contains(k)) e=true;
            if (k.Item2 > 0 && maze.GetValueOrDefault((k.Item1,k.Item2-1), []).Contains(k)) s=true;
            if (k.Item2 < height-1 && maze.GetValueOrDefault((k.Item1,k.Item2+1), []).Contains(k)) n=true;
            var node = new Node(new Coord(area, k.Item1, k.Item2, 0), "Somewhere in a mysterious maze.");
            if (n) node.AddLink(new NodeLink("north", new Coord(area, k.Item1, k.Item2+1, 0), new List<string>{"n"}));
            if (s) node.AddLink(new NodeLink("south", new Coord(area, k.Item1, k.Item2-1, 0), new List<string>{"s"}));
            if (e) node.AddLink(new NodeLink("east", new Coord(area, k.Item1+1, k.Item2, 0), new List<string>{"e"}));
            if (w) node.AddLink(new NodeLink("west", new Coord(area, k.Item1-1, k.Item2, 0), new List<string>{"w"}));
            grid.AddNode(node);
            // the Node ctor no longer publishes to the registry —
            // register explicitly (mirrors NodeHandler.AddNode's order).
            Atheriz.Core.Globals.ObjectRegistry.AddObject(node);
            string ch;
            if (n&&s&&e&&w) ch="╬"; else if (n&&s&&e) ch="╠"; else if (n&&s&&w) ch="╣"; else if (s&&e&&w) ch="╦"; else if (n&&e&&w) ch="╩"; else if (s&&e) ch="╔"; else if (s&&w) ch="╗"; else if (n&&e) ch="╚"; else if (n&&w) ch="╝"; else if (n||s) ch="║"; else if (e||w) ch="═"; else ch=" ";
            map[(k.Item1,k.Item2)] = ch;
        }
        return (map, grid);
    }
}