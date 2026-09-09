namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class HelpCommand : Command
{
    public override string Key => "help";
    public override IReadOnlyList<string> Aliases => ["?"];
    public override string Desc => "Show help for commands.";
    public override string Category => "General";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("command", nargs: "?", help: "Command to get help on"); }
    private static string PrintHelpFor(Command cmd) => HelpHelper.FormatFor(cmd);
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
                else if (caller is ISessionProvider sp && sp.Session is Objects.Session s2) { sr = s2.ScreenReader; tw = s2.TermWidth - 2; if (tw < 20) tw = 20; }
                // No dynamic fallback: GameObject/BaseConnection/ISessionProvider cover
                // all production callers and test doubles (MockCaller has no Session).
            }
            catch (Exception) { }
            var cmds = cs.GetAll().Where(c => !c.Hide && c.Access(caller)).OrderBy(c => c.Category).ThenBy(c => c.Key).ToList();
            caller.Msg("\n" + HelpFormatter.Format(cmds, sr, tw + 2));
            return;
        }
        var cmd = cs.Get(query!);
        if (cmd != null && cmd.Access(caller) && !cmd.Hide) { caller.Msg(PrintHelpFor(cmd)); return; }
        caller.Msg("Command not found.");
    }
}
