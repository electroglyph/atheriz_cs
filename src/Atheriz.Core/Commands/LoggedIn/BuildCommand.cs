using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class BuildArgs
{
    public bool N; public bool E; public bool S; public bool W; public bool U; public bool D; public bool X;
    public bool Room; public bool Road; public bool Path;
    public string? Desc;
    public bool Single; public bool Double; public bool Round; public bool None;
}

public sealed class BuildCommand : Command
{
    public static readonly IReadOnlyDictionary<string, (int dx, int dy, int dz, string link, string back)> Directions
        = new Dictionary<string, (int, int, int, string, string)>
        {
            ["n"] = (0, 1, 0, "north", "south"),
            ["e"] = (1, 0, 0, "east", "west"),
            ["s"] = (0, -1, 0, "south", "north"),
            ["w"] = (-1, 0, 0, "west", "east"),
            ["u"] = (0, 0, 1, "up", "down"),
            ["d"] = (0, 0, -1, "down", "up"),
            ["x"] = (0, 0, 0, "here", "here"),
        };
    // Python alias DIRECTIONS
    public static readonly IReadOnlyDictionary<string, (int dx, int dy, int dz, string link, string back)> DIRECTIONS = Directions;

    public override string Key => "build";
    public override string Desc => "Build rooms, roads, and paths.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser parser)
    {
        parser.AddArgument("--room", help: "Build a room").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("--road", help: "Build a road").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("--path", help: "Build a path").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("-x", help: "Build here").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("-n", help: "Build north").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("-e", help: "Build east").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("-s", help: "Build south").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("-w", help: "Build west").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("-u", help: "Build up").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("-d", help: "Build down").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("--desc", help: "Set description").Type(typeof(string));
        parser.AddArgument("--single", help: "Single line for room walls").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("--double", help: "Double line for room walls").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("--round", help: "Rounded line for room walls").Action(GameArgumentParser.ArgAction.StoreTrue);
        parser.AddArgument("--none", help: "No room walls").Action(GameArgumentParser.ArgAction.StoreTrue);
    }

    public override void Run(IMessageTarget caller, object? args)
    {
        // Resolve args to flags
        bool n=false, e=false, s=false, w=false, u=false, d=false, x=false;
        bool room=false, road=false, path=false;
        string? desc=null;
        bool single=false, dbl=false, round=false, none=false;
        bool hasArgsObj = false;
        if (args == null)
        {
            caller.Msg(PrintHelp());
            return;
        }
        if (args is BuildArgs ba)
        {
            hasArgsObj = true;
            n=ba.N; e=ba.E; s=ba.S; w=ba.W; u=ba.U; d=ba.D; x=ba.X;
            room=ba.Room; road=ba.Road; path=ba.Path; desc=ba.Desc;
            single=ba.Single; dbl=ba.Double; round=ba.Round; none=ba.None;
        }
        else if (args is GameArgumentParser.ParsedArgs pa)
        {
            hasArgsObj = true;
            n=pa.GetBool("n"); e=pa.GetBool("e"); s=pa.GetBool("s"); w=pa.GetBool("w"); u=pa.GetBool("u"); d=pa.GetBool("d"); x=pa.GetBool("x");
            room=pa.GetBool("room"); road=pa.GetBool("road"); path=pa.GetBool("path");
            desc=pa["desc"] as string;
            single=pa.GetBool("single"); dbl=pa.GetBool("double"); round=pa.GetBool("round"); none=pa.GetBool("none");
        }
        else
        {
            // Generic reflection: anonymous object, dictionary, or MockCaller mock-like
            try
            {
                var t = args.GetType();
                bool TryGetBool(string name, out bool val)
                {
                    var p = t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                    if (p != null && p.PropertyType == typeof(bool)) { val = (bool)(p.GetValue(args) ?? false); return true; }
                    var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                    if (f != null && f.FieldType == typeof(bool)) { val = (bool)(f.GetValue(args) ?? false); return true; }
                    val=false; return false;
                }
                bool TryGetString(string name, out string? val)
                {
                    var p = t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                    if (p != null) { val = p.GetValue(args) as string; return true; }
                    var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                    if (f != null) { val = f.GetValue(args) as string; return true; }
                    val=null; return false;
                }
                hasArgsObj = true;
                TryGetBool("n", out n); TryGetBool("e", out e); TryGetBool("s", out s); TryGetBool("w", out w); TryGetBool("u", out u); TryGetBool("d", out d); TryGetBool("x", out x);
                TryGetBool("room", out room); TryGetBool("road", out road); TryGetBool("path", out path);
                TryGetString("desc", out desc);
                TryGetBool("single", out single); TryGetBool("double", out dbl); TryGetBool("round", out round); TryGetBool("none", out none);
                // Also check dynamic dictionary pattern: IDictionary
                if (args is System.Collections.IDictionary dict)
                {
                    if (dict.Contains("desc")) desc = dict["desc"] as string;
                    // bool flags may be in dict too but not needed for test helper
                }
            }
            catch { caller.Msg(PrintHelp()); return; }
        }

        if (!hasArgsObj)
        {
            caller.Msg(PrintHelp());
            return;
        }

        // Node and map handlers via Singletons
        var nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
        MapHandler mh;
        try { mh = GlobalServices.GetMapHandler(); } catch { mh = new MapHandler(autoLoad:false); }

        GameObject? goCaller = caller as GameObject;
        Node? loc = null;
        if (goCaller != null)
        {
            loc = goCaller.ResolveLocationObject() as Node;
            // Also support direct location via Caller property? If caller is MockCaller style with location property
            if (loc == null)
            {
                try
                {
                    var prop = caller.GetType().GetProperty("location", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                    if (prop != null)
                    {
                        var val = prop.GetValue(caller);
                        if (val is Node nnode) loc = nnode;
                        else if (val == null) loc = null;
                    }
                }
                catch { }
            }
        }
        else
        {
            // generic IMessageTarget with location property (MockCaller in tests)
            try
            {
                var prop = caller.GetType().GetProperty("location", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    var val = prop.GetValue(caller);
                    if (val is Node nnode) loc = nnode;
                }
                var field = caller.GetType().GetField("location", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (loc == null && field != null)
                {
                    var val = field.GetValue(caller);
                    if (val is Node nnode) loc = nnode;
                }
            }
            catch { }
        }

        if (loc == null)
        {
            caller.Msg("You must be in a valid location to build.");
            return;
        }

        // mutually exclusive groups: --room/--road/--path and --single/--double/--round/--none (build.py:35,53)
        if (new[] { room, road, path }.Count(b => b) > 1) { caller.Msg(PrintHelp()); return; }
        if (new[] { single, dbl, round, none }.Count(b => b) > 1) { caller.Msg(PrintHelp()); return; }

        bool hasArgs = x || n || e || s || w || u || d || road || path || room || single || dbl || round || none || desc != null;
        if (!hasArgs)
        {
            caller.Msg(PrintHelp());
            return;
        }

        var targets = new List<string>();
        if (n) targets.Add("n");
        if (e) targets.Add("e");
        if (s) targets.Add("s");
        if (w) targets.Add("w");
        if (u) targets.Add("u");
        if (d) targets.Add("d");
        if (x) targets.Add("x");

        if (targets.Count == 0)
        {
            if (desc != null)
            {
                loc.Desc = desc;
                caller.Msg("Updated current location's description.");
                return;
            }
            else
            {
                caller.Msg(PrintHelp() + "\n" + Parser!.FormatUsage());
                return;
            }
        }

        if (!room && !road && !path)
            room = true;

        // Helpers
        (bool N,bool S,bool E,bool W) GetRoomDirs(MapInfo mi, (int X,int Y) coord)
        {
            var settings = AtherizSettings.Global;
            bool nn=false, ss=false, ee=false, ww=false;
            mi.Lock.EnterReadLock();
            try
            {
                if (mi.PreGrid.TryGetValue((coord.X, coord.Y+1), out var v) && v == settings.RoomPlaceholder) nn=true;
                if (mi.PreGrid.TryGetValue((coord.X, coord.Y-1), out v) && v == settings.RoomPlaceholder) ss=true;
                if (mi.PreGrid.TryGetValue((coord.X+1, coord.Y), out v) && v == settings.RoomPlaceholder) ee=true;
                if (mi.PreGrid.TryGetValue((coord.X-1, coord.Y), out v) && v == settings.RoomPlaceholder) ww=true;
            }
            finally { mi.Lock.ExitReadLock(); }
            return (nn, ss, ee, ww);
        }
        void EnsureLinks(Node node, bool nn,bool ss,bool ee,bool ww)
        {
            if (nn)
            {
                node.AddLinkIfAbsent("north", () => new NodeLink("north", new Coord(node.Coord.Area, node.Coord.X, node.Coord.Y+1, node.Coord.Z), new List<string>{"n"}));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X, node.Coord.Y+1, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode != null) toNode.AddLinkIfAbsent("south", () => new NodeLink("south", node.Coord, new List<string>{"s"}));
            }
            if (ss)
            {
                node.AddLinkIfAbsent("south", () => new NodeLink("south", new Coord(node.Coord.Area, node.Coord.X, node.Coord.Y-1, node.Coord.Z), new List<string>{"s"}));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X, node.Coord.Y-1, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode != null) toNode.AddLinkIfAbsent("north", () => new NodeLink("north", node.Coord, new List<string>{"n"}));
            }
            if (ee)
            {
                node.AddLinkIfAbsent("east", () => new NodeLink("east", new Coord(node.Coord.Area, node.Coord.X+1, node.Coord.Y, node.Coord.Z), new List<string>{"e"}));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X+1, node.Coord.Y, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode != null) toNode.AddLinkIfAbsent("west", () => new NodeLink("west", node.Coord, new List<string>{"w"}));
            }
            if (ww)
            {
                node.AddLinkIfAbsent("west", () => new NodeLink("west", new Coord(node.Coord.Area, node.Coord.X-1, node.Coord.Y, node.Coord.Z), new List<string>{"w"}));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X-1, node.Coord.Y, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode != null) toNode.AddLinkIfAbsent("east", () => new NodeLink("east", node.Coord, new List<string>{"e"}));
            }
        }

        Node? lastNewNode = null;
        foreach (var dKey in targets)
        {
            var dData = Directions[dKey];
            int dx=dData.dx, dy=dData.dy, dz=dData.dz; string linkName=dData.link, backLinkName=dData.back;
            var c = loc.Coord;
            var newCoord = new Coord(c.Area, c.X+dx, c.Y+dy, c.Z+dz);
            var newNode = nh.GetNode(newCoord);
            if (newNode == null)
            {
                string nd = desc ?? "Placeholder desc, use desc command to change";
                var areaObj = nh.GetArea(c.Area);
                if (areaObj == null)
                {
                    caller.Msg("Error: Current area not found.");
                    return;
                }
                NodeGrid grid;
                areaObj.Lock.EnterWriteLock();
                try
                {
                    grid = areaObj.GetGrid(newCoord.Z) ?? new NodeGrid(c.Area, newCoord.Z);
                    if (areaObj.GetGrid(newCoord.Z) == null) areaObj.AddGrid(grid);
                }
                finally { areaObj.Lock.ExitWriteLock(); }
                grid.Lock.EnterWriteLock();
                try
                {
                    if (grid.Nodes.TryGetValue((newCoord.X, newCoord.Y), out var existing) && existing != null)
                    {
                        newNode = existing;
                        if (desc != null) newNode.Desc = desc;
                        caller.Msg($"Updating node at {newCoord}.");
                    }
                    else
                    {
                        newNode = new Node(newCoord, desc: nd);
                        // Need to add via grid.AddNode (which also handles transitions)
                        // But we already have lock; use direct to avoid deadlock? grid.AddNode will try to acquire lock again (SupportsRecursion) ok.
                        grid.Lock.ExitWriteLock();
                        try { grid.AddNode(newNode); } finally { grid.Lock.EnterWriteLock(); }
                        caller.Msg($"Created new node at {newCoord}.");
                    }
                }
                finally { grid.Lock.ExitWriteLock(); }
            }
            else
            {
                caller.Msg($"Updating node at {newCoord}.");
                if (desc != null) newNode.Desc = desc;
            }

            if (dKey != "x")
            {
                loc.AddLinkIfAbsent(linkName, () => new NodeLink(linkName, newCoord, new List<string>{dKey}));
                string alias = GetAlias(backLinkName);
                var aliases = string.IsNullOrEmpty(alias) ? new List<string>() : new List<string>{alias};
                newNode.AddLinkIfAbsent(backLinkName, () => new NodeLink(backLinkName, loc.Coord, aliases));
            }

            var mi = mh.GetOrCreatePublic(newCoord.Area, newCoord.Z);
            using (mi.BatchUpdate())
            {
                if (room)
                {
                    string ch = "";
                    var sset = AtherizSettings.Global;
                    if (single) ch = sset.SingleWallPlaceholder;
                    else if (dbl) ch = sset.DoubleWallPlaceholder;
                    else if (round) ch = sset.RoundedWallPlaceholder;
                    else if (none) { }
                    else
                    {
                        if (sset.DefaultRoomOutline == "single") ch = sset.SingleWallPlaceholder;
                        else if (sset.DefaultRoomOutline == "double") ch = sset.DoubleWallPlaceholder;
                        else if (sset.DefaultRoomOutline == "rounded") ch = sset.RoundedWallPlaceholder;
                    }
                    if (!string.IsNullOrEmpty(ch))
                    {
                        var roomPH = AtherizSettings.Global.RoomPlaceholder;
                        mi.UpdateGrid((newCoord.X, newCoord.Y), roomPH);
                        mi.PlaceWalls((newCoord.X, newCoord.Y), ch);
                        var (nn, ss, ee, ww) = GetRoomDirs(mi, (newCoord.X, newCoord.Y));
                        EnsureLinks(newNode, nn, ss, ee, ww);
                    }
                }
                else if (road)
                {
                    var roadPH = AtherizSettings.Global.RoadPlaceholder;
                    mi.UpdateGrid((newCoord.X, newCoord.Y), roadPH);
                }
                else if (path)
                {
                    var pathPH = AtherizSettings.Global.PathPlaceholder;
                    mi.UpdateGrid((newCoord.X, newCoord.Y), pathPH);
                    mi.PlaceWalls((newCoord.X, newCoord.Y), pathPH);
                }
            }
            lastNewNode = newNode;
        }

        if (targets.Count == 1 && lastNewNode != null)
        {
            // Move caller to new node (support both GameObject and generic MockCaller)
            if (goCaller != null)
            {
                goCaller.MoveTo(lastNewNode);
            }
            else
            {
                try
                {
                    var m = caller.GetType().GetMethod("move_to") ?? caller.GetType().GetMethod("MoveTo");
                    if (m != null) m.Invoke(caller, new object[]{lastNewNode});
                    else
                    {
                        var prop = caller.GetType().GetProperty("location");
                        if (prop != null && prop.CanWrite) prop.SetValue(caller, lastNewNode);
                    }
                }
                catch { }
            }
        }
    }

    public string GetAlias(string name)
    {
        return name switch { "north"=>"n", "south"=>"s", "east"=>"e", "west"=>"w", "up"=>"u", "down"=>"d", _=>"" };
    }
    public bool HasLink(Node node, string linkName)
    {
        if (node.Links == null) return false;
        foreach (var l in node.GetLinks()) if (l.Name == linkName) return true;
        return false;
    }
    // Python compat names
    public bool _has_link(Node node, string linkName) => HasLink(node, linkName);
    public string _get_alias(string name) => GetAlias(name);
}