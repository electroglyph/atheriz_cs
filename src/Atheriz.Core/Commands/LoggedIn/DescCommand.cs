// Port of atheriz/commands/loggedin/desc.py:35

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DescCommand : Command
{
    public override string Key => "desc";
    public override string Desc => "Change current room description, use \\n for newlines.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("text", nargs: "REMAINDER", help: "New description.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null) { go.Msg(PrintHelp()); return; }
        var lst = pa.GetList("text");
        if (lst.Count > 0)
        {
            var loc = go.ResolveLocationObject();
            if (loc is null) { CommandHelpers.MsgNowhereExclaim(go); return; }
            string newDesc = string.Join(" ", lst).Replace("\\n", "\n");
            if (loc is Node node) node.Desc = newDesc;
            else loc.Desc = newDesc;
            // at_look
            try { go.Msg(go.AtLook(loc)); } catch { go.Msg(newDesc); }
        }
        else go.Msg(PrintHelp());
    }
}