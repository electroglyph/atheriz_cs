
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DropCommand : Command
{
    public override string Key => "drop";
    public override string Desc => "Drop an object.";
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument("object", nargs: "REMAINDER", help: "object to drop or all"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        // Extract drop name robustly
        string? dropName = null;
        if (pa is not null)
        {
        // The parser defines a single positional dest ("object"); live input
        // never carries other keys, so no fallback dests are read here.
        var lst = pa.GetList("object");
        if (lst.Count > 0) dropName = string.Join(" ", lst).Trim();
        }
        if (string.IsNullOrWhiteSpace(dropName)) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject();
        if (loc is null) { go.Msg("You can't drop something here!"); return; }
        // Intentional: Drop checks the "put" lock, not "drop" — verbatim atheriz/commands/loggedin/drop.py:26.
        if (!loc.Access(go, "put")) { go.Msg("You can't drop something here!"); return; }
        dropName = dropName!.Trim();
        List<GameObject> excludeSelf = [go];
        if (dropName == "all")
        {
            var contents = ObjectRegistry.Get(go.ContentsSnapshot);
            foreach (var obj in contents)
            {
                if (!obj.AtPreDrop(go)) continue;
                if (!obj.MoveTo(loc)) { go.Msg($"You can't drop {obj.Name}."); continue; }
                ContentUtils.EmitToLocation(loc, $"{go.Name} dropped {obj.Name}.", fromObj: go, exclude: excludeSelf);
                go.Msg($"You dropped: {obj.Name}");
                obj.AtDrop(go);
            }
            return;
        }
        var found = go.Search(dropName, true, go);
        if (found.Count == 0) { CommandHelpers.MsgObjectNotFound(go); return; }
        foreach (var f in found)
        {
            if (!f.AtPreDrop(go)) continue;
            if (!f.MoveTo(loc)) { go.Msg($"You can't drop {f.Name}."); continue; }
            ContentUtils.EmitToLocation(loc, $"{go.Name} dropped {f.Name}.", fromObj: go, exclude: excludeSelf);
            go.Msg($"You dropped: {f.Name}");
            f.AtDrop(go);
        }
    }
}
