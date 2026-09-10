
namespace Atheriz.Core.Commands.LoggedIn;

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
    // Canonical flag order behind the direction if-chain (n, e, s, w, u, d, x):
    // the targets list below must keep this exact order.
    private static readonly string[] DirectionOrder = ["n", "e", "s", "w", "u", "d", "x"];

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
        if (!CommandHelpers.RequirePuppet(caller, out var goCaller)) return;
        // Resolve args to flags (production Run receives ParsedArgs via UseParser).
        bool n=false, e=false, s=false, w=false, u=false, d=false, x=false;
        bool room=false, road=false, path=false;
        string? desc=null;
        bool single=false, dbl=false, round=false, none=false;
        if (args is null)
        {
            caller.Msg(PrintHelp());
            return;
        }
        if (args is GameArgumentParser.ParsedArgs pa)
        {
            n=pa.GetBool("n"); e=pa.GetBool("e"); s=pa.GetBool("s"); w=pa.GetBool("w"); u=pa.GetBool("u"); d=pa.GetBool("d"); x=pa.GetBool("x");
            room=pa.GetBool("room"); road=pa.GetBool("road"); path=pa.GetBool("path");
            desc=pa["desc"] as string;
            single=pa.GetBool("single"); dbl=pa.GetBool("double"); round=pa.GetBool("round"); none=pa.GetBool("none");
        }
        else
        {
            caller.Msg(PrintHelp());
            return;
        }

        // Node and map handlers via Singletons
        var nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
        MapHandler mh;
        try { mh = GlobalServices.GetMapHandler(); } catch { mh = new MapHandler(autoLoad:false); }

        Node? loc = goCaller.ResolveLocationObject() as Node;

        if (loc is null)
        {
            caller.Msg("You must be in a valid location to build.");
            return;
        }

        // mutually exclusive groups: --room/--road/--path and --single/--double/--round/--none (build.py:35,53)
        if (new[] { room, road, path }.Count(b => b) > 1) { caller.Msg(PrintHelp()); return; }
        if (new[] { single, dbl, round, none }.Count(b => b) > 1) { caller.Msg(PrintHelp()); return; }

        bool hasArgs = x || n || e || s || w || u || d || road || path || room || single || dbl || round || none || desc is not null;
        if (!hasArgs)
        {
            caller.Msg(PrintHelp());
            return;
        }

        List<string> targets = [];
        var dirFlags = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["n"] = n, ["e"] = e, ["s"] = s, ["w"] = w, ["u"] = u, ["d"] = d, ["x"] = x,
        };
        foreach (var key in DirectionOrder)
            if (dirFlags[key])
                targets.Add(key);

        if (targets.Count == 0)
        {
            if (desc is not null)
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
                if (toNode is not null) toNode.AddLinkIfAbsent("south", () => new NodeLink("south", node.Coord, new List<string>{"s"}));
            }
            if (ss)
            {
                node.AddLinkIfAbsent("south", () => new NodeLink("south", new Coord(node.Coord.Area, node.Coord.X, node.Coord.Y-1, node.Coord.Z), new List<string>{"s"}));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X, node.Coord.Y-1, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode is not null) toNode.AddLinkIfAbsent("north", () => new NodeLink("north", node.Coord, new List<string>{"n"}));
            }
            if (ee)
            {
                node.AddLinkIfAbsent("east", () => new NodeLink("east", new Coord(node.Coord.Area, node.Coord.X+1, node.Coord.Y, node.Coord.Z), new List<string>{"e"}));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X+1, node.Coord.Y, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode is not null) toNode.AddLinkIfAbsent("west", () => new NodeLink("west", node.Coord, new List<string>{"w"}));
            }
            if (ww)
            {
                node.AddLinkIfAbsent("west", () => new NodeLink("west", new Coord(node.Coord.Area, node.Coord.X-1, node.Coord.Y, node.Coord.Z), new List<string>{"w"}));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X-1, node.Coord.Y, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode is not null) toNode.AddLinkIfAbsent("east", () => new NodeLink("east", node.Coord, new List<string>{"e"}));
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
            if (newNode is null)
            {
                string nd = desc ?? "Placeholder desc, use desc command to change";
                var areaObj = nh.GetArea(c.Area);
                if (areaObj is null)
                {
                    caller.Msg("Error: Current area not found.");
                    return;
                }
                NodeGrid grid;
                areaObj.Lock.EnterWriteLock();
                try
                {
                    grid = areaObj.GetGrid(newCoord.Z) ?? new NodeGrid(c.Area, newCoord.Z);
                    if (areaObj.GetGrid(newCoord.Z) is null) areaObj.AddGrid(grid);
                }
                finally { areaObj.Lock.ExitWriteLock(); }
                grid.Lock.EnterWriteLock();
                try
                {
                    if (grid.Nodes.TryGetValue((newCoord.X, newCoord.Y), out var existing) && existing is not null)
                    {
                        newNode = existing;
                        if (desc is not null) newNode.Desc = desc;
                        caller.Msg($"Updating node at {newCoord}.");
                    }
                    else
                    {
                        newNode = new Node(newCoord, desc: nd);
                        // Hold the grid write lock across creation+insert (no exit/re-enter window).
                        // Grid lock is SupportsRecursion so re-entrant AddNode does not deadlock.
                        grid.AddNode(newNode);
                        // the Node ctor no longer publishes to the
                        // registry — register explicitly (mirrors
                        // NodeHandler.AddNode's insert-then-publish order).
                        Atheriz.Core.Globals.ObjectRegistry.AddObject(newNode);
                        caller.Msg($"Created new node at {newCoord}.");
                    }
                }
                finally { grid.Lock.ExitWriteLock(); }
            }
            else
            {
                caller.Msg($"Updating node at {newCoord}.");
                if (desc is not null) newNode.Desc = desc;
            }

            if (dKey != "x")
            {
                loc.AddLinkIfAbsent(linkName, () => new NodeLink(linkName, newCoord, new List<string>{dKey}));
                string alias = GetAlias(backLinkName);
                var aliases = string.IsNullOrEmpty(alias) ? [] : new List<string>{alias};
                newNode.AddLinkIfAbsent(backLinkName, () => new NodeLink(backLinkName, loc.Coord, aliases));
            }

            var mi = mh.EnsureMapInfo(newCoord.Area, newCoord.Z);
            using (mi.BatchUpdate())
            {
                if (room)
                {
                    var sset = AtherizSettings.Global;
                    // Precedence matches the old if-chain (single > double >
                    // rounded > none > default outline); mutual exclusion is
                    // enforced above, so at most one arm can fire either way.
                    string ch = (single, dbl, round, none) switch
                    {
                        (true, _, _, _) => sset.SingleWallPlaceholder,
                        (_, true, _, _) => sset.DoubleWallPlaceholder,
                        (_, _, true, _) => sset.RoundedWallPlaceholder,
                        (_, _, _, true) => "",
                        _ => sset.DefaultRoomOutline switch
                        {
                            "single" => sset.SingleWallPlaceholder,
                            "double" => sset.DoubleWallPlaceholder,
                            "rounded" => sset.RoundedWallPlaceholder,
                            _ => "",
                        },
                    };
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

        if (targets.Count == 1 && lastNewNode is not null)
        {
            // Move caller to new node
            if (!goCaller.MoveTo(lastNewNode))
                caller.Msg($"Could not move to {lastNewNode.Coord}.");
        }
    }

    public string GetAlias(string name)
    {
        return name switch { "north"=>"n", "south"=>"s", "east"=>"e", "west"=>"w", "up"=>"u", "down"=>"d", _=>"" };
    }
    public bool HasLink(Node node, string linkName)
    {
        if (node.Links is null) return false;
        foreach (var l in node.GetLinks()) if (l.Name == linkName) return true;
        return false;
    }
    // Python compat names
    public bool _has_link(Node node, string linkName) => HasLink(node, linkName);
    public string _get_alias(string name) => GetAlias(name);
}