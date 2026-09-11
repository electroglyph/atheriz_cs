// Shared single-command help text. Centralizes the three identical copies:
// logged-in HelpCommand.PrintHelpFor, unlogged-in HelpCommand.PrintHelpFor,
// and Command.PrintHelp's no-parser branch. Mirrors Python help.py
// NO_PARSER_TEMPLATE ("\n{description}\n\nAliases: {aliases}\n").
namespace Atheriz.Core.Commands;

public static class HelpHelper
{
    public static string FormatFor(Command cmd)
    {
        if (cmd.Parser is not null) return cmd.PrintHelp();
        return FormatNoParser(cmd);
    }

    public static string FormatAliasList(Command cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return string.Join(", ", [cmd.Key, ..cmd.Aliases]);
    }

    public static string FormatNoParser(Command cmd)
    {
        string aliasStr = FormatAliasList(cmd);
        return $"\n{cmd.Desc}\n\nAliases: {aliasStr}\n" + cmd.ExtraDesc;
    }

    /// <summary>
    /// Shared global help lookup: sends the formatted help when
    /// <paramref name="query"/> names a visible accessible command in
    /// <paramref name="set"/> and returns true; otherwise returns false
    /// without sending anything (the caller owns the local tail and the
    /// not-found message).
    /// </summary>
    public static bool TryShowGlobal(CmdSet set, IMessageTarget caller, string query)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrEmpty(query)) return false;
        var cmd = set.Get(query);
        if (cmd is not null && cmd.Access(caller) && !cmd.Hide)
        {
            caller.Msg(FormatFor(cmd));
            return true;
        }
        return false;
    }
}
