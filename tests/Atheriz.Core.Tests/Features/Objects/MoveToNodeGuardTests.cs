using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// A node moving into itself must be refused: the cycle walk is skipped for
// Node destinations, so node.MoveTo(node) rewrites containment instead of
// returning false (GameObject.Move.cs:192).
[Collection("Ported")]
public class MoveToNodeGuardTests
{
    [Fact]
    public void Node_MoveTo_Self_ReturnsFalse()
    {
        // Correct: self-move is denied and leaves no self-containment behind.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 1, 2, 0));
            Assert.False(node.MoveTo(node));
            Assert.DoesNotContain(node.Id, node.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
