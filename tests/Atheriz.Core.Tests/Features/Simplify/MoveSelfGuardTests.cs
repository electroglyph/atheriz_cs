using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Move guards: self-moves are refused in both identity forms (the earlier
// checks subsume the later repeat), and the non-node-to-node announce path
// delivers with a null reverse link instead of throwing.
[Collection("Ported")]
public class MoveSelfGuardTests
{
    [Fact]
    public void MoveTo_Self_IsRefused()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("one");
            ObjectRegistry.AddObject(obj);

            Assert.False(obj.MoveTo(obj));

            var sameId = GameObject.Create("other");
            sameId.Id = obj.Id;
            Assert.False(obj.MoveTo(sameId));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_ContainerToNode_AnnouncesWithoutReverseLink()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var node = new Node(new Coord("limbo", 0, 0, 0));
            ObjectRegistry.AddObject(node);
            var mover = GameObject.Create("mover");
            ObjectRegistry.AddObject(mover);
            var receiver = GameObject.Create("recv");
            ObjectRegistry.AddObject(receiver);
            mover.MoveTo(room, force: true, announce: false);
            receiver.MoveTo(node, force: true, announce: false);

            Assert.True(mover.MoveTo(node, force: true, announce: true));

            Assert.NotEmpty(receiver.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
