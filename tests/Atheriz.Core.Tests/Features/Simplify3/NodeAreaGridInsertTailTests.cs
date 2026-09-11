// Pins for the unified grid-insert tail in NodeAreaDto.ToDomain: the subtype
// and plain branches share one insert keyed by the DTO coord (never the raw
// mapping key), hydrate the same fields, keep the subtype concrete type, and
// leave no registry phantom behind; unknown ObjectType falls back to plain.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class NodeAreaGridInsertTailTests
{
    private sealed class B22TailProbeNode : Node
    {
        public B22TailProbeNode(Coord coord) : base(coord) { }
    }

    static NodeAreaGridInsertTailTests()
    {
        Node.RegisterPersistedSubtype("B22GridTailProbe", typeof(B22TailProbeNode), c => new B22TailProbeNode(c));
    }

    private static NodeAreaDto MakeArea(params (string key, NodeDto dto)[] nodes)
    {
        var grid = new NodeGridDto { Area = "b22tail", Z = 0 };
        foreach (var (key, dto) in nodes)
            grid.Nodes[key] = dto;
        return new NodeAreaDto { Name = "b22tail", Grids = { [0] = grid } };
    }

    [Fact]
    public void ToDomain_SubtypeAndPlain_InsertAtCoordDerivedKeys()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = new NodeDto { Coord = new Coord("b22tail", 5, 6, 0), Name = "Plain", Id = 6101 };
        var sub = new NodeDto { Coord = new Coord("b22tail", 7, 8, 0), Name = "Sub", Id = 6102, ObjectType = "B22GridTailProbe" };
        var grid = MakeArea(("wrong-key-a", plain), ("wrong-key-b", sub)).ToDomain().Grids[0];
        Assert.Equal(2, grid.Nodes.Count);
        Assert.True(grid.Nodes.ContainsKey((5, 6)));
        Assert.True(grid.Nodes.ContainsKey((7, 8)));
        Assert.Equal(new Coord("b22tail", 5, 6, 0), grid.Nodes[(5, 6)].Coord);
        Assert.Equal(new Coord("b22tail", 7, 8, 0), grid.Nodes[(7, 8)].Coord);
        Assert.Equal(typeof(Node), grid.Nodes[(5, 6)].GetType());
        Assert.IsType<B22TailProbeNode>(grid.Nodes[(7, 8)]);
        Assert.Empty(ObjectRegistry.Get(6101));
        Assert.Empty(ObjectRegistry.Get(6102));
    }

    [Fact]
    public void ToDomain_BothBranches_HydrateFieldsAndIds()
    {
        using var env = GlobalTestEnv.Enter();
        var plain = new NodeDto
        {
            Coord = new Coord("b22tail", 1, 2, 0), Name = "Plain", Desc = "plain-desc",
            Theme = "plain-theme", Symbol = "plain-sym", Id = 6201,
        };
        var sub = new NodeDto
        {
            Coord = new Coord("b22tail", 3, 4, 0), Name = "Sub", Desc = "sub-desc",
            Theme = "sub-theme", Symbol = "sub-sym", Id = 6202, ObjectType = "B22GridTailProbe",
        };
        var grid = MakeArea(("p", plain), ("s", sub)).ToDomain().Grids[0];
        var plainNode = grid.Nodes[(1, 2)];
        Assert.Equal("plain-desc", plainNode.Desc);
        Assert.Equal("plain-theme", plainNode.Theme);
        Assert.Equal("plain-sym", plainNode.Symbol);
        Assert.Equal(6201, plainNode.Id);
        var subNode = Assert.IsType<B22TailProbeNode>(grid.Nodes[(3, 4)]);
        Assert.Equal("sub-desc", subNode.Desc);
        Assert.Equal("sub-theme", subNode.Theme);
        Assert.Equal("sub-sym", subNode.Symbol);
        Assert.Equal(6202, subNode.Id);
        Assert.Equal("b22tail(3,4,0)", subNode.Name);
        Assert.NotEqual("Sub", subNode.Name);
    }

    [Fact]
    public void ToDomain_UnknownObjectType_FallsBackToPlainNode()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = new NodeDto
        {
            Coord = new Coord("b22tail", 9, 9, 0), Name = "Mystery",
            Desc = "mystery-desc", Id = 6301, ObjectType = "No.Such.Type, Whatever",
        };
        var grid = MakeArea(("m", dto)).ToDomain().Grids[0];
        var node = grid.Nodes[(9, 9)];
        Assert.Equal(typeof(Node), node.GetType());
        Assert.Equal(new Coord("b22tail", 9, 9, 0), node.Coord);
        Assert.Equal("mystery-desc", node.Desc);
        Assert.Equal(6301, node.Id);
    }
}
