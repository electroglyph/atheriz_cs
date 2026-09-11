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
                try { sr = goc.Session?.ScreenReader ?? false; } catch (Exception) { }
                try { tw = (goc.Session?.TermWidth ?? 80) - 2; if (tw < 20) tw = 20; } catch { tw = 80; }
            }
            var all = CommandRegistry.LoggedIn.GetAll().Where(c => !c.Hide && c.Access(caller)).ToList();
            var sb = new StringBuilder("\n" + HelpFormatter.Format(all, sr, tw + 2));
            // local commands from location/inventory (single session lookup above)
            if (caller is Objects.GameObject go)
            {
                // Order-preserving dedup (set for membership, list for order):
                // the default comparer is reference equality here, matching
                // the old ReferenceEquals scan exactly.
                List<Command> locals = [];
                HashSet<Command> seenLocals = [];
                foreach (var set in CommandHelpers.LocalVerbSets(go))
                    foreach (var lc in set.GetAll())
                        if (!lc.Hide && lc.Access(go) && seenLocals.Add(lc))
                            locals.Add(lc);
                if (locals.Count > 0)
                {
                    sb.AppendLine("\nLocal commands:");
                    sb.Append(HelpFormatter.Format(locals, sr, tw + 2));
                }
            }
            caller.Msg(sb.ToString());
            return;
        }
        if (HelpHelper.TryShowGlobal(CommandRegistry.LoggedIn, caller, query!)) return;
        // search local
        if (caller is Objects.GameObject go2)
        {
            foreach (var set in CommandHelpers.LocalVerbSets(go2))
            {
                var c = set.Get(query!);
                if (c is not null && c.Access(go2) && !c.Hide) { caller.Msg(HelpHelper.FormatFor(c)); return; }
                foreach (var cc in set.GetAll()) if (cc.Aliases.Any(a => a.Equals(query, StringComparison.OrdinalIgnoreCase)) && cc.Access(go2) && !cc.Hide) { caller.Msg(HelpHelper.FormatFor(cc)); return; }
            }
        }
        caller.Msg("Command not found.");
    }
}
