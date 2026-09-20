using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
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

    [Fact]
    public void Move_MultiWordSpaceForm_ShowsUsage()
    {
        // The space-separated form takes exactly four tokens; a quoted
        // multi-word area arrives as 5+ tokens and is refused with Usage.
        // Multi-word areas use the comma form: move (my area,1,2,3).
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var c = GameObject.Create("f7b", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(c);
            var before = c.Location;
            var pa = new GameArgumentParser.ParsedArgs();
            pa["coord"] = new List<string> { "my area", "1", "2", "3" };
            c.ClearMessages();
            new MoveCommand().Run(c, pa);
            var msgs = string.Join(" ", c.PeekMessages());
            Assert.Contains("Usage:", msgs);
            Assert.DoesNotContain("Moved to", msgs);
            Assert.Same(before, c.Location);
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ObjectRegistry.ClearAll();
        }
    }
}
