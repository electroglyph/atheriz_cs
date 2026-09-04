using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;

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
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null || pa.GetList("coord").Count == 0) { go.Msg(PrintHelp()); return; }
        var raw = string.Join(" ", pa.GetList("coord")).Trim();
        if (raw.StartsWith("(") && raw.EndsWith(")")) raw = raw[1..^1];
        List<string> parts;
        if (raw.Contains(",")) parts = raw.Split(',').Select(s => s.Trim()).ToList();
        else parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count != 4) { go.Msg("Usage: move <area> <x> <y> <z>  or  move (<area>,<x>,<y>,<z>)"); return; }
        var area = parts[0];
        if (!int.TryParse(parts[1], out var x) || !int.TryParse(parts[2], out var y) || !int.TryParse(parts[3], out var z))
        {
            go.Msg("x, y, and z must be integers.");
            return;
        }
        var coord = new Coord(area, x, y, z);
        var nh = NodeHandler.GetCurrent();
        var node = nh?.GetNode(coord);
        if (node == null) { go.Msg($"No node found at {coord}."); return; }
        go.MoveTo(node, force: true);
        go.Msg($"Moved to {coord}.");
    }
}