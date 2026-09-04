using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class NoneCommand : Command
{
    public override string Key => "none";
    public override bool Hide => true;
    public override string Desc => "None.";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("none", nargs: "*", help: "None."); }
    public override void Run(IMessageTarget caller, object? args)
    {
        var pa = args as GameArgumentParser.ParsedArgs;
        string text = "";
        if (pa != null) text = string.Join(" ", pa.GetList("none"));
        else text = (args as string ?? "").Trim();
        if (string.IsNullOrEmpty(text)) { caller.Msg("Command not found."); return; }
        var ignored = AtherizSettings.Global.AutoAliasIgnoredKeys;
        var cmds = CommandRegistry.UnloggedIn.GetKeys().Where(k => !ignored.Contains(k)).ToList();
        if (cmds.Count == 0) { caller.Msg($"Command \"{text}\" not found."); return; }
        string? best = StringDistance.BestMatch(text.Split(' ')[0].ToLowerInvariant(), cmds);
        caller.Msg($"Command \"{text}\" not found, did you mean: \"{best}\"?");
    }
}
