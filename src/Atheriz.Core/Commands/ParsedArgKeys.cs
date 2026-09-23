namespace Atheriz.Core.Commands;

// Canonical argument keys shared across commands. Each command defines its
// arguments via AddArgument and reads them via GetString/GetList/GetBool —
// both sides reference these constants instead of string literals, so a typo
// becomes a compile error instead of a silently missing argument.
// Values are the exact runtime keys; renaming a value re-keys the argument.
public static class ParsedArgKeys
{
    public const string Account = "account";
    public const string Args = "args";
    public const string Attribute = "attribute";
    public const string Command = "command";
    public const string Coord = "coord";
    public const string Desc = "desc";
    public const string Message = "message";
    public const string None = "none";
    public const string Noun = "noun";
    public const string Target = "target";
    public const string Text = "text";
}
