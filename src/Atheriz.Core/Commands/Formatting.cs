namespace Atheriz.Core.Commands;

// Shared single-command help text. Centralizes the three identical copies:
// logged-in HelpCommand.PrintHelpFor, unlogged-in HelpCommand.PrintHelpFor,
// and Command.PrintHelp's no-parser branch. Mirrors Python help.py
// NO_PARSER_TEMPLATE ("\n{description}\n\nAliases: {aliases}\n").
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

// Centralized message dialects (the CommandHelpers half): the Python
// originals spell these differently per command; behavior is preserved
// exactly — one home for the literals, no unification.
public static partial class CommandHelpers
{
    public static void MsgNo(IMessageTarget go) => go.Msg("No.");
    public static void MsgNowhere(IMessageTarget go) => go.Msg("You are nowhere.");
    public static void MsgNowhereExclaim(IMessageTarget go) => go.Msg("You are nowhere!");
    public static void MsgInvalidLocation(IMessageTarget go) => go.Msg("You have an invalid location.");
    public static void MsgObjectNotFound(IMessageTarget go) => go.Msg("Object not found.");
    public static string FormatNoMatchFound(string name) => $"No match found for '{name}'.";
    public static void MsgNoMatchFound(IMessageTarget go, string name) => go.Msg(FormatNoMatchFound(name));
    public static string FormatCouldNotFind(string name) => $"Could not find '{name}'.";
    public static void MsgCouldNotFind(IMessageTarget go, string name) => go.Msg(FormatCouldNotFind(name));
    public static void MsgChannelViewDenied(IMessageTarget go) => go.Msg("You do not have permission to view this channel.");
    public static void MsgChannelSendDenied(IMessageTarget go) => go.Msg("You do not have permission to send to this channel.");
    public static void MsgNoChannelHistory(IMessageTarget go) => go.Msg("No history available.");
    public static void MsgMultipleMatches(IMessageTarget go, string name) => go.Msg($"Multiple matches for '{name}'.");
    public static void MsgMultipleMatchesColon(IMessageTarget go, string name) => go.Msg($"Multiple matches for '{name}':");
    public static void MsgMultipleMatchesFound(IMessageTarget go, string name) => go.Msg($"Multiple matches found for '{name}'.");
    public static string FormatMultipleMatchesIdList(IEnumerable<GameObject> matches)
        => $"Multiple matches: {string.Join(", ", matches.Select(m => $"#{m.Id} {m.Name}"))}. Use #id to pick one.";
}
