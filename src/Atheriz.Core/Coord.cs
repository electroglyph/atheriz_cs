namespace Atheriz.Core;

/// <summary>
/// 3D coordinate + area, mirrors <c>atheriz/coord.py:Coord</c> NamedTuple(area,x,y,z).
/// Immutable value type.
/// </summary>
public readonly record struct Coord(string Area, int X, int Y, int Z)
{
    public override string ToString() => $"{Area}({X},{Y},{Z})";

    public static bool TryParse(string? s, out Coord coord)
    {
        coord = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        // Very lenient: "limbo" or "limbo(4,4,4)" or "limbo 4 4 4"
        return false;
    }
}
