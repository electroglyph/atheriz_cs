// Pins for the §1 value-site sweep (ObjectRegistry.GetSingle): move-to-container
// resolution, the contents-cycle guard walk, node-delete home moves, and the
// single-pass content filter keep their established outcomes.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class MoveToSingleFetchTests
{
    [Fact]
    public void MoveTo_ObjectLocationContainer_MovesIntoContainer()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        var coin = GameObject.Create("coin", isItem: true);
        ObjectRegistry.AddObject(coin);

        Assert.True(coin.MoveTo(new LocationRef.ObjectLocation(bag.Id)));
        Assert.Contains(coin.Id, bag.ContentsSnapshot);
        Assert.IsType<LocationRef.ObjectLocation>(coin.Location);
    }

    [Fact]
    public void MoveTo_BogusObjectLocation_DetachesToNull()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        var coin = GameObject.Create("coin", isItem: true);
        ObjectRegistry.AddObject(coin);
        coin.MoveTo(bag);

        // An unresolvable destination reference resolves to null, which the
        // established detach path treats as "remove from location".
        Assert.True(coin.MoveTo(new LocationRef.ObjectLocation(-999)));
        Assert.IsType<LocationRef.NullLocation>(coin.Location);
        Assert.DoesNotContain(coin.Id, bag.ContentsSnapshot);
    }

    [Fact]
    public void MoveTo_StaleMemberCycle_ReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        var chest = GameObject.Create("chest", isContainer: true);
        ObjectRegistry.AddObject(chest);
        var pouch = GameObject.Create("pouch", isContainer: true);
        ObjectRegistry.AddObject(pouch);
        chest.AddObject(pouch);
        // Dangling chain with stale membership: the upward walk from the pouch
        // reaches no node, so the contents guard must still refuse the cycle.
        pouch.Location = LocationRef.NullLocation.Instance;

        Assert.False(chest.MoveTo(pouch));
        Assert.IsType<LocationRef.NullLocation>(chest.Location);
    }

    [Fact]
    public void NodeDelete_ObjectLocationHome_MovesChildHome()
    {
        using var env = GlobalTestEnv.Enter();
        var nodeA = new Node(new Coord("limbo", 0, 0, 0));
        ObjectRegistry.AddObject(nodeA);
        var admin = GameObject.Create("admin", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        var box = GameObject.Create("box", isContainer: true);
        ObjectRegistry.AddObject(box);
        var kid = GameObject.Create("kid");
        ObjectRegistry.AddObject(kid);
        kid.Home = new LocationRef.ObjectLocation(box.Id);
        nodeA.AddObject(kid);

        Assert.NotNull(nodeA.Delete(admin, recursive: false));
        Assert.Contains(kid.Id, box.ContentsSnapshot);
        Assert.DoesNotContain(kid.Id, nodeA.ContentsSnapshot);
    }

    [Fact]
    public void NodeDelete_MissingObjectHome_FallsBackToCallerLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var nodeA = new Node(new Coord("limbo", 0, 0, 0));
        ObjectRegistry.AddObject(nodeA);
        var nodeB = new Node(new Coord("limbo", 5, 5, 0));
        ObjectRegistry.AddObject(nodeB);
        var admin = GameObject.Create("admin", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        nodeB.AddObject(admin);
        var kid = GameObject.Create("kid");
        ObjectRegistry.AddObject(kid);
        kid.Home = new LocationRef.ObjectLocation(-999);
        nodeA.AddObject(kid);

        Assert.NotNull(nodeA.Delete(admin, recursive: false));
        Assert.Contains(kid.Id, nodeB.ContentsSnapshot);
    }

    [Fact]
    public void FilterContents_PredicateAppliesToResolvedContents()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        var sword = GameObject.Create("sword", isItem: true);
        ObjectRegistry.AddObject(sword);
        bag.AddObject(sword);
        var shield = GameObject.Create("shield", isItem: true);
        ObjectRegistry.AddObject(shield);
        bag.AddObject(shield);

        var got = ContentUtils.FilterContents(bag, o => o.Name == "sword");
        Assert.Single(got);
        Assert.Same(sword, got[0]);
        Assert.Empty(ContentUtils.FilterContents(bag, o => o.Name == "nope"));
    }

    [Fact]
    public void FilterContents_SkipsUnregisteredIds()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        var rock = GameObject.Create("rock", isItem: true);
        ObjectRegistry.AddObject(rock);
        bag.AddObject(rock);
        var gem = GameObject.Create("gem", isItem: true);
        ObjectRegistry.AddObject(gem);
        bag.AddObject(gem);
        // The id stays in the container snapshot while the registry entry is
        // gone; the fused pass must skip it instead of surfacing a null.
        ObjectRegistry.RemoveObject(rock);

        var got = ContentUtils.FilterContents(bag, _ => true);
        Assert.Single(got);
        Assert.Same(gem, got[0]);
    }
}
