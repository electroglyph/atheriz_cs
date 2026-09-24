using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// The move options record carries the parsed destination into the move
// itself without re-parsing text.
[Collection("Ported")]
public sealed class MoveOptionsTests
{
    [Fact]
    public void MoveOptions_CarriesParsedCoord()
    {
        // The options record carries the parse result into the move itself:
        // RunMove resolves and moves without re-parsing text.
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var startCoord = new Coord("limbo", 0, 0, 0);
            var destCoord = new Coord("limbo", 2, 1, 0);
            var startNode = new Node(startCoord);
            nh.AddNode(startNode);
            nh.AddNode(new Node(destCoord));
            var builder = GameObject.Create("gap1mover", privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            Assert.True(builder.MoveTo(startNode));
            builder.ClearMessages();

            var opts = new MoveOptions(destCoord);
            Assert.Equal(destCoord, opts.Dest);
            new MoveCommand().RunMove(builder, opts, CancellationToken.None);

            var loc = builder.ResolveLocationObject() as Node;
            Assert.NotNull(loc);
            Assert.Equal(destCoord, loc!.Coord);
            Assert.Contains($"Moved to {destCoord}.", string.Join("\n", builder.PeekMessages()));
        }
        finally
        {
            NodeHandler.SetCurrent(null);
            ObjectRegistry.ClearAll();
        }
    }
}
