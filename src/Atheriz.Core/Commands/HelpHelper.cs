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

    public static string FormatNoParser(Command cmd)
    {
        string aliasStr = cmd.Aliases.Count > 0 ? $"{cmd.Key}, {string.Join(", ", cmd.Aliases)}" : cmd.Key;
        return $"\n{cmd.Desc}\n\nAliases: {aliasStr}\n" + cmd.ExtraDesc;
    }
}
