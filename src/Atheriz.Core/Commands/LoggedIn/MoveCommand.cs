
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class MoveCommand : Command
{
    public override string Key => "move";
    public override string Desc => "Move to a coordinate.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Coord).Nargs("+").Help("Coordinate: area x y z  or  (area,x,y,z)");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null || pa.GetList(ParsedArgKeys.Coord).Count == 0) { go.Msg(PrintHelp()); return; }
        var raw = string.Join(" ", pa.GetList(ParsedArgKeys.Coord)).Trim();
        if (raw.StartsWith("(", StringComparison.Ordinal) && raw.EndsWith(")", StringComparison.Ordinal)) raw = raw[1..^1];
        List<string> parts;
        bool commaForm = raw.Contains(",");
        if (commaForm) parts = raw.Split(',').Select(s => s.Trim()).ToList();
        else parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        // Both forms take exactly four fields: the space form splits on
        // whitespace (area x y z), the comma form on commas. Multi-word
        // areas use the comma form: move (my area,1,2,3).
        if (parts.Count != 4) { go.Msg("Usage: move <area> <x> <y> <z>  or  move (<area>,<x>,<y>,<z>)"); return; }
        string area = parts[0], xs = parts[1], ys = parts[2], zs = parts[3];
        if (!int.TryParse(xs, out var x) || !int.TryParse(ys, out var y) || !int.TryParse(zs, out var z))
        {
            go.Msg("x, y, and z must be integers.");
            return;
        }
        var coord = new Coord(area, x, y, z);
        var nh = NodeHandler.GetCurrent();
        var node = nh?.GetNode(coord);
        if (node is null) { go.Msg($"No node found at {coord}."); return; }
        if (go.MoveTo(node, force: true)) go.Msg($"Moved to {coord}.");
        else go.Msg($"Could not move to {coord}.");
    }
}