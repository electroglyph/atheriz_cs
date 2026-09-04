// Port of atheriz/commands/loggedin/desc.py:35
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DescCommand : Command
{
    public override string Key => "desc";
    public override string Desc => "Change current room description, use \\n for newlines.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("text", nargs: "*", help: "New description.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
        var lst = pa.GetList("text");
        if (lst.Count > 0)
        {
            var loc = go.ResolveLocationObject();
            if (loc == null) { go.Msg("You are nowhere!"); return; }
            string newDesc = string.Join(" ", lst).Replace("\\n", "\n");
            if (loc is Node node) node.Desc = newDesc;
            else loc.Desc = newDesc;
            // at_look
            try { go.Msg(go.AtLook(loc)); } catch { go.Msg(newDesc); }
        }
        else go.Msg(PrintHelp());
    }
}