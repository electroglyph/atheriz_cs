
namespace Atheriz.Core.Utils;

internal sealed class PathNode : IComparable<PathNode>
{
    public PathNode? Parent { get; }
    public Node Position { get; }
    public int G { get; set; }
    public int H { get; set; }
    public int F { get; set; }
    public PathNode(PathNode? parent, Node position)
    {
        Parent = parent;
        Position = position;
        G = 0; H = 0; F = 0;
    }
    public override bool Equals(object? obj) => obj is PathNode o && Position.Coord.Equals(o.Position.Coord);
    public override int GetHashCode() => Position.Coord.GetHashCode();
// the open queue orders
    // nodes through this comparer, exactly like heapq.
    public int CompareTo(PathNode? other)
    {
        if (other is null) return 1;
        return F.CompareTo(other.F);
    }
}
