// Port of atheriz/commands/loggedin/door.py:482

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DoorCommand : Command
{
    public override string Key => "door";
    public override string Desc => "Manage doors.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
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
        parser.AddArgument("args", nargs: "*", help: "Other args");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null) { go.Msg(PrintHelp()); return; }
        bool north = pa.GetBool("north"), south = pa.GetBool("south"), east = pa.GetBool("east"), west = pa.GetBool("west"), up = pa.GetBool("up"), down = pa.GetBool("down");
        bool remove = pa.GetBool("remove"), auto = pa.GetBool("auto");
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
            // Port of door.py: to_node link handling (verbatim messages)
            var toLinks = toNode.GetLinks();
            bool needDestLink = true;
            foreach (var l in toLinks.ToList())
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
            foreach (var l in hereLinks.ToList())
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
        foreach (var obj in node.GetContents().ToList())
        {
            obj.MoveTo(fallback, force:true, announce:false);
            // Python moves occupants silently; tell them what happened.
            // (Authorization is the command-level builder gate; per-occupant
            // consent hooks don't exist in either codebase.)
            try { obj.Msg($"A door is being placed where you stand; you are moved to {fallback.Name}."); } catch (Exception) { }
        }
        nh.RemoveNode(doorCoord);
        caller.Msg($"Removed node at {doorCoord} since a door is being placed there.");
    }
}
