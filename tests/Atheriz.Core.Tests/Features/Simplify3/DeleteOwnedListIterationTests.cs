// Pins for iterating owned delete lists directly (GameObject.Delete.cs,
// Node.cs): recursive and non-recursive deletes of objects and nodes keep
// their established move-or-delete outcomes.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class DeleteOwnedListIterationTests
{
    [Fact]
    public void DeleteRecursive_ObjectWithChildren_DeletesAll()
    {
        using var env = GlobalTestEnv.Enter();
        var admin = GameObject.Create("admin", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        var coin = GameObject.Create("coin", isItem: true);
        ObjectRegistry.AddObject(coin);
        var gem = GameObject.Create("gem", isItem: true);
        ObjectRegistry.AddObject(gem);
        Assert.True(coin.MoveTo(bag));
        Assert.True(gem.MoveTo(bag));

        var res = bag.Delete(admin, recursive: true);

        Assert.NotNull(res);
        Assert.Equal(3, res.Value.count);
        Assert.Empty(ObjectRegistry.Get(bag.Id));
        Assert.Empty(ObjectRegistry.Get(coin.Id));
        Assert.Empty(ObjectRegistry.Get(gem.Id));
    }

    [Fact]
    public void DeleteNonRecursive_ObjectWithChildren_MovesChildrenToLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var admin = GameObject.Create("admin", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        var room = GameObject.Create("room", isContainer: true);
        ObjectRegistry.AddObject(room);
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        Assert.True(bag.MoveTo(room));
        var coin = GameObject.Create("coin", isItem: true);
        ObjectRegistry.AddObject(coin);
        Assert.True(coin.MoveTo(bag));

        var res = bag.Delete(admin, recursive: false);

        Assert.NotNull(res);
        Assert.Empty(ObjectRegistry.Get(bag.Id));
        Assert.Contains(coin.Id, room.ContentsSnapshot);
    }

    [Fact]
    public void DeleteRecursive_NodeWithChildren_DeletesAll()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("dellist", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var admin = GameObject.Create("admin", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        var kid = GameObject.Create("kid");
        ObjectRegistry.AddObject(kid);
        Assert.True(kid.MoveTo(node));

        var res = node.Delete(admin, recursive: true);

        Assert.NotNull(res);
        Assert.Equal(2, res.Value.count);
        Assert.Empty(ObjectRegistry.Get(node.Id));
        Assert.Empty(ObjectRegistry.Get(kid.Id));
    }

    [Fact]
    public void DeleteNonRecursive_NodeWithChildren_MovesChildrenHome()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("dellist", 1, 1, 0));
        ObjectRegistry.AddObject(node);
        var admin = GameObject.Create("admin", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(admin);
        var box = GameObject.Create("box", isContainer: true);
        ObjectRegistry.AddObject(box);
        var kid = GameObject.Create("kid");
        ObjectRegistry.AddObject(kid);
        kid.Home = new LocationRef.ObjectLocation(box.Id);
        Assert.True(kid.MoveTo(node));

        var res = node.Delete(admin, recursive: false);

        Assert.NotNull(res);
        Assert.Empty(ObjectRegistry.Get(node.Id));
        Assert.Contains(kid.Id, box.ContentsSnapshot);
    }
}
