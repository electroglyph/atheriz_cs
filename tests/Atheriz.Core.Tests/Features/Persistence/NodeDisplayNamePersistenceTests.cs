// DisplayName must survive every persistence mapping it flows through:
// NodeDto.Migrate backfill, NodeAreaDto.HydrateNode, NodeHandler.Save/Load,
// and the shared GameObjectDto BuildDto/FromDto path.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Persistence;

[Collection("Ported")]
public sealed class NodeDisplayNamePersistenceTests
{
    [Fact]
    public void Migrate_NullDisplayName_BackfillsEmpty()
    {
        var dto = new NodeDto { Coord = new Coord("migarea", 0, 0, 0), DisplayName = null! };
        dto.Migrate();
        Assert.Equal("", dto.DisplayName);
    }

    [Fact]
    public void HydrateNode_RestoresDisplayName()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = new NodeAreaDto
        {
            Name = "dnarea",
            Grids = new()
            {
                [0] = new NodeGridDto
                {
                    Area = "dnarea",
                    Z = 0,
                    Nodes = new()
                    {
                        ["1,2"] = new NodeDto
                        {
                            Coord = new Coord("dnarea", 1, 2, 0),
                            DisplayName = "Custom Chamber",
                            Id = 424243,
                        },
                    },
                },
            },
        };
        var node = dto.ToDomain().Grids[0].Nodes[(1, 2)];
        Assert.Equal("Custom Chamber", node.DisplayName);
        // The coord-derived Name is untouched: DisplayName rides its own field.
        Assert.Equal(node.Coord.ToString(), node.Name);
    }

    [Fact]
    public void SaveLoad_Roundtrip_PreservesDisplayName()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var n = new Node(new Coord("dnround", 1, 2, 0)) { DisplayName = "Round Chamber", Desc = "Kept." };
            if (ObjectRegistry.Get(n.Id).Count == 0) ObjectRegistry.AddObject(n);
            nh.AddNode(n);
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); nh.Save(db, force: true); }
            var fresh = new NodeHandler(autoLoad: false);
            using (var db = new AtherizDbContext(env.TempPath)) { fresh.Load(db); }
            var back = fresh.GetNode(new Coord("dnround", 1, 2, 0));
            Assert.NotNull(back);
            Assert.Equal("Round Chamber", back.DisplayName);
            Assert.Equal("Kept.", back.Desc);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GameObjectDto_RoundTrip_PreservesDisplayName()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("dnconv", 3, 4, 0)) { DisplayName = "Converted Chamber" };
        if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
        var dto = GameObjectDtoConverter.BuildDto(node);
        Assert.Equal("Converted Chamber", dto.DisplayName);
        var back = Assert.IsType<Node>(GameObjectDtoConverter.FromDto(dto));
        Assert.Equal("Converted Chamber", back.DisplayName);
    }

    [Fact]
    public void GameObjectDto_RoundTrip_PreservesPlainObjectDisplayName()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("dnplain");
        obj.DisplayName = "Plain Show";
        if (ObjectRegistry.Get(obj.Id).Count == 0) ObjectRegistry.AddObject(obj);
        var dto = GameObjectDtoConverter.BuildDto(obj);
        Assert.Equal("Plain Show", dto.DisplayName);
        var back = GameObjectDtoConverter.FromDto(dto);
        Assert.Equal("Plain Show", back.DisplayName);
    }
}
