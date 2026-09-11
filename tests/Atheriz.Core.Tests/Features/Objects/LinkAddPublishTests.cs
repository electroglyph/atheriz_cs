// Pins for the shared AddLink insert core + publish tail (Node.Links.cs):
// duplicate inserts refuse, case-folded AddLinkIfAbsent skips the factory,
// cross-area links publish a transition, and occupants gain exit commands.
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class LinkAddPublishTests
{
    [Fact]
    public void AddLink_DuplicateInsert_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkdup", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var dest = new Coord("linkdup", 0, 1, 0);

        node.AddLink(new NodeLink("north", dest));
        node.AddLink(new NodeLink("north", dest));
        Assert.Single(node.GetLinks());

        node.AddLink(new NodeLink("south", dest));
        Assert.Equal(2, node.GetLinks().Count);
    }

    [Fact]
    public void AddLinkIfAbsent_CaseFoldedName_RefusedAndFactorySkipped()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkabsent", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        int calls = 0;

        Assert.True(node.AddLinkIfAbsent("north", () =>
        {
            calls++;
            return new NodeLink("north", new Coord("linkabsent", 0, 1, 0), ["n"]);
        }));
        Assert.Equal(1, calls);
        Assert.False(node.AddLinkIfAbsent("NORTH", () =>
        {
            calls++;
            return new NodeLink("NORTH", new Coord("linkabsent", 0, -1, 0));
        }));
        Assert.Equal(1, calls);
        Assert.Single(node.GetLinks());
    }

    [Fact]
    public void AddLink_CrossArea_PublishesTransition()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("linka", 0, 0, 0));
        nh.AddNode(node);

        node.AddLink(new NodeLink("east", new Coord("linka", 1, 0, 0)));
        Assert.Empty(nh.FindTransitions(fromArea: "linka"));

        var far = new Coord("linkb", 5, 5, 0);
        node.AddLink(new NodeLink("gate", far));
        var edges = nh.FindTransitions(fromArea: "linka");
        Assert.Contains(edges, t => t.ToCoord.Equals(far) && t.FromCoord.Equals(node.Coord));
    }

    [Fact]
    public void AddLink_Occupant_InstallsExitCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkocc", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var occupant = GameObject.Create("bob");
        ObjectRegistry.AddObject(occupant);
        node.AddObject(occupant);
        Assert.DoesNotContain(occupant.InternalCmdSet!.GetAll(), c => c.Key == "north");

        node.AddLink(new NodeLink("north", new Coord("linkocc", 0, 1, 0)));

        var exits = occupant.InternalCmdSet!.GetAll().Where(c => c.Key == "north").ToList();
        Assert.Single(exits);
        Assert.Equal("exits", exits[0].Tag);
    }

    [Fact]
    public void AddLinkIfAbsent_Occupant_InstallsExitCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkocc2", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var occupant = GameObject.Create("bob");
        ObjectRegistry.AddObject(occupant);
        node.AddObject(occupant);

        Assert.True(node.AddLinkIfAbsent("north", () => new NodeLink("north", new Coord("linkocc2", 0, 1, 0))));

        Assert.Contains(occupant.InternalCmdSet!.GetAll(), c => c.Key == "north" && c.Tag == "exits");
    }
}
