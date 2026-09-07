using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// IComparable contract: any instance compares greater than null, so nulls sort
// last and ordered structures stay consistent (Pathfind.cs:29-37 inverts this).
// Touches the global object registry via `new Node`, so serialized with "Ported".
[Collection("Ported")]
public sealed class PathNodeTests
{
    [Fact]
    public void PathNode_CompareTo_Null_ReturnsPositive()
    {
        // PathNode is internal; reach it via reflection (test-only seam).
        // The separate open-queue key gap (AStar enqueues by F only at
        // Pathfind.cs:124,147,196,203, so the H/seq tiebreaks in CompareTo
        // never fire) is noted here but not pinned: heap pop order for equal
        // keys is an implementation detail, not a contract to freeze.
        ObjectRegistry.ClearAll();
        try
        {
            var pathNodeType = typeof(Pathfind).Assembly.GetType("Atheriz.Core.Utils.PathNode");
            Assert.NotNull(pathNodeType);
            var node = new Node(new Coord("PathNodeNullArea", 0, 0, 0));
            var instance = Activator.CreateInstance(pathNodeType!, new object?[] { null, node });
            Assert.NotNull(instance);
            var compareTo = pathNodeType!.GetMethod("CompareTo");
            Assert.NotNull(compareTo);
            var result = (int)compareTo!.Invoke(instance, new object?[] { null })!;
            Assert.True(result > 0, $"CompareTo(null) must return positive (any instance sorts after null); was {result}.");
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
