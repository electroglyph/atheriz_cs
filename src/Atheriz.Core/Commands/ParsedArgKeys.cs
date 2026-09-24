namespace Atheriz.Core.Commands;

// Canonical argument keys shared across commands. Each command defines its
// arguments via AddArgument and reads them via GetString/GetList/GetBool —
// both sides reference these constants instead of string literals, so a typo
// becomes a compile error instead of a silently missing argument.
// Values are the exact runtime keys; renaming a value re-keys the argument.
public static class ParsedArgKeys
{
    public const string Account = "account";
    public const string AccountName = "account_name";
    public const string Args = "args";
    public const string Attribute = "attribute";
    public const string Auto = "auto";
    public const string Channel = "channel";
    public const string Command = "command";
    public const string Coord = "coord";
    public const string Count = "count";
    public const string D = "d";
    public const string Desc = "desc";
    public const string Double = "double";
    public const string Down = "down";
    public const string E = "e";
    public const string East = "east";
    public const string Ip = "ip";
    public const string IsContainer = "is_container";
    public const string IsItem = "is_item";
    public const string IsMapable = "is_mapable";
    public const string IsNpc = "is_npc";
    public const string IsPc = "is_pc";
    public const string IsTickable = "is_tickable";
    public const string List = "list";
    public const string Message = "message";
    public const string N = "n";
    public const string Name = "name";
    public const string None = "none";
    public const string North = "north";
    public const string Noun = "noun";
    public const string Object = "object";
    public const string Password = "password";
    public const string Path = "path";
    public const string Reason = "reason";
    public const string Recursive = "recursive";
    public const string Remove = "remove";
    public const string Replay = "replay";
    public const string Road = "road";
    public const string Room = "room";
    public const string Round = "round";
    public const string S = "s";
    public const string Single = "single";
    public const string South = "south";
    public const string Subscribe = "subscribe";
    public const string Target = "target";
    public const string Text = "text";
    public const string U = "u";
    public const string Unsubscribe = "unsubscribe";
    public const string Up = "up";
    public const string Value = "value";
    public const string W = "w";
    public const string West = "west";
    public const string X = "x";
}
