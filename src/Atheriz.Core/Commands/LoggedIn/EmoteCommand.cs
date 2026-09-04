using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class EmoteCommand : Command
{
    public override string Key => "emote";
    public override IReadOnlyList<string> Aliases => [":"];
    public override string Category => "Communication";
    public override string Desc => "Emote something.";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("text").Nargs("*").Help("Text to emote.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject p) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { p.Msg(PrintHelp()); return; }
        var lst = pa.GetList("text");
        var loc = p.ResolveLocationObject();
        if (lst.Count > 0 && loc != null)
        {
            loc.MsgContents($"{p.Name} {string.Join(" ", lst)}", fromObj: p, msgType: "emote");
        }
        else p.Msg(PrintHelp());
    }
}
