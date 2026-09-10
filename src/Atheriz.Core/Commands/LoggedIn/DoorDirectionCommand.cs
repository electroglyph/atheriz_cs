// Port of atheriz/commands/loggedin/open.py:345 (shared DoorDirection template)

namespace Atheriz.Core.Commands.LoggedIn;

public abstract class DoorDirectionCommand : Command
{
    protected abstract string VerbNoun { get; }
    protected abstract void Act(Door door, GameObject go);
    public override string ExtraDesc => "Also accepts n,s,e,w,u,d as arguments.";

    protected sealed override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("-n", "--north").Help("North").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-s", "--south").Help("South").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-e", "--east").Help("East").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-w", "--west").Help("West").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-u", "--up").Help("Up").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-d", "--down").Help("Down").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("args").Help("Other args").Nargs("*");
    }

    public sealed override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        // single location resolve (open.py reads caller.location once).
        var loc = go.ResolveLocationObject() as Node;
        if (loc is null)
        {
            CommandHelpers.MsgInvalidLocation(go);
            return;
        }
        var pa = args as GameArgumentParser.ParsedArgs;
        var lower = pa?.GetList("args").Select(a => a.ToLowerInvariant()).ToList() ?? [];
        bool n = pa?.GetBool("north") == true || lower.Contains("n") || lower.Contains("north");
        bool s = pa?.GetBool("south") == true || lower.Contains("s") || lower.Contains("south");
        bool e = pa?.GetBool("east") == true || lower.Contains("e") || lower.Contains("east");
        bool w = pa?.GetBool("west") == true || lower.Contains("w") || lower.Contains("west");
        bool u = pa?.GetBool("up") == true || lower.Contains("u") || lower.Contains("up");
        bool d = pa?.GetBool("down") == true || lower.Contains("d") || lower.Contains("down");
        if (!(n || s || e || w || u || d))
        {
            string cap = char.ToUpperInvariant(VerbNoun[0]) + VerbNoun.Substring(1);
            go.Msg($"{cap} what?");
            go.Msg(PrintHelp());
            return;
        }
        var nh = NodeHandler.GetCurrent();
        if (nh is null)
        {
            // Open shows message, others silently return — we preserve Open behavior for all to avoid silent failure in tests
            go.Msg("No door handler.");
            return;
        }
        Door? GetDoor(string[] names)
        {
            var doors = nh.GetDoors(loc.Coord);
            if (doors is null) return null;
            foreach (var nn in names) if (doors.TryGetValue(nn, out var door)) return door;
            return null;
        }
        // Row order is the established act order (n, s, e, w, u, d). The up/down
        // rows carry their own wording ("no door up", no "to the") — keep the
        // per-row message, never a shared template.
        var dirs = new (string longName, string shortName, bool active, string missing)[]
        {
            ("north", "n", n, "There is no door to the north."),
            ("south", "s", s, "There is no door to the south."),
            ("east", "e", e, "There is no door to the east."),
            ("west", "w", w, "There is no door to the west."),
            ("up", "u", u, "There is no door up."),
            ("down", "d", d, "There is no door down."),
        };
        foreach (var (longName, shortName, active, missing) in dirs)
        {
            if (!active) continue;
            var door = GetDoor([longName, shortName]);
            if (door is not null) Act(door, go);
            else go.Msg(missing);
        }
    }
}
