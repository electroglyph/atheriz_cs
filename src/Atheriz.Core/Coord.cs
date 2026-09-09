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
        // Failures leave a usable (empty-area) coord, never a null-Area default.
        coord = new Coord(string.Empty, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        // Form "Area(X,Y,Z)" (mirrors ToString()) or "(Area,X,Y,Z)" (search form).
        // The LAST open paren starts the numeric group so area names may
        // themselves contain parens ("My (old) Area(1,2,3)").
        if (s.EndsWith(")", StringComparison.Ordinal))
        {
            int open = s.LastIndexOf('(');
            if (open < 0) return false;
            string tail = s[(open + 1)..^1];
            if (tail.Contains('(') || tail.Contains(')')) return false;
            string head = s[..open].Trim();
            string[] inside = tail.Split(',');
            string area;
            string[] nums;
            if (head.Length == 0)
            {
                // "(Area,X,Y,Z)"
                if (inside.Length != 4) return false;
                area = inside[0].Trim();
                nums = inside[1..];
            }
            else
            {
                // "Area(X,Y,Z)"
                if (inside.Length != 3) return false;
                area = head.Trim();
                nums = inside;
            }
            if (string.IsNullOrEmpty(area)) return false;
            if (!int.TryParse(nums[0].Trim(), out var x)) return false;
            if (!int.TryParse(nums[1].Trim(), out var y)) return false;
            if (!int.TryParse(nums[2].Trim(), out var z)) return false;
            coord = new Coord(area, x, y, z);
            return true;
        }
        // Form "Area X Y Z". A bare area name is NOT a coord (no origin guess).
        string[] parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[1], out var x2)) return false;
        if (!int.TryParse(parts[2], out var y2)) return false;
        if (!int.TryParse(parts[3], out var z2)) return false;
        coord = new Coord(parts[0], x2, y2, z2);
        return true;
    }
}
