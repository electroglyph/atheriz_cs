namespace Atheriz.Core.Utils;

// A* open-list entry: immutable position/parent/scores, ordered by F.
// A record (value semantics, `with` support) replaces the old mutable
// G/H/F class whose Equals (Coord-only) disagreed with CompareTo (F):
// identity in the open set is positional (openByPos keyed by Coord),
// ordering is by F priority, and the two never shared one member again.
internal sealed record PathNode(PathNode? Parent, Node Position, int G = 0, int H = 0) : IComparable<PathNode>
{
    public int F => G + H;

    // Two-arg shape kept: test-only reflection constructs PathNode(parent,
    // node) positionally (Activator does not fill optional parameters).
    public PathNode(PathNode? parent, Node position)
        : this(parent, position, 0, 0)
    {
    }

    // the open queue orders
    // nodes through this comparer, exactly like heapq.
    public int CompareTo(PathNode? other)
    {
        if (other is null) return 1;
        return F.CompareTo(other.F);
    }
}
