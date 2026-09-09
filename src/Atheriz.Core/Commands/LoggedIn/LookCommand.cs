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
        // Defense in depth : AtLook gates internally, but the call
        // site checks first so a denied target never reaches hooks/rendering.
        if (!found[0].Access(puppet, "view")) { puppet.Msg("You can't see anything."); return; }
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
            // show the location's own appearance as-is. The old code
            // substituted the VIEWER's desc when the appearance was a bare
            // "name:" (empty desc) — echoing self. Python just shows
            // caller.at_look(loc) (look.py:20-30,66-72).
            // Gated like the sibling paths: a denied container never reaches hooks/rendering.
            if (!loc.Access(puppet, "view")) { puppet.Msg("You can't see anything."); return; }
            puppet.Msg(puppet.AtLook(loc));
            return;
        }
        if (!loc.Access(puppet, "view")) { puppet.Msg("You can't see anything."); return; }
        puppet.Msg(puppet.AtLook(loc));
    }
}
