using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>
/// Mirrors <c>atheriz/commands/loggedin/say.py</c> (27 LOC).
/// </summary>
public sealed class SayCommand : Command
{
    public override string Key => "say";
    public override IReadOnlyList<string> Aliases => ["'"];
    public override string Desc => "Say something.";
    public override string Category => "Communication";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("text").Nargs("*").Help("Text to say.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject puppet) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { puppet.Msg(PrintHelp()); return; }
        var lst = pa.GetList("text");
        if (lst.Count > 0)
        {
            puppet.AtSay(string.Join(" ", lst), msgSelf: true);
        }
        else puppet.Msg(PrintHelp());
    }
}
