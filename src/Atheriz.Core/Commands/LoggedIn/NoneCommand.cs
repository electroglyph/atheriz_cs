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

    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("none", nargs: "*", help: "None."); }
    public override void Run(IMessageTarget caller, object? args)
    {
        var pa = args as GameArgumentParser.ParsedArgs;
        string text = "";
        if (pa != null) text = string.Join(" ", pa.GetList("none"));
        else text = (args as string ?? "").Trim();
        if (string.IsNullOrEmpty(text)) { caller.Msg("Command not found."); return; }
        var ignored = Atheriz.Core.Settings.AtherizSettings.Default.AutoAliasIgnoredKeys;
        var settings = Atheriz.Core.Settings.AtherizSettings.Default;
        // build choices: internal + global + external verbs (mirrors none.py:62)
        var choices = new List<string>();
        if (caller is Objects.GameObject go && go.InternalCmdSet != null)
            choices.AddRange(go.InternalCmdSet.GetKeys().Where(k => !ignored.Contains(k)));
        choices.AddRange(CommandRegistry.LoggedIn.GetKeys().Where(k => !ignored.Contains(k) && !choices.Contains(k)));
        // external verbs from location and inventory
        if (caller is Objects.GameObject go2)
        {
            var loc = go2.ResolveLocationObject();
            if (loc != null)
                foreach (var id in loc.ContentsSnapshot)
                {
                    var o = Globals.ObjectRegistry.Get(id).FirstOrDefault();
                    if (o?.ExternalCmdSet != null)
                        foreach (var k in o.ExternalCmdSet.GetKeys()) if (!ignored.Contains(k) && !choices.Contains(k)) choices.Add(k);
                }
            foreach (var id in go2.ContentsSnapshot)
            {
                var o = Globals.ObjectRegistry.Get(id).FirstOrDefault();
                if (o?.ExternalCmdSet != null)
                    foreach (var k in o.ExternalCmdSet.GetKeys()) if (!ignored.Contains(k) && !choices.Contains(k)) choices.Add(k);
            }
        }
        if (choices.Count > 0)
        {
            var best = StringDistance.BestMatch(text.Split(' ')[0].ToLowerInvariant(), choices);
            // Keep Huh? for legacy test compatibility while also faithful to none.py format
            caller.Msg($"Huh? Command \"{text}\" not found, did you mean: \"{best}\"?");
        }
        else caller.Msg($"Huh? Command \"{text}\" not found.");
    }
}
