using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>
/// Mirrors <c>atheriz/commands/loggedin/look.py:LookCommand</c> (72 LOC).
/// </summary>
public sealed class LookCommand : Command
{
    public override string Key => "look";
    public override IReadOnlyList<string> Aliases => ["l"];
    public override string Desc => "Look at your current location or an object.";
    public override string Category => "General";

    protected override void SetupParser(GameArgumentParser parser)
    {
        parser.AddArgument("target", help: "Object to look at.", nargs: "REMAINDER");
    }

    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var puppet)) return;
        if (args is not GameArgumentParser.ParsedArgs parsed)
        {
            ShowLocation(puppet);
            return;
        }
        var targets = parsed.GetList("target");
        if (targets.Count == 0)
        {
            ShowLocation(puppet);
            return;
        }
        var targetName = string.Join(" ", targets);
        // SearchWithFallback covers caller + loc fallback and global #id
        // (same resolution as Exam/Delete via ResolveObject, minus messaging).
        var found = CommandHelpers.SearchWithFallback(puppet, targetName);
        if (found.Count == 0)
        {
            var loc = puppet.ResolveLocationObject();
            if (loc != null && loc.Access(puppet, "view"))
            {
                // noun/link fallback (not part of SearchWithFallback)
                if (loc is Node node)
                {
                    var noun = node.GetNoun(targetName.ToLowerInvariant());
                    if (noun != null) { puppet.Msg(noun); return; }
                    var link = node.GetLinks().FirstOrDefault(l => l.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) || l.Aliases.Any(a => a.Equals(targetName, StringComparison.OrdinalIgnoreCase)));
                    if (link != null)
                    {
                        var nh = Globals.NodeHandler.GetCurrent();
                        var ln = nh?.GetNode(link.Coord);
                        if (ln != null) { puppet.Msg(ln.ReturnAppearance(puppet)); return; }
                    }
                }
                CommandHelpers.MsgNoMatchFound(puppet, targetName);
                return;
            }
            else
            {
                CommandHelpers.MsgNoMatchFound(puppet, targetName);
                return;
            }
        }
        if (found.Count > 1) { CommandHelpers.MsgMultipleMatches(puppet, targetName); return; }
        puppet.Msg(puppet.AtLook(found[0]));
    }

    private static void ShowLocation(GameObject puppet)
    {
        var loc = puppet.ResolveLocationObject();
        if (loc == null)
        {
            if (!string.IsNullOrEmpty(puppet.Desc)) puppet.Msg(puppet.Desc);
            else CommandHelpers.MsgNowhere(puppet);
            return;
        }
        if (loc is not Node)
        {
            var appearance = puppet.AtLook(loc);
            if (appearance.Trim() == $"{loc.Name}:" && !string.IsNullOrEmpty(puppet.Desc))
            { puppet.Msg(puppet.Desc); return; }
            puppet.Msg(appearance);
            return;
        }
        if (!loc.Access(puppet, "view")) { puppet.Msg("You can't see anything."); return; }
        puppet.Msg(puppet.AtLook(loc));
    }
}
