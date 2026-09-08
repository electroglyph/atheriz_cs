using Atheriz.Core.Utils;

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>
/// Fallback unknown command. Mirrors <c>atheriz/commands/loggedin/none.py:NoneCommand</c> (62 LOC).
/// </summary>
public sealed class NoneCommand : Command
{
    public override string Key => "none";
    public override bool Hide => true;
    public override string Desc => "Fallback for unknown commands.";
    public override bool UseParser => false;

    public override void Run(IMessageTarget caller, object? args)
    {
        var pa = args as GameArgumentParser.ParsedArgs;
        string text = "";
        if (pa != null) text = string.Join(" ", pa.GetList("none"));
        else text = (args as string ?? "").Trim();
        if (string.IsNullOrEmpty(text)) { caller.Msg("Command not found."); return; } // none.py:25
        var ignored = Atheriz.Core.Settings.AtherizSettings.Global.AutoAliasIgnoredKeys;
        // Port of none.py:28-36: internal + global keys, ignored-only filter
        // (no Hide/Access gate — hidden commands are suggested upstream too).
        var choices = new List<string>();
        if (caller is Objects.GameObject go && go.InternalCmdSet != null)
            foreach (var k in go.InternalCmdSet.GetKeys())
                if (!ignored.Contains(k) && !choices.Contains(k)) choices.Add(k);
        foreach (var k in CommandRegistry.LoggedIn.GetKeys())
            if (!ignored.Contains(k) && !choices.Contains(k)) choices.Add(k);
        // Port of none.py:37-54: external verbs from location + inventory.
        if (caller is Objects.GameObject go2)
        {
            try
            {
                foreach (var set in CommandHelpers.LocalVerbSets(go2))
                    foreach (var k in set.GetKeys())
                        if (!ignored.Contains(k) && !choices.Contains(k)) choices.Add(k);
            }
            catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed NoneCommand externals: " + logEx.Message, "NoneCommand"); }
        }
        if (choices.Count > 0)
        {
            // Port of none.py:56-60: levenshtein over the full text, case-sensitive.
            var best = StringDistance.BestMatch(text, choices);
            caller.Msg($"Command \"{text}\" not found, did you mean: \"{best}\"?");
        }
        else caller.Msg($"Command \"{text}\" not found.");
    }
}
