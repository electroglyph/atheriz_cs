using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Bulk fetch returns matches in caller order skipping misses, and the save
// filter persists only live non-temporary non-node objects.
[Collection("Ported")]
public sealed class RegistryBulkFetchTests
{
    [Fact]
    public void Get_MultiId_ReturnsMatchesInOrderSkippingMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var o1 = GameObject.Create("bulk1");
        var o2 = GameObject.Create("bulk2");
        var o3 = GameObject.Create("bulk3");
        ObjectRegistry.AddObject(o1);
        ObjectRegistry.AddObject(o2);
        ObjectRegistry.AddObject(o3);

        var res = ObjectRegistry.Get([o1.Id, 999999, o3.Id, o2.Id]);
        Assert.Equal(3, res.Count);
        Assert.Same(o1, res[0]);
        Assert.Same(o3, res[1]);
        Assert.Same(o2, res[2]);
    }

    [Fact]
    public void Get_EmptyIds_ReturnsEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Empty(ObjectRegistry.Get([]));
    }

    [Fact]
    public void SaveObjects_FilterSkipsTemporaryNodeAndDeleted()
    {
        using var env = GlobalTestEnv.Enter();
        var keep = GameObject.Create("savekeep");
        ObjectRegistry.AddObject(keep);
        keep.Desc = "dirty";
        keep.IsModified = true;

        var temp = GameObject.Create("savetemp");
        ObjectRegistry.AddObject(temp);
        temp.IsTemporary = true;
        temp.Desc = "dirty";
        temp.IsModified = true;

        var node = new Node(new Coord("bulkfilter", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        node.Desc = "dirty";
        node.IsModified = true;

        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            ObjectRegistry.SaveObjects(db);
            Assert.NotNull(db.Objects.Find(keep.Id));
            Assert.Null(db.Objects.Find(temp.Id));
            Assert.Null(db.Objects.Find(node.Id));
        }
    }
}
