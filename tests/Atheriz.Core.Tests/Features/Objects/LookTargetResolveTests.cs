// Pins for TryResolveLookTarget routing through GetNoun + FindLink
// (Node.Links.cs): noun lookup keeps its case folding and link lookup keeps
// its name + alias case-insensitive matching.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class LookTargetResolveTests
{
    [Fact]
    public void TryResolveLookTarget_NounCaseVariants_ResolveNoun()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord("lookcase", 0, 0, 0));
        nh.AddNode(room);
        room.AddNoun("Plaque", "A brass plaque.");
        var p = GameObject.Create("Seer", isPc: true);
        ObjectRegistry.AddObject(p);

        Assert.Equal("A brass plaque.", room.TryResolveLookTarget("plaque", p));
        Assert.Equal("A brass plaque.", room.TryResolveLookTarget("PLAQUE", p));
        Assert.Equal("A brass plaque.", room.TryResolveLookTarget("Plaque", p));
    }

    [Fact]
    public void TryResolveLookTarget_LinkNameAndAliasCaseVariants_ResolveAppearance()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord("linkcase", 0, 0, 0), desc: "Home room.");
        var dest = new Node(new Coord("linkcase", 0, 1, 0), desc: "Far room.");
        nh.AddNode(room);
        nh.AddNode(dest);
        room.AddLink(new NodeLink("north", dest.Coord, ["n"]));
        var p = GameObject.Create("Seer", isPc: true);
        ObjectRegistry.AddObject(p);

        string expected = dest.ReturnAppearance(p);
        Assert.Equal(expected, room.TryResolveLookTarget("north", p));
        Assert.Equal(expected, room.TryResolveLookTarget("NORTH", p));
        Assert.Equal(expected, room.TryResolveLookTarget("North", p));
        Assert.Equal(expected, room.TryResolveLookTarget("n", p));
        Assert.Equal(expected, room.TryResolveLookTarget("N", p));
        Assert.Null(room.TryResolveLookTarget("south", p));
    }
}
