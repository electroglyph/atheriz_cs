// Port of atheriz/commands/loggedin/noun.py:34

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class NounCommand : Command
{
    public override string Key => "noun";
    public override string Desc => "Set noun description in current room";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("noun", help: "noun to add or change");
        p.AddArgument("desc", nargs: "REMAINDER", help: "desc to set for the noun");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null || string.IsNullOrWhiteSpace(pa.GetString("noun")) || pa.GetList("desc").Count == 0) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject() as Node;
        if (loc is null) { CommandHelpers.MsgNo(go); return; }
        string noun = pa.GetString("noun")!;
        string desc = string.Join(" ", pa.GetList("desc"));
        // Atomic add-vs-update decision under the node write lock: a separate
        // GetNoun read here let two concurrent adds of the same new noun both
        // report "Added". Only the absent path inserts; the present path
        // overwrites via AddNoun. Messages stay byte-identical.
        if (loc.AddNounIfAbsent(noun, desc)) go.Msg($"Added '{noun}'.");
        else { loc.AddNoun(noun, desc); go.Msg($"Updated '{noun}'."); }
    }
}