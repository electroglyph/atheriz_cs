using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Deduped node hydration with centralized null backfill: the subtype branch
// keeps its coord-string Name (assignName is load-bearing) while the plain
// branch assigns nd.Name, and null collections backfill instead of throwing.
[Collection("Ported")]
public class NodeHydrationTests
{
    private sealed class HydrationProbeNode : Node
    {
        public HydrationProbeNode(Coord coord) : base(coord) { }
    }

    static NodeHydrationTests()
    {
        Node.RegisterPersistedSubtype("SimplifyHydrationProbe", typeof(HydrationProbeNode), c => new HydrationProbeNode(c));
    }

    private static NodeAreaDto MakeArea(NodeDto plain, NodeDto sub)
    {
        return new NodeAreaDto
        {
            Name = "hydration",
            Grids =
            {
                [0] = new NodeGridDto
                {
                    Area = "hydration",
                    Z = 0,
                    Nodes = { ["0,0"] = plain, ["1,0"] = sub },
                },
            },
        };
    }

    [Fact]
    public void ToDomain_PlainAssignsName_SubtypeKeepsCoordName()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = new NodeDto { Coord = new Coord("hydration", 0, 0, 0), Name = "PlainName", Id = 9101 };
        var sub = new NodeDto { Coord = new Coord("hydration", 1, 0, 0), Name = "SubName", Id = 9102, ObjectType = "SimplifyHydrationProbe" };
        var area = MakeArea(plain, sub).ToDomain();
        var grid = area.Grids[0];
        Assert.Equal("PlainName", grid.Nodes[(0, 0)].Name);
        var subNode = Assert.IsType<HydrationProbeNode>(grid.Nodes[(1, 0)]);
        Assert.NotEqual("SubName", subNode.Name);
    }

    [Fact]
    public void ToDomain_NullCollections_BackfillToEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = new NodeDto { Coord = new Coord("hydration", 0, 0, 0), Name = "Nully", Id = 9201 };
        plain.Links = null!;
        plain.Nouns = null!;
        plain.Scripts = null!;
        var sub = new NodeDto { Coord = new Coord("hydration", 1, 0, 0), Name = "Sub", Id = 9202 };
        var area = MakeArea(plain, sub).ToDomain();
        var node = area.Grids[0].Nodes[(0, 0)];
        Assert.NotNull(node.Links);
        Assert.Empty(node.Links);
        Assert.NotNull(node.Nouns);
        Assert.Empty(node.Nouns);
    }
}
