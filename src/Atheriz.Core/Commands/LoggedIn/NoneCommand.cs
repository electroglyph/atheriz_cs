
namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>
/// Fallback unknown command. Mirrors <c>atheriz/commands/loggedin/none.py:NoneCommand</c> (62 LOC).
/// </summary>
public sealed class NoneCommand : Command
{
    public override string Key => "none";
    public override bool Hide => true;
    public override string Desc => "Fallback for unknown commands.";
    // Same parsed shape as the unlogged fallback: identical inputs suggest
    // identically pre/post login (quotes, dash-input) instead of raw-vs-parsed.
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.None, nargs: "*", help: "Fallback for unknown commands."); }

    public override void Run(IMessageTarget caller, object? args)
    {
        var pa = args as GameArgumentParser.ParsedArgs;
        string text = "";
        if (pa is not null) text = string.Join(" ", pa.GetList(ParsedArgKeys.None));
        else text = (args as string ?? "").Trim();
        if (string.IsNullOrEmpty(text)) { caller.Msg("Command not found."); return; } // none.py:25
        var ignored = Atheriz.Core.Settings.AtherizSettings.Global.AutoAliasIgnoredKeys;
        // hidden or inaccessible commands are never suggested (otherwise a
        // typo oracle leaks their names to callers who cannot use them).
        List<string> choices = [];
        // Order-preserving dedup (set for membership, list for order):
        // insertion order feeds BestMatch's stable tie-breaks.
        HashSet<string> seen = new(StringComparer.Ordinal);
        if (caller is Objects.GameObject go && go.InternalCmdSet is not null)
            foreach (var c in go.InternalCmdSet.GetAll())
                if (!ignored.Contains(c.Key) && !c.Hide && c.Access(caller)) CommandHelpers.TryAddChoice(choices, seen, c.Key);
        foreach (var c in CommandRegistry.LoggedIn.GetAll())
            if (!ignored.Contains(c.Key) && !c.Hide && c.Access(caller)) CommandHelpers.TryAddChoice(choices, seen, c.Key);
        if (caller is Objects.GameObject go2)
        {
            try
            {
                foreach (var set in CommandHelpers.LocalVerbSets(go2))
                    foreach (var c in set.GetAll())
                        if (!ignored.Contains(c.Key) && !c.Hide && c.Access(go2)) CommandHelpers.TryAddChoice(choices, seen, c.Key);
            }
            catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed NoneCommand externals: " + logEx.Message, "NoneCommand"); }
        }
        if (choices.Count > 0)
        {
            var best = StringDistance.BestMatch(text, choices);
            caller.Msg($"Command \"{text}\" not found, did you mean: \"{best}\"?");
        }
        else caller.Msg($"Command \"{text}\" not found.");
    }
}
