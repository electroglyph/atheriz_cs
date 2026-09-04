using System.Text;

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>
/// Mirrors <c>atheriz/commands/loggedin/help.py:HelpCommand</c> (116 LOC).
/// </summary>
public sealed class HelpCommand : Command
{
    public override string Key => "help";
    public override IReadOnlyList<string> Aliases => ["?"];
    public override string Desc => "Show help for commands.";
    public override bool UseParser => true;

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
        if (string.IsNullOrEmpty(query))
        {
            // mirror help.py:30-38 term_width and screenreader from session
            bool sr = false;
            int tw = 80;
            if (caller is Objects.GameObject goc)
            {
                try { sr = goc.Session?.ScreenReader ?? false; } catch { }
                try { tw = (goc.Session?.TermWidth ?? 80) - 2; if (tw < 20) tw = 20; } catch { tw = 80; }
            }
            var all = CommandRegistry.LoggedIn.GetAll().Distinct().Where(c => !c.Hide && c.Access(caller)).ToList();
            var sb = new StringBuilder(HelpFormatter.Format(all, sr, tw + 2));
            // local commands from location/inventory
            if (caller is Objects.GameObject go)
            {
                bool sr2 = false; int tw2 = 80;
                try { sr2 = go.Session?.ScreenReader ?? false; } catch { }
                try { tw2 = (go.Session?.TermWidth ?? 80) - 2; if (tw2 < 20) tw2 = 20; } catch { tw2 = 80; }
                var loc = go.ResolveLocationObject();
                var locals = new List<Command>();
                if (loc != null)
                    foreach (var id in loc.ContentsSnapshot)
                    {
                        var o = Globals.ObjectRegistry.Get(id).FirstOrDefault();
                        if (o?.ExternalCmdSet != null) locals.AddRange(o.ExternalCmdSet.GetAll().Where(cmd => !cmd.Hide && cmd.Access(go)));
                    }
                foreach (var id in go.ContentsSnapshot)
                {
                    var o = Globals.ObjectRegistry.Get(id).FirstOrDefault();
                    if (o?.ExternalCmdSet != null) locals.AddRange(o.ExternalCmdSet.GetAll().Where(cmd => !cmd.Hide && cmd.Access(go)));
                }
                if (locals.Count > 0)
                {
                    sb.AppendLine("\nLocal commands:");
                    sb.Append(HelpFormatter.Format(locals, sr2, tw2 + 2));
                }
            }
            caller.Msg(sb.ToString());
            return;
        }
        var cmd = CommandRegistry.LoggedIn.Get(query!);
        if (cmd != null && cmd.Access(caller) && !cmd.Hide) { caller.Msg(PrintHelpFor(cmd)); return; }
        // search local
        if (caller is Objects.GameObject go2)
        {
            var loc = go2.ResolveLocationObject();
            if (loc != null)
                foreach (var id in loc.ContentsSnapshot)
                {
                    var o = Globals.ObjectRegistry.Get(id).FirstOrDefault();
                    if (o?.ExternalCmdSet != null)
                    {
                        var c = o.ExternalCmdSet.Get(query!);
                        if (c != null && c.Access(go2) && !c.Hide) { caller.Msg(PrintHelpFor(c)); return; }
                        foreach (var cc in o.ExternalCmdSet.GetAll()) if (cc.Aliases.Any(a => a.Equals(query, StringComparison.OrdinalIgnoreCase)) && cc.Access(go2) && !cc.Hide) { caller.Msg(PrintHelpFor(cc)); return; }
                    }
                }
            foreach (var id in go2.ContentsSnapshot)
            {
                var o = Globals.ObjectRegistry.Get(id).FirstOrDefault();
                if (o?.ExternalCmdSet != null)
                {
                    var c = o.ExternalCmdSet.Get(query!);
                    if (c != null && c.Access(go2) && !c.Hide) { caller.Msg(PrintHelpFor(c)); return; }
                    foreach (var cc in o.ExternalCmdSet.GetAll()) if (cc.Aliases.Any(a => a.Equals(query, StringComparison.OrdinalIgnoreCase)) && cc.Access(go2) && !cc.Hide) { caller.Msg(PrintHelpFor(cc)); return; }
                }
            }
        }
        caller.Msg("Command not found.");
    }
}
