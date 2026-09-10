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
        // Span parse: no Trim copy, no Split arrays, no range-slice allocs.
        // Accepted inputs and failure contract (false, never throw) match the
        // split-based parse this replaced — span int.TryParse accepts the same
        // numeric shapes (leading +/-/spaces already trimmable).
        ReadOnlySpan<char> span = s.AsSpan().Trim();
        // Form "Area(X,Y,Z)" (mirrors ToString()) or "(Area,X,Y,Z)" (search form).
        // The LAST open paren starts the numeric group so area names may
        // themselves contain parens ("My (old) Area(1,2,3)").
        if (span.Length > 0 && span[^1] == ')')
        {
            int open = span.LastIndexOf('(');
            if (open < 0) return false;
            ReadOnlySpan<char> tail = span[(open + 1)..^1];
            if (tail.Contains('(') || tail.Contains(')')) return false;
            ReadOnlySpan<char> head = span[..open].Trim();
            // Manual comma split (no Split array): exactly 3 numeric segments
            // for Area(X,Y,Z), exactly 4 (area + 3) for (Area,X,Y,Z).
            Span<Range> segs = stackalloc Range[4];
            int count = 0;
            int start = 0;
            for (int i = 0; i <= tail.Length; i++)
            {
                if (i == tail.Length || tail[i] == ',')
                {
                    if (count >= 4) return false;
                    segs[count++] = new Range(start, i);
                    start = i + 1;
                }
            }
            string area;
            int numBase;
            if (head.Length == 0)
            {
                // "(Area,X,Y,Z)"
                if (count != 4) return false;
                area = tail[segs[0]].Trim().ToString();
                numBase = 1;
            }
            else
            {
                // "Area(X,Y,Z)"
                if (count != 3) return false;
                area = head.ToString();
                numBase = 0;
            }
            if (area.Length == 0) return false;
            if (!int.TryParse(tail[segs[numBase]], out var x)) return false;
            if (!int.TryParse(tail[segs[numBase + 1]], out var y)) return false;
            if (!int.TryParse(tail[segs[numBase + 2]], out var z)) return false;
            coord = new Coord(area, x, y, z);
            return true;
        }
        // Form "Area X Y Z" (RemoveEmptyEntries-equivalent manual skipping).
        // A bare area name is NOT a coord (no origin guess).
        Span<Range> words = stackalloc Range[4];
        int w = 0;
        int j = 0;
        while (j < span.Length)
        {
            while (j < span.Length && char.IsWhiteSpace(span[j])) j++;
            if (j >= span.Length) break;
            int wordStart = j;
            while (j < span.Length && !char.IsWhiteSpace(span[j])) j++;
            if (w >= 4) return false;
            words[w++] = new Range(wordStart, j);
        }
        if (w != 4) return false;
        if (!int.TryParse(span[words[1]], out var x2)) return false;
        if (!int.TryParse(span[words[2]], out var y2)) return false;
        if (!int.TryParse(span[words[3]], out var z2)) return false;
        coord = new Coord(span[words[0]].ToString(), x2, y2, z2);
        return true;
    }
}
