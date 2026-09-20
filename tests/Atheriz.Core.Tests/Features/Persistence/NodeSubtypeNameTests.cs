// A custom node subtype must keep its persisted Name across save/load:
// the subtype factory only rebuilds type+coord.
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Persistence;

[Collection("Ported")]
public sealed class NodeSubtypeNameTests
{
    // Custom subtype with a genuinely settable Name (plain Node.Name is
    // coord-derived with a no-op setter, so only a real override can
    // observe the load-half Name assignment).
    private sealed class NamedProbeNode : Node
    {
        public NamedProbeNode(Coord c) : base(c) { }
        public override string Name { get; set; } = "room";
    }

    [Fact]
    public void NodeSubtype_RoundTrip_PreservesCustomName()
    {
        using var env = GlobalTestEnv.Enter();
        Node.RegisterPersistedSubtype("SubtypeNameProbe", typeof(NamedProbeNode), c => new NamedProbeNode(c));
        var dto = new NodeAreaDto
        {
            Name = "f11area",
            Grids = new()
            {
                [0] = new NodeGridDto
                {
                    Area = "f11area",
                    Z = 0,
                    Nodes = new()
                    {
                        ["0,0"] = new NodeDto
                        {
                            Coord = new Coord("f11area", 0, 0, 0),
                            Name = "Renamed Chamber",
                            ObjectType = "SubtypeNameProbe",
                            Id = 424242,
                        },
                    },
                },
            },
        };
        var domain = dto.ToDomain();
        var node = domain.Grids[0].Nodes[(0, 0)];
        Assert.IsType<NamedProbeNode>(node);
        Assert.Equal("Renamed Chamber", node.Name);
    }
}
