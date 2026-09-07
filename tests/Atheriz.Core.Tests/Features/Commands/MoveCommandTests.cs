using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Move must check the MoveTo result before reporting success (MoveCommand.cs:38-39).
[Collection("Ported")]
public class MoveCommandTests
{
    [Fact]
    public void Move_FailedMove_DoesNotReportSuccess()
    {
        // Moving into a deleted node fails, so no success message may appear.
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var startCoord = new Coord("limbo", 0, 0, 0);
            var destCoord = new Coord("limbo", 1, 0, 0);
            var startNode = new Node(startCoord);
            var destNode = new Node(destCoord);
            nh.AddNode(startNode);
            nh.AddNode(destNode);
            var builder = GameObject.Create("bob", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            Assert.True(builder.MoveTo(startNode));
            builder.ClearMessages();
            destNode.IsDeleted = true;
            var job = CommandDispatcher.DispatchLoggedIn(builder, "move limbo 1 0 0", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var msgs = string.Join("\n", builder.PeekMessages());
            Assert.DoesNotContain("Moved to", msgs);
            var loc = builder.ResolveLocationObject() as Node;
            Assert.NotNull(loc);
            Assert.Equal(startCoord, loc!.Coord);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ObjectRegistry.ClearAll();
        }
    }
}
