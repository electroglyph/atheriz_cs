// Port of atheriz/commands/loggedin/noun.py:34
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;

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
        p.AddArgument("desc", nargs: "*", help: "desc to set for the noun");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null || string.IsNullOrWhiteSpace(pa.GetString("noun")) || pa.GetList("desc").Count == 0) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject() as Node;
        if (loc == null) { go.Msg("No."); return; }
        string noun = pa.GetString("noun")!;
        string desc = string.Join(" ", pa.GetList("desc"));
        string mode = loc.GetNoun(noun) != null ? "Updated" : "Added";
        loc.AddNoun(noun, desc);
        go.Msg($"{mode} '{noun}'.");
    }
}