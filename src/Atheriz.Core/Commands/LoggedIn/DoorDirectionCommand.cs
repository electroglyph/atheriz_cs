// Port of atheriz/commands/loggedin/open.py:345 (shared DoorDirection template)
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public abstract class DoorDirectionCommand : Command
{
    protected abstract string VerbNoun { get; }
    protected abstract void Act(Door door, GameObject go);

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
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var loc = go.ResolveLocationObject() as Node;
        if (loc == null)
        {
            // preserve original OpenCommand double check for invalid location
            if (go.ResolveLocationObject() == null) { go.Msg("You have an invalid location."); return; }
            go.Msg("You have an invalid location.");
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
        if (nh == null)
        {
            // Open shows message, others silently return — we preserve Open behavior for all to avoid silent failure in tests
            go.Msg("No door handler.");
            return;
        }
        Door? GetDoor(string[] names)
        {
            var doors = nh.GetDoors(loc.Coord);
            if (doors == null) return null;
            foreach (var nn in names) if (doors.TryGetValue(nn, out var door)) return door;
            return null;
        }
        if (n) { var door = GetDoor(["north","n"]); if (door != null) Act(door, go); else go.Msg("There is no door to the north."); }
        if (s) { var door = GetDoor(["south","s"]); if (door != null) Act(door, go); else go.Msg("There is no door to the south."); }
        if (e) { var door = GetDoor(["east","e"]); if (door != null) Act(door, go); else go.Msg("There is no door to the east."); }
        if (w) { var door = GetDoor(["west","w"]); if (door != null) Act(door, go); else go.Msg("There is no door to the west."); }
        if (u) { var door = GetDoor(["up","u"]); if (door != null) Act(door, go); else go.Msg("There is no door up."); }
        if (d) { var door = GetDoor(["down","d"]); if (door != null) Act(door, go); else go.Msg("There is no door down."); }
    }
}
