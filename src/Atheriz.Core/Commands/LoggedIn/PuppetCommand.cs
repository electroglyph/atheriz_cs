// Port of atheriz/commands/loggedin/puppet.py:192 — snapshot only is_pc/privilege_level wontfix
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class PuppetCommand : Command
{
    public override string Key => "puppet";
    public override string Desc => "Take control of an object, temporarily making it a player character.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("target", help: "Object to puppet (name or #id)."); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        var query = pa?.GetString("target");
        if (string.IsNullOrWhiteSpace(query)) { go.Msg(PrintHelp()); return; }
        var sess = go.Session;
        if (sess == null) { go.Msg("You have no active session."); return; }
        var (target, err) = FindTarget(go, query!);
        if (err != null) { go.Msg(err); return; }
        if (target == go) { go.Msg("You are already puppeting yourself."); return; }
        if (target!.IsAccount || target.IsChannel || target.IsNode) { go.Msg($"You cannot puppet {target.Name}."); return; }
        if (target.IsDeleted) { go.Msg($"{target.Name} is not available."); return; }
        if (target.Session != null && target.Session != sess) { go.Msg($"{target.Name} is already being puppeted."); return; }
        if (!target.Access(go, "puppet")) { go.Msg($"You cannot puppet {target.Name}."); return; }
        bool ok = go.Puppet(sess, target);
        if (!ok)
        {
            // Puppet may have failed due to race (already puppeted/deleted) — faithful to puppet.py:115-136
            if (target.Session != null && target.Session != sess) go.Msg($"{target.Name} is already being puppeted.");
            else if (target.IsDeleted) go.Msg($"{target.Name} is not available.");
            else go.Msg($"You cannot puppet {target.Name}.");
            return;
        }
        // original python has no success msg; we keep no extra string to stay verbatim (wontfix: extra success msg removed)
        // go.Msg($"You are now puppeting {target.Name}."); // removed for fidelity
    }
    private static (GameObject? t, string? err) FindTarget(GameObject caller, string query)
    {
        if (query.StartsWith("#"))
        {
            if (!int.TryParse(query[1..], out var id)) return (null, "Invalid ID format. Use #<number>.");
            var res = ObjectRegistry.Get(id);
            if (res.Count == 0) return (null, $"No object found with ID {id}.");
            return (res[0], null);
        }
        var matches = CommandHelpers.SearchWithFallback(caller, query);
        if (matches.Count == 0) return (null, $"No match found for '{query}'.");
        if (matches.Count > 1) return (null, $"Multiple matches: {string.Join(", ", matches.Select(m => $"#{m.Id} {m.Name}"))}. Use #id to pick one.");
        return (matches[0], null);
    }
}

public sealed class UnpuppetCommand : Command
{
    public override string Key => "unpuppet";
    public override string Desc => "Release the puppeted object and return to your previous one.";
    public override string Category => "Building";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var sess = go.Session;
        if (sess == null) { go.Msg("You have no active session."); return; }
        bool ok = go.Unpuppet(sess);
        if (!ok) go.Msg("You are not puppeting anything.");
    }
}
