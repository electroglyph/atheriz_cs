namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class HelpCommand : Command
{
    public override string Key => "help";
    public override IReadOnlyList<string> Aliases => ["?"];
    public override string Desc => "Show help for commands.";
    public override string Category => "General";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("command", nargs: "?", help: "Command to get help on"); }

    // Help-table width clamp shared by every caller shape below: the table
    // reserves two columns, and widths below 20 collapse.
    private static int ClampTermWidth(int tw) => tw < 20 ? 20 : tw;

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
                // GameObject and BaseConnection both carry ISessionProvider,
                // so one branch covers every caller shape. The null-conditional
                // defaults reproduce each branch's arithmetic exactly: a
                // session-less caller reads (null ?? 80) - 2 = 78, never 80.
                if (caller is ISessionProvider sp)
                {
                    sr = sp.Session?.ScreenReader ?? false;
                    tw = ClampTermWidth((sp.Session?.TermWidth ?? 80) - 2);
                }
                // No dynamic fallback: GameObject/BaseConnection/ISessionProvider cover
                // all production callers and test doubles (MockCaller has no Session).
            }
            catch (Exception) { }
            var cmds = cs.GetAll().Where(c => !c.Hide && c.Access(caller)).OrderBy(c => c.Category).ThenBy(c => c.Key).ToList();
            caller.Msg("\n" + HelpFormatter.Format(cmds, sr, tw + 2));
            return;
        }
        if (HelpHelper.TryShowGlobal(cs, caller, query!)) return;
        caller.Msg("Command not found.");
    }
}
