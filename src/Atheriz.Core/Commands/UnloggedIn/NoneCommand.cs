
namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class NoneCommand : Command
{
    public override string Key => "none";
    public override bool Hide => true;
    public override string Desc => "None.";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("none", nargs: "*", help: "None."); }
    // Port of atheriz/commands/unloggedin/none.py:NoneCommand (levenshtein
    // over the full text), gated like the logged-in twin and help: hidden
    // or inaccessible commands are never suggested (otherwise a typo oracle
    // leaks their names), aliases are not keys (GetAll, not GetKeys, so the
    // "?" alias is not suggestible), and verbs disabled by settings
    // (IsUnloggedInEnabled — dispatch demotes them to none) are not offered.
    public override void Run(IMessageTarget caller, object? args)
    {
        var pa = args as GameArgumentParser.ParsedArgs;
        string text = "";
        if (pa is not null) text = string.Join(" ", pa.GetList("none"));
        else text = (args as string ?? "").Trim();
        if (string.IsNullOrEmpty(text)) { caller.Msg("Command not found."); return; }
        var ignored = AtherizSettings.Global.AutoAliasIgnoredKeys;
        List<string> cmds = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var c in CommandRegistry.UnloggedIn.GetAll())
            if (!ignored.Contains(c.Key) && !c.Hide && c.Access(caller) && CommandDispatcher.IsUnloggedInEnabled(c))
                CommandHelpers.TryAddChoice(cmds, seen, c.Key);
        if (cmds.Count == 0) { caller.Msg($"Command \"{text}\" not found."); return; }
        string? best = StringDistance.BestMatch(text, cmds);
        caller.Msg($"Command \"{text}\" not found, did you mean: \"{best}\"?");
    }
}
