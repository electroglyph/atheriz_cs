using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class InventoryCommand : Command
{
    public override string Key => "inventory";
    public override IReadOnlyList<string> Aliases => ["i"];
    public override string Desc => "View your inventory.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject p) return;
        var contents = Globals.ObjectRegistry.Get(p.ContentsSnapshot.ToList());
        if (contents.Count == 0) p.Msg("You are carrying nothing.");
        else
        {
            var names = ContentUtils.GroupByName(contents, p);
            p.Msg($"You are carrying: {names}");
        }
    }
}
