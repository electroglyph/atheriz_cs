using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Raw field writers routed through the shared Read/Write helpers must keep
// the same values, locks, and dirty-flag behavior as the hand-rolled blocks.
[Collection("Ported")]
public class RawWriterHelperTests
{
    [Fact]
    public void Coord_SetThenGet_ReturnsAssignedValue()
    {
        var node = new Node(new Atheriz.Core.Coord("limbo", 0, 0, 0));
        var target = new Atheriz.Core.Coord("arena", 3, 4, 0);
        node.Coord = target;
        Assert.Equal(target, node.Coord);
    }

    [Fact]
    public void Coord_Set_DoesNotMarkModified()
    {
        var node = new Node(new Atheriz.Core.Coord("limbo", 0, 0, 0));
        node.IsModified = false;
        node.Coord = new Atheriz.Core.Coord("arena", 1, 2, 0);
        Assert.False(node.IsModified);
    }

    [Fact]
    public void SetIdRaw_AssignsIdAndMarksModified()
    {
        var o = GameObject.Create("rawid");
        o.IsModified = false;
        o.SetIdRaw(4242);
        Assert.Equal(4242, o.Id);
        Assert.True(o.IsModified);
    }

    [Fact]
    public void SetIsDeletedRaw_SetsFlagAndMarksModified()
    {
        var o = GameObject.Create("rawdel");
        o.IsModified = false;
        o.SetIsDeletedRaw(true);
        Assert.True(o.IsDeleted);
        Assert.True(o.IsModified);
    }
}
