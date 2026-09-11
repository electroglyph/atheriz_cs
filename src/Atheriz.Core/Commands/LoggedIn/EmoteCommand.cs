
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class EmoteCommand : Command
{
    public override string Key => "emote";
    public override IReadOnlyList<string> Aliases => [":"];
    public override string Category => "Communication";
    public override string Desc => "Emote something.";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("text").Nargs("REMAINDER").Help("Text to emote.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var p)) return;
        if (!this.RequireParsedArgs(caller, args, out var pa)) return;
        var lst = pa.GetList("text");
        if (lst.Count > 0 && p.ResolveLocationObject() is not null)
        {
            string text = $"{p.Name} {string.Join(" ", lst)}";
            // Same AtSay entry as say so game-code hooks observe emotes; the
            // literal text keeps the established wording for actor and room.
            p.AtSayFull(text, msgSelf: text, msgLocation: text, msgType: "emote",
                mapping: new Dictionary<string, object?> { ["you"] = p });
        }
        else p.Msg(PrintHelp());
    }
}
