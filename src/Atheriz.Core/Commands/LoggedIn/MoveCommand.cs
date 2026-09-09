
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class MoveCommand : Command
{
    public override string Key => "move";
    public override string Desc => "Move to a coordinate.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("coord").Nargs("+").Help("Coordinate: area x y z  or  (area,x,y,z)");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null || pa.GetList("coord").Count == 0) { go.Msg(PrintHelp()); return; }
        var raw = string.Join(" ", pa.GetList("coord")).Trim();
        if (raw.StartsWith("(", StringComparison.Ordinal) && raw.EndsWith(")", StringComparison.Ordinal)) raw = raw[1..^1];
        List<string> parts;
        if (raw.Contains(",")) parts = raw.Split(',').Select(s => s.Trim()).ToList();
        else parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts is not [var area, var xs, var ys, var zs]) { go.Msg("Usage: move <area> <x> <y> <z>  or  move (<area>,<x>,<y>,<z>)"); return; }
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