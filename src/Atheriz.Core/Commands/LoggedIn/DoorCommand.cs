// Port of atheriz/commands/loggedin/door.py:482
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DoorCommand : Command
{
    public override string Key => "door";
    public override string Desc => "Manage doors.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
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
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
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
        if (loc == null) { go.Msg("You have an invalid location."); return; }
        var nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
        if (remove)
        {
            var doors = nh.GetDoors(loc.Coord);
            if (doors == null || doors.Count==0) { go.Msg("There are no doors here."); return; }
            void TryRemove(string longName, string shortName)
            {
                Door? d = null;
                if (doors.TryGetValue(longName, out var dd)) d = dd;
                else if (doors.TryGetValue(shortName, out var d2)) d = d2;
                if (d != null) { nh.RemoveDoor(d); go.Msg($"Removed {d}"); }
                else go.Msg($"There is no door {longName}.");
            }
            if (north) TryRemove("north","n");
            if (south) TryRemove("south","s");
            if (east) TryRemove("east","e");
            if (west) TryRemove("west","w");
            if (up) TryRemove("up","u");
            if (down) TryRemove("down","d");
            return;
        }
        var settings = AtherizSettings.Default;
        var defs = new (string flag, string longName, string shortName, int dx,int dy,int dz, string closed, string open)[]
        {
            ("north","north","n",0,1,0, settings.NsClosedDoor, settings.NsOpenDoor1),
            ("south","south","s",0,-1,0, settings.NsClosedDoor, settings.NsOpenDoor2),
            ("east","east","e",1,0,0, settings.EwClosedDoor, settings.EwOpenDoor1),
            ("west","west","w",-1,0,0, settings.EwClosedDoor, settings.EwOpenDoor2),
            ("up","up","u",0,0,1, settings.UdClosedDoor, settings.UdOpenDoor),
            ("down","down","d",0,0,-1, settings.UdClosedDoor, settings.UdOpenDoor),
        };
        foreach (var def in defs)
        {
            bool active = def.flag switch { "north"=>north, "south"=>south, "east"=>east, "west"=>west, "up"=>up, "down"=>down, _=>false };
            if (!active) continue;
            var toCoord = new Coord(loc.Coord.Area, loc.Coord.X + def.dx*2, loc.Coord.Y + def.dy*2, loc.Coord.Z + def.dz*2);
            var doorCoord = new Coord(loc.Coord.Area, loc.Coord.X + def.dx, loc.Coord.Y + def.dy, loc.Coord.Z + def.dz);
            var toNode = nh.GetNode(toCoord);
            if (toNode == null)
            {
                if (auto) { toNode = new Node(toCoord); nh.AddNode(toNode); }
                else { go.Msg($"There is no node at the destination coord {toCoord}, use -a to auto-create it."); return; }
            }
            ReplaceNodeWithDoor(nh, doorCoord, go, loc);
            string oppLong = def.longName switch { "north"=>"south", "south"=>"north", "east"=>"west", "west"=>"east", "up"=>"down", "down"=>"up", _=>"" };
            string oppShort = oppLong switch { "north"=>"n", "south"=>"s", "east"=>"e", "west"=>"w", "up"=>"u", "down"=>"d", _=>"" };
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
        if (node == null) return;
        foreach (var obj in node.GetContents().ToList())
        {
            obj.MoveTo(fallback, force:true, announce:false);
        }
        nh.RemoveNode(doorCoord);
        caller.Msg($"Removed node at {doorCoord} since a door is being placed there.");
    }
}
