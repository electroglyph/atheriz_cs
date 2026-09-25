using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class BanCommand : BuilderCommand
{
    public override string Key => "ban";
    public override string Desc => "Ban a player character, optionally their account and/or IP.";
    public override string Category => "Building";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target).Help("Player character to ban (name or #id).");
        p.AddArgument("-r", "--reason").Help("Reason for the ban.");
        p.AddArgument("--account").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Ban the entire account and all its characters.");
        p.AddArgument("--ip").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Also ban the target's IP (requires an online target).");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var targetName = pa.GetString(ParsedArgKeys.Target);
        if (string.IsNullOrWhiteSpace(targetName)) { go.Msg(PrintHelp()); return; }
        var reason = pa.GetString(ParsedArgKeys.Reason);
        bool wantAccount = pa.GetBool(ParsedArgKeys.Account);
        bool ip = pa.GetBool(ParsedArgKeys.Ip);
        if (!BanHelper.TryResolveBanPreamble(go, targetName, wantAccount, ip, "ban", "banning",
            out var target, out var acct, out var acctChars, out var host, out bool account)
            || target is null)
            return;
        List<GameObject> kickTargets;
        if (account && acct is not null)
        {
            kickTargets = acct is Account ? acctChars : [target];
            // Atomic scope apply: account + members commit together.
            // A non-Account holder keeps the old shape (members only).
            var banScope = new List<GameObject>(kickTargets);
            if (acct is Account) banScope.Add(acct);
            BanHelper.ApplyScopeState(banScope, true, string.IsNullOrEmpty(reason) ? null : reason);
        }
        else
        {
            kickTargets = [target];
            BanHelper.ApplyScopeState([target], true, string.IsNullOrEmpty(reason) ? null : reason);
        }
        string? kickedIp = null;
        if (ip)
        {
            if (host is null) go.Msg("Target is not online; cannot ban IP.");
            else { ObjectRegistry.BanIp(host); kickedIp = host; }
        }
        List<string> failed = [];
        foreach (var t in kickTargets)
        {
            try
            {
                // Atomic live-conn snapshot: never Msg+Close a torn
                // session/connection pair across a reconnect swap. An offline
                // target (no session) skips silently as before — only a live
                // target whose snapshot failed reports.
                if (!BanHelper.TryGetLiveConn(t, out _, out var conn) || conn is null)
                {
                    if (t.Session is not null) failed.Add(t.Name);
                    continue;
                }
                {
                    string msg = string.IsNullOrEmpty(reason) ? "You have been banned." : $"You have been banned. Reason: {reason}";
                    conn.Msg(msg);
                    conn.Close();
                }
            }
            catch { failed.Add(t.Name); }
        }
        string scope = account && acct is not null ? "account" : "character";
        string outMsg = string.IsNullOrEmpty(reason) ? $"Banned {target.Name} ({scope})." : $"Banned {target.Name} ({scope}, reason: {reason}).";
        if (kickedIp is not null) outMsg = $"{outMsg} IP {kickedIp} banned until server restart.";
        if (failed.Count > 0) outMsg = $"{outMsg} Kick failed for: {string.Join(", ", failed)}.";
        go.Msg(outMsg);
    }
}

public sealed class BuildCommand : BuilderCommand
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

    protected override void RunPuppet(GameObject goCaller, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        // Resolve args to flags (production Run receives ParsedArgs via UseParser).
        bool n=false, e=false, s=false, w=false, u=false, d=false, x=false;
        bool room=false, road=false, path=false;
        string? desc=null;
        bool single=false, dbl=false, round=false, none=false;
        n=pa.GetBool(ParsedArgKeys.N); e=pa.GetBool(ParsedArgKeys.E); s=pa.GetBool(ParsedArgKeys.S); w=pa.GetBool(ParsedArgKeys.W); u=pa.GetBool(ParsedArgKeys.U); d=pa.GetBool(ParsedArgKeys.D); x=pa.GetBool(ParsedArgKeys.X);
        room=pa.GetBool(ParsedArgKeys.Room); road=pa.GetBool(ParsedArgKeys.Road); path=pa.GetBool(ParsedArgKeys.Path);
        desc=pa[ParsedArgKeys.Desc] as string;
        single=pa.GetBool(ParsedArgKeys.Single); dbl=pa.GetBool(ParsedArgKeys.Double); round=pa.GetBool(ParsedArgKeys.Round); none=pa.GetBool(ParsedArgKeys.None);

        // Node and map handlers via Singletons
        var nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
        MapHandler mh;
        try { mh = GlobalServices.GetMapHandler(); } catch { mh = new MapHandler(autoLoad:false); }

        Node? loc = goCaller.ResolveLocationObject() as Node;

        if (loc is null)
        {
            goCaller.Msg("You must be in a valid location to build.");
            return;
        }

        // mutually exclusive groups: --room/--road/--path and --single/--double/--round/--none (build.py:35,53)
        if (new[] { room, road, path }.Count(b => b) > 1) { goCaller.Msg(PrintHelp()); return; }
        if (new[] { single, dbl, round, none }.Count(b => b) > 1) { goCaller.Msg(PrintHelp()); return; }

        bool hasArgs = x || n || e || s || w || u || d || road || path || room || single || dbl || round || none || desc is not null;
        if (!hasArgs)
        {
            goCaller.Msg(PrintHelp());
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
                goCaller.Msg("Updated current location's description.");
                return;
            }
            else
            {
                goCaller.Msg(PrintHelp() + "\n" + Parser!.FormatUsage());
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
            // Single snapshot under the info read lock (a second access
            // would re-enter the NoRecursion lock).
            var pre = mi.PreGrid;
            if (pre.TryGetValue((coord.X, coord.Y+1), out var v) && v == settings.RoomPlaceholder) nn=true;
            if (pre.TryGetValue((coord.X, coord.Y-1), out v) && v == settings.RoomPlaceholder) ss=true;
            if (pre.TryGetValue((coord.X+1, coord.Y), out v) && v == settings.RoomPlaceholder) ee=true;
            if (pre.TryGetValue((coord.X-1, coord.Y), out v) && v == settings.RoomPlaceholder) ww=true;
            return (nn, ss, ee, ww);
        }
        void EnsureLinks(Node node, bool nn,bool ss,bool ee,bool ww)
        {
            var rows = new (bool active, string name, string alias, int dx, int dy, string opp, string oppAlias)[]
            {
                (nn, "north", "n", 0, 1, "south", "s"),
                (ss, "south", "s", 0, -1, "north", "n"),
                (ee, "east", "e", 1, 0, "west", "w"),
                (ww, "west", "w", -1, 0, "east", "e"),
            };
            foreach (var row in rows)
            {
                if (!row.active) continue;
                string name = row.name;
                string alias = row.alias;
                string opp = row.opp;
                string oppAlias = row.oppAlias;
                int dx = row.dx;
                int dy = row.dy;
                node.AddLinkIfAbsent(name, () => new NodeLink(name, new Coord(node.Coord.Area, node.Coord.X + dx, node.Coord.Y + dy, node.Coord.Z), new List<string> { alias }));
                var toCoord = new Coord(node.Coord.Area, node.Coord.X + dx, node.Coord.Y + dy, node.Coord.Z);
                var toNode = nh.GetNode(toCoord);
                if (toNode is not null) toNode.AddLinkIfAbsent(opp, () => new NodeLink(opp, node.Coord, new List<string> { oppAlias }));
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
                    goCaller.Msg("Error: Current area not found.");
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
                    // Locked lookup (the grid lock is already held,
                    // SupportsRecursion) instead of touching the live dict.
                    var existing = grid.GetNode(newCoord.X, newCoord.Y);
                    if (existing is not null)
                    {
                        newNode = existing;
                        if (desc is not null) newNode.Desc = desc;
                        goCaller.Msg($"Updating node at {newCoord}.");
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
                        goCaller.Msg($"Created new node at {newCoord}.");
                    }
                }
                finally { grid.Lock.ExitWriteLock(); }
            }
            else
            {
                goCaller.Msg($"Updating node at {newCoord}.");
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
                // One settings generation for the whole cell: the room/
                // road/path arms below must not mix generations on a swap.
                var sset = AtherizSettings.Global;
                if (room)
                {
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
                        // Single-generation reads: sset was captured
                        // once above — re-reading Global here could mix two
                        // settings generations in one command.
                        var roomPH = sset.RoomPlaceholder;
                        mi.UpdateGrid((newCoord.X, newCoord.Y), roomPH);
                        mi.PlaceWalls((newCoord.X, newCoord.Y), ch);
                        var (nn, ss, ee, ww) = GetRoomDirs(mi, (newCoord.X, newCoord.Y));
                        EnsureLinks(newNode, nn, ss, ee, ww);
                    }
                }
                else if (road)
                {
                    var roadPH = sset.RoadPlaceholder;
                    mi.UpdateGrid((newCoord.X, newCoord.Y), roadPH);
                }
                else if (path)
                {
                    var pathPH = sset.PathPlaceholder;
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
                goCaller.Msg($"Could not move to {lastNewNode.Coord}.");
        }
    }

    public string GetAlias(string name)
    {
        return name switch { "north"=>"n", "south"=>"s", "east"=>"e", "west"=>"w", "up"=>"u", "down"=>"d", _=>"" };
    }
    public bool HasLink(Node node, string linkName)
    {
        // The Links getter never returns null (empty list when linkless).
        foreach (var l in node.GetLinks()) if (l.Name == linkName) return true;
        return false;
    }
    // Python compat names
    public bool _has_link(Node node, string linkName) => HasLink(node, linkName);
    public string _get_alias(string name) => GetAlias(name);
}

public sealed class CreateCommand : BuilderCommand
{
    public override string Key => "create";
    public override string Desc => "Create a new object.";
    public override string Category => "Building";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Name).Help("name of the object to create");
        p.AddArgument("-p", "--is_pc").Help("create as player character").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-i", "--is_item").Help("create as item").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-n", "--is_npc").Help("create as NPC").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-m", "--is_mapable").Help("make object mapable").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-c", "--is_container").Help("make object a container").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-t", "--is_tickable").Help("make object tickable").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument(ParsedArgKeys.Desc).Help("description of the object to create").Nargs("REMAINDER");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var name = pa.GetString(ParsedArgKeys.Name);
        if (string.IsNullOrWhiteSpace(name)) { go.Msg(PrintHelp()); return; }
        var descList = pa.GetList(ParsedArgKeys.Desc);
        var desc = string.Join(" ", descList);
        var obj = GameObject.Create(name!, desc, isPc: pa.GetBool(ParsedArgKeys.IsPc), isItem: pa.GetBool(ParsedArgKeys.IsItem), isNpc: pa.GetBool(ParsedArgKeys.IsNpc), isMapable: pa.GetBool(ParsedArgKeys.IsMapable), isContainer: pa.GetBool(ParsedArgKeys.IsContainer), isTickable: pa.GetBool(ParsedArgKeys.IsTickable));
        ObjectRegistry.AddObject(obj);
        obj.MoveTo(go);
        go.Msg($"Created object '{obj.Name}' (ID: {obj.Id}).");
    }
}

public sealed class DeleteCommand : BuilderCommand
{
    public override string Key => "delete";
    public override string Desc => "Delete an object permanently.";
    public override string Category => "Building";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target).Help("Object to delete.").Nargs("+");
        p.AddArgument("-r", "--recursive").Help("Delete contents recursively.").Action(GameArgumentParser.ArgAction.StoreTrue);
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var targets = pa.GetList(ParsedArgKeys.Target);
        if (targets.Count == 0) { go.Msg("Delete what?"); return; }
        string targetName = string.Join(" ", targets).Trim();
        var target = CommandHelpers.ResolveObject(go, targetName);
        if (target is null) return;
        if (!target.Access(go, "delete")) { go.Msg("You do not have permission to delete that."); return; }
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot delete an object of equal or higher privilege."); return; }
        var fullName = target.GetDisplayName(go);
        var result = target.Delete(go, pa.GetBool(ParsedArgKeys.Recursive));
        if (result is null) { go.Msg("Deletion aborted."); return; }
        int count = result.Value.Count;
        if (count > 1) go.Msg($"Deleted or moved {fullName}, {count} objects total.");
        else go.Msg($"Deleted {fullName}.");
    }
}

public sealed class DescCommand : BuilderCommand
{
    public override string Key => "desc";
    public override string Desc => "Change current room description, use \\n for newlines.";
    public override string Category => "Building";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Text, nargs: "REMAINDER", help: "New description.");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var lst = pa.GetList(ParsedArgKeys.Text);
        if (lst.Count > 0)
        {
            var loc = go.ResolveLocationObject();
            if (loc is null) { CommandHelpers.MsgNowhereExclaim(go); return; }
            string newDesc = string.Join(" ", lst).Replace("\\n", "\n");
            loc.Desc = newDesc;
            // at_look
            try { go.Msg(go.AtLook(loc)); } catch { go.Msg(newDesc); }
        }
        else go.Msg(PrintHelp());
    }
}

public sealed class DoorCommand : BuilderCommand
{
    public override string Key => "door";
    public override string Desc => "Manage doors.";
    public override string Category => "Building";
    // Removal rows (long, short) in established removal order; opposites live
    // in the creation table below (n<->s, e<->w, u<->d — verify both tables
    // agree when editing either).
    private static readonly (string Long, string Short)[] RemoveDirs =
        [("north", "n"), ("south", "s"), ("east", "e"), ("west", "w"), ("up", "u"), ("down", "d")];
    protected override void SetupParser(GameArgumentParser parser)
    {
        parser.AddArgument("-n", "--north").Action(GameArgumentParser.ArgAction.StoreTrue).Help("North");
        parser.AddArgument("-s", "--south").Action(GameArgumentParser.ArgAction.StoreTrue).Help("South");
        parser.AddArgument("-e", "--east").Action(GameArgumentParser.ArgAction.StoreTrue).Help("East");
        parser.AddArgument("-w", "--west").Action(GameArgumentParser.ArgAction.StoreTrue).Help("West");
        parser.AddArgument("-u", "--up").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Up");
        parser.AddArgument("-d", "--down").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Down");
        parser.AddArgument("-r", "--remove").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Remove door");
        parser.AddArgument("-a", "--auto").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Auto create destination room if it doesn't exist");
        parser.AddArgument(ParsedArgKeys.Args, nargs: "*", help: "Other args");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        // Bare direction words ride in the args carrier (like DoorDirectionCommand):
        // `door north`, `door n`, etc. work with or without a dash flag.
        var lower = pa.GetList(ParsedArgKeys.Args).Select(a => a.ToLowerInvariant()).ToList();
        bool north = pa.GetBool(ParsedArgKeys.North) || lower.Contains("n") || lower.Contains("north"),
            south = pa.GetBool(ParsedArgKeys.South) || lower.Contains("s") || lower.Contains("south"),
            east = pa.GetBool(ParsedArgKeys.East) || lower.Contains("e") || lower.Contains("east"),
            west = pa.GetBool(ParsedArgKeys.West) || lower.Contains("w") || lower.Contains("west"),
            up = pa.GetBool(ParsedArgKeys.Up) || lower.Contains("u") || lower.Contains("up"),
            down = pa.GetBool(ParsedArgKeys.Down) || lower.Contains("d") || lower.Contains("down");
        bool remove = pa.GetBool(ParsedArgKeys.Remove), auto = pa.GetBool(ParsedArgKeys.Auto);
        if (!remove && !(north||south||east||west||up||down))
        {
            go.Msg("You must specify a direction when creating a door.");
            go.Msg(PrintHelp());
            return;
        }
        if (remove && !(north||south||east||west||up||down))
        {
            go.Msg("You must specify a direction when removing a door.");
            go.Msg(PrintHelp());
            return;
        }
        var loc = go.ResolveLocationObject() as Node;
        if (loc is null) { CommandHelpers.MsgInvalidLocation(go); return; }
        var nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
        if (remove)
        {
            var doors = nh.GetDoors(loc.Coord);
            if (doors is null || doors.Count==0) { go.Msg("There are no doors here."); return; }
            void TryRemove(string longName, string shortName)
            {
                Door? d = null;
                if (doors.TryGetValue(longName, out var dd)) d = dd;
                else if (doors.TryGetValue(shortName, out var d2)) d = d2;
                if (d is not null) { nh.RemoveDoor(d); go.Msg($"Removed {d}"); }
                else go.Msg($"There is no door {longName}.");
            }
            // Row order is the established removal order (n, s, e, w, u, d).
            var removeFlags = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["north"] = north, ["south"] = south, ["east"] = east,
                ["west"] = west, ["up"] = up, ["down"] = down,
            };
            foreach (var (longName, shortName) in RemoveDirs)
                if (removeFlags[longName])
                    TryRemove(longName, shortName);
            return;
        }
        var settings = AtherizSettings.Global;
        // One row per direction: flag state rides in the tuple (no per-loop
        // flag switch) and opposites are table data (no oppLong/oppShort
        // switches). Opposite pairs: n<->s, e<->w, u<->d.
        var defs = new (string longName, string shortName, int dx, int dy, int dz, string closed, string open, bool active, string oppLong, string oppShort)[]
        {
            ("north","n",0,1,0, settings.NsClosedDoor, settings.NsOpenDoor1, north, "south","s"),
            ("south","s",0,-1,0, settings.NsClosedDoor, settings.NsOpenDoor2, south, "north","n"),
            ("east","e",1,0,0, settings.EwClosedDoor, settings.EwOpenDoor1, east, "west","w"),
            ("west","w",-1,0,0, settings.EwClosedDoor, settings.EwOpenDoor2, west, "east","e"),
            ("up","u",0,0,1, settings.UdClosedDoor, settings.UdOpenDoor, up, "down","d"),
            ("down","d",0,0,-1, settings.UdClosedDoor, settings.UdOpenDoor, down, "up","u"),
        };
        // Atomicity pre-check (deliberate improvement over Python's sequential
        // apply): without -a, a missing destination for a LATER direction must
        // fail before ANY door is created, not after earlier ones were applied
        // with success messages already sent. Mirrors the in-loop message.
        // Scope note: directions apply sequentially, not all-or-none —
        // a node deleted between the pre-check and a later direction's apply
        // refuses that direction cleanly (no door ever references a missing
        // node) but keeps earlier directions. Rolling back applied directions
        // would need a transaction across occupant MoveTo hooks; per-direction
        // refusal is fail-closed and safe.
        if (!auto)
        {
            foreach (var def in defs)
            {
                if (!def.active) continue;
                var preCoord = new Coord(loc.Coord.Area, loc.Coord.X + def.dx*2, loc.Coord.Y + def.dy*2, loc.Coord.Z + def.dz*2);
                if (nh.GetNode(preCoord) is null)
                { go.Msg($"There is no node at the destination coord {preCoord}, use -a to auto-create it."); return; }
            }
        }
        foreach (var def in defs)
        {
            if (!def.active) continue;
            var toCoord = new Coord(loc.Coord.Area, loc.Coord.X + def.dx*2, loc.Coord.Y + def.dy*2, loc.Coord.Z + def.dz*2);
            var doorCoord = new Coord(loc.Coord.Area, loc.Coord.X + def.dx, loc.Coord.Y + def.dy, loc.Coord.Z + def.dz);
            var toNode = nh.GetNode(toCoord);
            if (toNode is null)
            {
                if (auto) { toNode = new Node(toCoord); nh.AddNode(toNode); }
                else { go.Msg($"There is no node at the destination coord {toCoord}, use -a to auto-create it."); return; }
            }
            ReplaceNodeWithDoor(nh, doorCoord, go, loc);
            string oppLong = def.oppLong;
            string oppShort = def.oppShort;
            var toLinks = toNode.GetLinks();
            bool needDestLink = true;
            foreach (var l in toLinks)
            {
                if (l.Name == oppLong)
                {
                    if (!l.Coord.Equals(loc.Coord))
                    {
                        toNode.RemoveLink(l.Name);
                        go.Msg($"Removed link '{l.Name}' from node at {toCoord} for linking to the wrong coord.");
                    }
                    else
                    {
                        needDestLink = false;
                    }
                }
            }
            if (needDestLink)
            {
                var link = new NodeLink(oppLong, loc.Coord, new List<string>{oppShort});
                toNode.AddLink(link);
                go.Msg($"Created link '{link.Name}' from node at {toCoord} linking to {loc.Coord}.");
            }
            var hereLinks = loc.GetLinks();
            bool needHereLink = true;
            foreach (var l in hereLinks)
            {
                if (l.Name == def.longName)
                {
                    if (!l.Coord.Equals(toCoord))
                    {
                        loc.RemoveLink(l.Name);
                        go.Msg($"Removed link '{l.Name}' from node at {loc.Coord} for linking to the wrong coord.");
                    }
                    else
                    {
                        needHereLink = false;
                    }
                }
            }
            if (needHereLink)
            {
                var link = new NodeLink(def.longName, toCoord, new List<string>{def.shortName});
                loc.AddLink(link);
                go.Msg($"Created link '{link.Name}' from node at {loc.Coord} linking to {toCoord}.");
            }
            var door = Door.Create(loc.Coord, def.longName, toCoord, oppLong, (doorCoord.X, doorCoord.Y), def.closed, def.open);
            nh.AddDoor(door);
            go.Msg($"Created door at {doorCoord}.");
        }
    }

    private static void ReplaceNodeWithDoor(NodeHandler nh, Coord doorCoord, GameObject caller, Node fallback)
    {
        var node = nh.GetNode(doorCoord);
        if (node is null) return;
        // Sweep until empty, bounded: a joiner arriving between the
        // snapshot and RemoveNode would otherwise stay homed in the removed
        // node. Adversarial joiners cannot stall the command forever.
        for (int sweep = 0; sweep < 3; sweep++)
        {
            var occupants = node.GetContents().ToList();
            if (occupants.Count == 0) break;
            foreach (var obj in occupants)
            {
                obj.MoveTo(fallback, force: true, announce: false);
                // Python moves occupants silently; tell them what happened.
                // (Authorization is the command-level builder gate; per-occupant
                // consent hooks don't exist in either codebase.)
                try { obj.Msg($"A door is being placed where you stand; you are moved to {fallback.Name}."); } catch (Exception) { }
            }
        }
        // Fail closed rather than stranding a live occupant in a removed node.
        if (node.GetContents().ToList().Count != 0)
        {
            caller.Msg($"Could not clear the node at {doorCoord}; occupants remain.");
            return;
        }
        nh.RemoveNode(doorCoord);
        caller.Msg($"Removed node at {doorCoord} since a door is being placed there.");
    }
}

public sealed class ExamCommand : BuilderCommand
{
    public override string Key => "examine";
    public override IReadOnlyList<string> Aliases => ["exam", "ex", "exa"];
    public override string Desc => "Examine an object to see its attributes.";
    public override string Category => "Building";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target, nargs: "?", help: "Object to examine (name or #id).");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        string? targetStr = pa.GetString(ParsedArgKeys.Target);
        GameObject? target = null;
        if (string.IsNullOrEmpty(targetStr))
        {
            target = go.ResolveLocationObject();
            if (target is null) { go.Msg("You are nowhere to examine."); return; }
        }
        else
        {
            target = CommandHelpers.ResolveObject(go, targetStr!);
            if (target is null) return;
        }
        if (target is Node nodeTarget)
        {
            string areaName;
            try
            {
                var nh = NodeHandler.GetCurrent();
                var area = nh?.GetArea(nodeTarget.Coord.Area);
                areaName = area?.Name ?? nodeTarget.Coord.Area;
            }
            catch { areaName = nodeTarget.Coord.Area; }
            go.Msg($"Examining Node at {nodeTarget.Coord} in area '{areaName}', z={nodeTarget.Coord.Z} (#{target.Id}):");
        }
        else go.Msg($"Examining {target.Name} (#{target.Id}):");
        var collected = ExamFormatter.Collect(target);
        Dictionary<string, object?> dict = [];
        HashSet<string> propNames = [];
        foreach (var (k, v, p) in collected) { dict[k] = v; if (p) propNames.Add(k); }
        var keysInOrder = dict.Keys.ToList();
        foreach (var key in keysInOrder)
        {
            var val = dict[key];
            var valOutput = FormatValue(val, key);
            string typeName = val?.GetType().Name ?? "null";
            string marker = propNames.Contains(key) ? " [property]" : "";
            if (valOutput is List<string> list)
            {
                go.Msg($"  {key}: {list[0]} ({typeName}{marker})");
                for (int i = 1; i < list.Count; i++) go.Msg($"    {list[i]}");
            }
            else
            {
                string valStr = valOutput as string ?? valOutput?.ToString() ?? "<unprintable>";
                go.Msg($"  {key}: {valStr} ({typeName}{marker})");
            }
        }
    }

    private static object FormatValue(object? val, string? hint) => ExamFormatter.FormatValue(val, hint);
}

// maze generation with legend and threadpool pathfind

public sealed class MazeCommand : BuilderCommand
{
    public override string Key => "maze";
    public override string Desc => "Generate a maze.";
    public override string Category => "Building";
    public override bool UseParser => true;
    protected override bool AllowMissingArgs => true;

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

    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
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

/// <summary>
/// Parsed options for <see cref="MoveCommand"/>: the destination coordinate.
/// Carries the parse result so the move itself (<see cref="MoveCommand.RunMove"/>)
/// never re-parses text.
/// </summary>
public sealed record MoveOptions(Coord Dest);

public sealed class MoveCommand : BuilderCommand
{
    public override string Key => "move";
    public override string Desc => "Move to a coordinate.";
    public override string Category => "Building";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Coord).Nargs("+").Help("Coordinate: area x y z  or  (area,x,y,z)");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        if (pa.GetList(ParsedArgKeys.Coord).Count == 0) { go.Msg(PrintHelp()); return; }
        var raw = string.Join(" ", pa.GetList(ParsedArgKeys.Coord)).Trim();
        // One coord grammar, one home: the wide `TryParse` accepts every
        // documented shape (`area x y z`, `Area(X,Y,Z)`, `(area,x,y,z)`), the
        // strict comma-only form covers bare `area,x,y,z` (multi-word areas
        // without parens). The detailed failures keep the two diagnostics
        // apart: four parts with bad numbers vs any other shape (Usage).
        CoordParseFailure commaFailure = CoordParseFailure.None;
        bool ok = Coord.TryParse(raw, commaOnly: false, out var coord, out var wideFailure)
            || Coord.TryParse(raw, commaOnly: true, out coord, out commaFailure);
        if (!ok)
        {
            if (wideFailure == CoordParseFailure.Number || commaFailure == CoordParseFailure.Number)
            {
                go.Msg("x, y, and z must be integers.");
                return;
            }
            go.Msg("Usage: move <area> <x> <y> <z>  or  move (<area>,<x>,<y>,<z>)");
            return;
        }
        RunMove(go, new MoveOptions(coord), ct);
    }

    /// <summary>
    /// Moves <paramref name="go"/> to <see cref="MoveOptions.Dest"/>. The
    /// coordinate is already parsed; this step only resolves and moves.
    /// </summary>
    public void RunMove(GameObject go, MoveOptions opts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(go);
        ArgumentNullException.ThrowIfNull(opts);
        var coord = opts.Dest;
        var nh = NodeHandler.GetCurrent();
        var node = nh?.GetNode(coord);
        if (node is null) { go.Msg($"No node found at {coord}."); return; }
        if (go.MoveTo(node, force: true)) go.Msg($"Moved to {coord}.");
        else go.Msg($"Could not move to {coord}.");
    }
}

public sealed class NounCommand : BuilderCommand
{
    public override string Key => "noun";
    public override string Desc => "Set noun description in current room";
    public override string Category => "Building";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Noun, help: "noun to add or change");
        p.AddArgument(ParsedArgKeys.Desc, nargs: "REMAINDER", help: "desc to set for the noun");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pa.GetString(ParsedArgKeys.Noun)) || pa.GetList(ParsedArgKeys.Desc).Count == 0) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject() as Node;
        if (loc is null) { CommandHelpers.MsgNo(go); return; }
        string noun = pa.GetString(ParsedArgKeys.Noun)!;
        string desc = string.Join(" ", pa.GetList(ParsedArgKeys.Desc));
        // Atomic add-vs-update decision under the node write lock: a separate
        // GetNoun read here let two concurrent adds of the same new noun both
        // report "Added". Only the absent path inserts; the present path
        // overwrites via AddNoun. Messages stay byte-identical.
        if (loc.AddNounIfAbsent(noun, desc)) go.Msg($"Added '{noun}'.");
        else { loc.AddNoun(noun, desc); go.Msg($"Updated '{noun}'."); }
    }
}

// snapshot only is_pc/privilege_level wontfix

public sealed class PuppetCommand : BuilderCommand
{
    public override string Key => "puppet";
    public override string Desc => "Take control of an object, temporarily making it a player character.";
    public override string Category => "Building";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Target, help: "Object to puppet (name or #id)."); }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var query = pa.GetString(ParsedArgKeys.Target);
        if (string.IsNullOrWhiteSpace(query)) { go.Msg(PrintHelp()); return; }
        var sess = go.Session;
        if (sess is null) { go.Msg("You have no active session."); return; }
        var (target, err) = FindTarget(go, query!);
        if (err is not null) { go.Msg(err); return; }
        if (target == go) { go.Msg("You are already puppeting yourself."); return; }
        if (target!.IsAccount || target.IsChannel || target.IsNode) { go.Msg($"You cannot puppet {target.Name}."); return; }
// permission precedes occupancy
        // disclosure : unpermitted callers must not learn whether
        // the target is puppeted.
        if (!target.Access(go, "puppet")) { go.Msg($"You cannot puppet {target.Name}."); return; }
        if (target.IsDeleted) { go.Msg($"{target.Name} is not available."); return; }
        if (target.Session is not null && target.Session != sess) { go.Msg($"{target.Name} is already being puppeted."); return; }
        bool ok = go.Puppet(sess, target);
        if (!ok)
        {
            // Puppet may have failed due to race (already puppeted/deleted) — faithful to puppet.py:115-136
            if (target.Session is not null && target.Session != sess) go.Msg($"{target.Name} is already being puppeted.");
            else if (target.IsDeleted) go.Msg($"{target.Name} is not available.");
            else go.Msg($"You cannot puppet {target.Name}.");
            return;
        }
        // original python has no success msg; we keep no extra string to stay verbatim (wontfix: extra success msg removed)
        // go.Msg($"You are now puppeting {target.Name}."); // removed for fidelity
    }
    private static (GameObject? t, string? err) FindTarget(GameObject caller, string query)
    {
        if (query.StartsWith("#", StringComparison.Ordinal))
        {
            // Shared fetch, own gates (not BanHelper.ResolveTarget: that
            // enforces IsPc with ban wording which must not leak into
            // puppet errors).
            var (found, err) = TargetResolution.ResolveById(caller, query);
            if (err is not null) return (null, err);
            // Deleted objects are never puppetable; report unavailability here
            // so a stale #id cannot reach the puppet-lock branch (which names
            // the target). No view gate on this path: disconnected player
            // characters deny view to everyone while offline, and builders
            // must stay able to puppet them by #id — the puppet lock below
            // stays the authorization gate.
            if (found is null || found.IsDeleted) return (null, $"{found?.Name} is not available.");
            return (found, null);
        }
        // Name search runs through the shared resolver with no filter (any
        // object kind stays puppetable) and no header (the single-line
        // multi-match shape). The #id branch above keeps its own gates.
        return TargetResolution.ResolveObject(
            caller,
            query,
            filter: null,
            notFound: CommandHelpers.FormatNoMatchFound(query));
    }
}

public sealed class QuellCommand : LoggedInCommand
{
    public override string Key => "quell";
    public override IReadOnlyList<string> Aliases => ["q"];
    public override string Desc => "Quell your privileges to the level of a normal player.";
    public override string Category => "Building";
    public override bool UseParser => false;
    // Raw privilege check (no quelled fold): a quelled builder must still
    // reach Run for the already-quelled branch, mirroring UnquellCommand.
    public override bool Access(IMessageTarget caller) => CommandPermissions.HasBuilderPrivilege(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        if (go.Quelled) go.Msg("You are already quelled!");
        else { go.Quelled = true; go.Msg("You are now quelled."); }
    }
}

public sealed class SetCommand : BuilderCommand
{
    public override string Key => "set";
    public override string Desc => "Set an attribute on an object.";
    public override string Category => "Building";
    // Serializes PC renames: check-then-SetAttr must not straddle a
    // rival rename to the same fresh name.
    private static readonly Lock RenameLock = new();
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target, help: "Object to modify (name, #id, 'me', or 'here').");
        p.AddArgument(ParsedArgKeys.Attribute, help: "Attribute name to set.");
        // Multi-word values (`set me desc hello world`): OneOrMore keeps the
        // missing-value required error and dash handling of a single
        // positional; Run joins the tokens back with spaces.
        p.AddArgument(ParsedArgKeys.Value, help: "Value to set (evaluated with ast.literal_eval).", nargs: "+");
    }
    private static object? ConvertJsonElement(JsonElement je)
    {
        return je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.TryGetInt32(out var i) ? i : je.TryGetDouble(out var d) ? d : (object)je.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => je.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => je.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => je.GetRawText()
        };
    }
    private static bool MatchesWordAt(string s, int pos, string word)
    {
        if (pos + word.Length > s.Length) return false;
        if (!s.Substring(pos, word.Length).Equals(word, StringComparison.Ordinal)) return false;
        bool leftOk = pos == 0 || (!char.IsLetterOrDigit(s[pos - 1]) && s[pos - 1] != '_');
        int end = pos + word.Length;
        bool rightOk = end >= s.Length || (!char.IsLetterOrDigit(s[end]) && s[end] != '_');
        return leftOk && rightOk;
    }
    private static string ReplaceWordOutsideQuotes(string input, string word, string replacement)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (c == '"' || c == '\'')
            {
                char q = c;
                sb.Append(c); i++;
                while (i < input.Length)
                {
                    char d = input[i];
                    sb.Append(d);
                    if (d == '\\' && i + 1 < input.Length) { sb.Append(input[i + 1]); i += 2; continue; }
                    i++;
                    if (d == q) break;
                }
            }
            else if (MatchesWordAt(input, i, word)) { sb.Append(replacement); i += word.Length; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }
    private static string ConvertSingleQuotesOutsideDoubleQuotes(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool inDouble = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) { inDouble = !inDouble; sb.Append(c); }
            else if (c == '\'' && !inDouble) sb.Append('"');
            else sb.Append(c);
        }
        return sb.ToString();
    }
    private static string NormalizeValueText(string text)
    {
        string s = ConvertSingleQuotesOutsideDoubleQuotes(text);
        s = ReplaceWordOutsideQuotes(s, "True", "true");
        s = ReplaceWordOutsideQuotes(s, "False", "false");
        s = ReplaceWordOutsideQuotes(s, "None", "null");
        return s;
    }
    private static string ReprJson(object? value)
    {
        string repr;
        try { repr = JsonSerializer.Serialize(value); }
        catch { repr = value?.ToString() ?? "None"; }
        // Json gives lower-case true/false/null, map to Python
        if (repr == "true") repr = "True";
        else if (repr == "false") repr = "False";
        else if (repr == "null") repr = "None";
        return repr;
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var targetStr = pa.GetString(ParsedArgKeys.Target) ?? "";
        var attr = pa.GetString(ParsedArgKeys.Attribute) ?? "";
        // `value` is OneOrMore: join multi-word tokens back. The scalar
        // fallback covers programmatic ParsedArgs with a plain string.
        var valueTokens = pa.GetList(ParsedArgKeys.Value);
        var raw = valueTokens.Count > 0 ? string.Join(" ", valueTokens) : (pa.GetString(ParsedArgKeys.Value) ?? "");
        var target = SetHelper.ResolveTarget(go, targetStr);
        if (target is null) return;
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
        object? value;
        string trimmed = raw.Trim();
        string trimStart = raw.TrimStart();
        try
        {
            // tuple handling: Python ast.literal_eval supports tuples '(1,2)' -> treat as array
            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (inner.EndsWith(",", StringComparison.Ordinal)) inner = inner.Substring(0, inner.Length - 1).TrimEnd();
                string norm = "[" + inner + "]";
                norm = NormalizeValueText(norm);
                try
                {
                    var je2 = JsonSerializer.Deserialize<JsonElement>(norm);
                    value = ConvertJsonElement(je2);
                }
                catch { value = raw; }
            }
            // sign or dot still denotes a number (JSON parses "-5" natively;
            // "+5"/".5" are normalized first since JSON rejects them).
            else if (trimStart.StartsWith("\"", StringComparison.Ordinal) || trimStart.StartsWith("'", StringComparison.Ordinal) || trimmed == "True" || trimmed == "False" || trimmed == "None" || (trimmed.Length > 0 && (char.IsDigit(trimmed[0]) || trimmed[0] == '-' || trimmed[0] == '+' || trimmed[0] == '.')) || trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                string candidate = raw;
                string candTrim = candidate.TrimStart();
                if (candTrim.StartsWith("+", StringComparison.Ordinal)) candidate = candTrim.Substring(1);
                else if (candTrim.StartsWith(".", StringComparison.Ordinal)) candidate = "0" + trimStart;
                try { value = JsonSerializer.Deserialize<JsonElement>(candidate); }
                catch { value = null; }
                if (value is null)
                {
                    string norm = NormalizeValueText(raw);
                    try { value = JsonSerializer.Deserialize<JsonElement>(norm); }
                    catch { value = raw; }
                }
                if (value is JsonElement je)
                {
                    value = ConvertJsonElement(je);
                    if (value is string rawFallback && je.ValueKind != JsonValueKind.String && je.ValueKind != JsonValueKind.Number && je.ValueKind != JsonValueKind.True && je.ValueKind != JsonValueKind.False && je.ValueKind != JsonValueKind.Null)
                    {
                        // ConvertJsonElement returns raw string for unsupported array/object if fallback, but we want preserved lists/dicts
                        // Actually Convert handles arrays/objects; if it fell back to raw, keep raw
                        if (rawFallback == raw) value = rawFallback;
                    }
                }
            }
            else value = raw;
        }
        catch { value = raw; }
        if (value is null && trimmed != "None" && trimmed != "null") value = raw;
        if (SetHelper.IsProtected(attr))
        {
            if (!go.IsSuperUser) { go.Msg($"'{attr}' is protected and cannot be set."); return; }
        }
        if (SetHelper.MoveGate.Contains(attr)) { go.Msg($"'{attr}' cannot be set directly; use move/teleport instead."); return; }
        bool had = SetHelper.HasAttr(target, attr);
        if (!had) go.Msg($"Warning: '{attr}' is a new attribute on {target.Name}.");
        // `set <target> name <value>` renames: mirror the guest/new-verb
        // checks so a rename cannot take an illegal name or collide with an
        // existing character (a rename to the target's own current name is
        // not a collision). Only the exact spellings that resolve to the
        // Name setter are gated; junk-case variants land in extras as before.
        if (attr is "Name" or "name" or "_name" && value is not null)
        {
            string newName = value as string ?? value.ToString() ?? "";
            var nameErr = Validation.ValidateCharacterName(newName);
            if (nameErr is not null) { go.Msg(nameErr); return; }
            bool sameAsSelf = target.IsPc && target.Name.Equals(newName, StringComparison.OrdinalIgnoreCase);
            if (!sameAsSelf && CreationValidation.PcNameExists(newName)) { go.Msg($"Character with this name ({newName}) already exists."); return; }
            // Atomic rename reserve: the check above and the SetAttr
            // below must not straddle a rival rename to the same fresh name.
            // Both serialize on RenameLock with a re-check inside, so two
            // concurrent renames cannot both pass. No other path takes this
            // lock (RenameLock -> object order only), so it cannot deadlock.
            lock (RenameLock)
            {
                bool stillSelf = target.IsPc && target.Name.Equals(newName, StringComparison.OrdinalIgnoreCase);
                if (!stillSelf && CreationValidation.PcNameExists(newName)) { go.Msg($"Character with this name ({newName}) already exists."); return; }
                try
                {
                    SetHelper.SetAttr(target, attr, value);
                    target.IsModified = true;
                }
                catch (InvalidOperationException) { go.Msg($"'{attr}' is a read-only attribute and cannot be set."); return; }
                catch (InvalidCastException) { go.Msg($"'{attr}' cannot be set from text."); return; }
                catch (Exception ex) { go.Msg($"Could not set '{attr}': {ex.Message}"); return; }
            }
            string renameRepr = value switch
            {
                null => "None",
                string s => $"'{s}'",
                bool b => b ? "True" : "False",
                _ => ReprJson(value),
            };
            go.Msg($"Set {target.Name}.{attr} = {renameRepr}");
            return;
        }
        try
        {
            SetHelper.SetAttr(target, attr, value);
            target.IsModified = true;
        }
        catch (InvalidOperationException) { go.Msg($"'{attr}' is a read-only attribute and cannot be set."); return; }
        // A property whose type can never convert from text (e.g. LocationRef)
        // is unsettable from the command line, so it gets its own message
        // rather than the read-only one: the attribute exists, the text just
        // cannot become its type. Malformed values for convertible types fall
        // through to the conversion message below.
        catch (InvalidCastException) { go.Msg($"'{attr}' cannot be set from text."); return; }
        catch (Exception ex) { go.Msg($"Could not set '{attr}': {ex.Message}"); return; }
        // First-match order mirrors the old if/else chain (null, string,
        // bool, then JSON with Python True/False/None spellings).
        string repr = value switch
        {
            null => "None",
            string s => $"'{s}'",
            bool b => b ? "True" : "False",
            _ => ReprJson(value),
        };
        go.Msg($"Set {target.Name}.{attr} = {repr}");
    }
}

public sealed class UnbanCommand : BuilderCommand
{
    public override string Key => "unban";
    public override string Desc => "Unban a player character, optionally their account and/or IP.";
    public override string Category => "Building";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target).Help("Player character to unban (name or #id).");
        p.AddArgument("--account").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Unban the entire account and all its characters.");
        p.AddArgument("--ip").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Also clear an IP ban for the target's host (requires an online target).");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var targetName = pa.GetString(ParsedArgKeys.Target);
        if (string.IsNullOrWhiteSpace(targetName)) { go.Msg(PrintHelp()); return; }
        bool wantAccount = pa.GetBool(ParsedArgKeys.Account);
        bool ip = pa.GetBool(ParsedArgKeys.Ip);
        if (!BanHelper.TryResolveBanPreamble(go, targetName, wantAccount, ip, "unban", "unbanning",
            out var target, out var acct, out var acctChars, out var host, out bool account)
            || target is null)
            return;
        if (account && acct is not null)
        {
            // Atomic scope apply, mirror of the ban path (a non-Account
            // holder keeps the members-only shape on both verbs).
            var unbanScope = new List<GameObject>(acctChars);
            if (acct is Account) unbanScope.Add(acct);
            BanHelper.ApplyScopeState(unbanScope, false, "");
        }
        else { BanHelper.ApplyScopeState([target], false, ""); }
        if (ip)
        {
            if (host is null) go.Msg("Target is not online; cannot clear IP ban by reference.");
            else ObjectRegistry.UnbanIp(host);
        }
        string scope = account && acct is not null ? "account" : "character";
        go.Msg($"Unbanned {target.Name} ({scope}).");
    }
}

public sealed class UnpuppetCommand : BuilderCommand
{
    public override string Key => "unpuppet";
    public override string Desc => "Release the puppeted object and return to your previous one.";
    public override string Category => "Building";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        var sess = go.Session;
        if (sess is null) { go.Msg("You have no active session."); return; }
        bool ok = go.Unpuppet(sess);
        if (!ok) go.Msg("You are not puppeting anything.");
    }
}

public sealed class UnquellCommand : LoggedInCommand
{
    public override string Key => "unquell";
    public override IReadOnlyList<string> Aliases => ["unq"];
    public override string Desc => "Unquell your privileges.";
    public override string Category => "Building";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.HasBuilderPrivilege(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        if (!go.Quelled) go.Msg("You are not quelled!");
        else { go.Quelled = false; go.Msg("You are now unquelled."); }
    }
}

public sealed class UnsetCommand : BuilderCommand
{
    public override string Key => "unset";
    public override string Desc => "Delete an attribute from an object.";
    public override string Category => "Building";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target, help: "Object to modify (name, #id, 'me', or 'here').");
        p.AddArgument(ParsedArgKeys.Attribute, help: "Attribute name to delete.");
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var targetStr = pa.GetString(ParsedArgKeys.Target) ?? "";
        var attr = pa.GetString(ParsedArgKeys.Attribute) ?? "";
        var target = SetHelper.ResolveTarget(go, targetStr);
        if (target is null) return;
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
// only the shared protected set is checked.
        if (SetHelper.IsProtected(attr))
        {
            if (!go.IsSuperUser) { go.Msg($"'{attr}' is protected and cannot be removed."); return; }
        }
        if (SetHelper.MoveGate.Contains(attr)) { go.Msg($"'{attr}' cannot be removed directly."); return; }
        try
        {
            if (SetHelper.HasKnownProp(target, attr)) throw new InvalidOperationException();
            bool had = SetHelper.HasAttr(target, attr);
            // also check _extra directly via SetHelper
            if (!had)
            {
                // Fallback direct check already done in HasAttr; just verify
                go.Msg($"{target.Name} has no attribute '{attr}'.");
                return;
            }
            // Try extra removal via helper
            bool removed = SetHelper.TryRemoveExtra(target, attr);
            if (!removed) { go.Msg($"{target.Name} has no attribute '{attr}'."); return; }
            target.IsModified = true;
        }
        catch { go.Msg($"'{attr}' is a read-only attribute and cannot be removed."); return; }
        go.Msg($"Deleted {target.Name}.{attr}");
    }
}

public sealed class WanderCommand : BuilderCommand
{
    public override string Key => "wander";
    public override string Desc => "Spawn 10 NPCs to your location to wander around";
    public override string Category => "Building";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Count, nargs: "?", type: typeof(int), help: "Number of wanderers to spawn"); }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        if (!CommandHelpers.TryGetCount(pa, ParsedArgKeys.Count, 10, 1, out int count)) { go.Msg("Count must be a positive number."); return; }
        if (count > 1000) { go.Msg("Maximum count is 1000."); return; }
        var loc = go.ResolveLocationObject() as Node;
        if (loc is null) { go.Msg("You must be in a room to spawn wanderers."); return; }
        var nh = NodeHandler.GetCurrent();
        if (nh is null) { go.Msg("Could not find your current area."); return; }
        var area = nh.GetArea(loc.Coord.Area);
        if (area is null) { go.Msg("Could not find your current area."); return; }
        var grid = area.GetGrid(loc.Coord.Z);
        if (grid is null) { go.Msg("Could not find the grid for your current z-level."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            var randomNode = grid.GetRandomNode();
            if (randomNode is null) continue;
            var npc = SpawnUniqueWanderer();
            npc.MoveTo(randomNode);
            // Spawn-vs-remove race: the node may have been removed
            // between the pick and the move. An NPC homed in an unregistered
            // node is a limbo ghost — fall back to the spawner's room.
            if (!ReferenceEquals(ObjectRegistry.GetSingle(randomNode.Id), randomNode))
                npc.MoveTo(loc, force: true, announce: false);
        }
        sw.Stop();
        go.Msg($"Spawned {count} NPCs across area '{loc.Coord.Area}' in {sw.Elapsed.TotalMilliseconds:F2} milliseconds");
    }

    // Wanderer NNNN names must be unique: duplicates feed SearchWithFallback
    // "Multiple matches" ambiguity. Probe sequentially from a random start
    // so concurrent spawns spread out; the 9000-slot namespace always has a
    // free number in practice, and saturation throws loudly instead of
    // silently replacing an existing NPC.
    private static WandererNpc SpawnUniqueWanderer()
    {
        int start = Random.Shared.Next(1000, 9999);
        for (int i = 0; i < 9000; i++)
        {
            int n = 1000 + ((start - 1000 + i) % 9000);
            var npc = new WandererNpc();
            npc.Name = $"Wanderer {n}";
            try
            {
                ObjectRegistry.AddObjectUnique(npc, o => string.Equals(o.Name, npc.Name, StringComparison.OrdinalIgnoreCase), $"Duplicate wanderer name {npc.Name}.");
                return npc;
            }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Wanderer namespace saturated.");
    }
    internal sealed class WandererNpc : GameObject
    {
        // Load-path factory: ApplyDtoFields restores name/flags from the row,
        // so this takes no id and touches neither the generator nor the registry
        // (LoadObjects publishes converted rows itself).
        internal WandererNpc()
        {
            IsNpc = true;
            IsMapable = true;
            IsTickable = true;
            TickSeconds = 1.0;
        }
        public WandererNpc(string name) : this()
        {
            Name = name;
            ObjectRegistry.AddObject(this);
        }
        public override void AtTick()
        {
            var loc = ResolveLocationObject() as Node;
            if (loc is null) return;
            var link = loc.GetRandomLink();
            if (link is null) return;
            var nh = NodeHandler.GetCurrent();
            var node = nh?.GetNode(link.Coord);
            if (node is null) return;
            var oldArea = loc.Coord.Area;
            var newArea = node.Coord.Area;
            if (oldArea != newArea)
            {
                try { AtherizLogger.LogWarning($"NPC {Name} (#{Id}) crossing areas: {loc.Coord} -> {node.Coord} via link '{link.Name}' (link.coord={link.Coord})"); } catch (Exception) { }
                try { AtherizLogger.LogWarning($"Wanderer crossed area {oldArea} -> {newArea}"); } catch (Exception) { }
            }
            if (!MoveTo(node, toExit: link.Name))
            {
                // a stuck wanderer must not fail silently every tick.
                // Python ignores move_to's result (wander.py:30); the NPC has
                // no session to message, so the failure goes to the log.
                try { AtherizLogger.LogWarning($"NPC {Name} (#{Id}) failed to move {loc.Coord} -> {node.Coord} via link '{link.Name}'"); } catch (Exception) { }
            }
        }
    }
}
