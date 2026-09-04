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
        if (caller is not GameObject puppet) { caller.Msg("You can't do that."); return; }
        if (args is not GameArgumentParser.ParsedArgs parsed)
        {
            var locRaw = puppet.ResolveLocationObject();
            if (locRaw == null)
            {
                if (!string.IsNullOrEmpty(puppet.Desc)) puppet.Msg(puppet.Desc);
                else puppet.Msg("You are nowhere.");
                return;
            }
            if (locRaw is not Node)
            {
                var appearance = puppet.AtLook(locRaw);
                if (appearance.Trim() == $"{locRaw.Name}:" && !string.IsNullOrEmpty(puppet.Desc))
                { puppet.Msg(puppet.Desc); return; }
                puppet.Msg(appearance);
                return;
            }
            if (!locRaw.Access(puppet, "view")) { puppet.Msg("You can't see anything."); return; }
            puppet.Msg(puppet.AtLook(locRaw));
            return;
        }
        var targets = parsed.GetList("target");
        if (targets.Count == 0)
        {
            var loc = puppet.ResolveLocationObject();
            if (loc == null)
            {
                if (!string.IsNullOrEmpty(puppet.Desc)) puppet.Msg(puppet.Desc);
                else puppet.Msg("You are nowhere.");
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
            return;
        }
        var targetName = string.Join(" ", targets);
        var found = puppet.Search(targetName);
        if (found.Count == 0)
        {
            var loc = puppet.ResolveLocationObject();
            if (loc != null && loc.Access(puppet, "view"))
            {
                // try loc search, noun, link
                var locFound = loc is Node n ? n.Search(targetName, true, puppet) : ContentUtils.Search(loc, targetName, id => Globals.ObjectRegistry.Get(id).FirstOrDefault(), true, puppet);
                if (locFound.Count > 0) found = locFound;
                else
                {
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
                    if (found.Count == 0) { puppet.Msg($"No match found for '{targetName}'."); return; }
                }
            }
            else
            {
                puppet.Msg($"No match found for '{targetName}'.");
                return;
            }
        }
        if (found.Count > 1) { puppet.Msg($"Multiple matches for '{targetName}'."); return; }
        puppet.Msg(puppet.AtLook(found[0]));
    }
}
