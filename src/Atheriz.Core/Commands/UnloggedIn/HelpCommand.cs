namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class HelpCommand : Command
{
    public override string Key => "help";
    public override IReadOnlyList<string> Aliases => ["?"];
    public override string Desc => "Show help for commands.";
    public override string Category => "General";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("command", nargs: "?", help: "Command to get help on"); }
    private static string PrintHelpFor(Command cmd)
    {
        if (cmd.Parser != null) return cmd.PrintHelp();
        string aliasStr = cmd.Aliases.Count > 0 ? $"{cmd.Key}, {string.Join(", ", cmd.Aliases)}" : cmd.Key;
        return $"\n{cmd.Desc}\n\nAliases: {aliasStr}\n" + cmd.ExtraDesc;
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        var pa = args as GameArgumentParser.ParsedArgs;
        string? query = pa?.GetString("command")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(query)) query = (args as string ?? "").Trim().ToLowerInvariant();
        var cs = CommandRegistry.UnloggedIn;
        if (string.IsNullOrEmpty(query))
        {
            bool sr = false;
            int tw = 80;
            try
            {
                if (caller is Objects.GameObject goc)
                {
                    sr = goc.Session?.ScreenReader ?? false;
                    tw = (goc.Session?.TermWidth ?? 80) - 2; if (tw < 20) tw = 20;
                }
                else if (caller is Network.BaseConnection bc)
                {
                    sr = bc.Session.ScreenReader;
                    tw = bc.Session.TermWidth - 2; if (tw < 20) tw = 20;
                }
                else
                {
                    try { var sess = ((dynamic)caller).Session as Objects.Session; if (sess != null) { sr = sess.ScreenReader; tw = sess.TermWidth - 2; if (tw < 20) tw = 20; } } catch { }
                }
            }
            catch { }
            var cmds = cs.GetAll().Distinct().Where(c => !c.Hide && c.Access(caller)).OrderBy(c => c.Category).ThenBy(c => c.Key).ToList();
            caller.Msg("\n" + HelpFormatter.Format(cmds, sr, tw + 2));
            return;
        }
        var cmd = cs.Get(query!);
        if (cmd != null && cmd.Access(caller) && !cmd.Hide) { caller.Msg(PrintHelpFor(cmd)); return; }
        caller.Msg("Command not found.");
    }
}
