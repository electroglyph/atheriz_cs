// Pin for the search-driven give split (the single-scan rewrite was dropped
// as unsound): every candidate split is validated by real searches, so
// multi-word item and target names resolve to the right pair.
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class GiveMultiWordResolutionTests
{
    [Fact]
    public void Give_MultiWordItem_ToMultiWordTarget_ResolvesBoth()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("givemw", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var giver = GameObject.Create("giver", isPc: true);
        var receiver = GameObject.Create("Bob Green", isPc: true);
        ObjectRegistry.AddObject(giver);
        ObjectRegistry.AddObject(receiver);
        var loc = new LocationRef.CoordLocation(node.Coord);
        giver.Location = loc;
        receiver.Location = loc;
        node.AddObject(giver);
        node.AddObject(receiver);
        var item = GameObject.Create("red ball", isItem: true);
        ObjectRegistry.AddObject(item);
        Assert.True(item.MoveTo(giver));
        giver.ClearMessages();
        receiver.ClearMessages();
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(["red", "ball", "Bob", "Green"]));
        Assert.Contains(item.Id, receiver.ContentsSnapshot);
        Assert.DoesNotContain(item.Id, giver.ContentsSnapshot);
        Assert.Contains(giver.PeekMessages(), m => m.Contains("You give red ball to Bob Green."));
    }
}
