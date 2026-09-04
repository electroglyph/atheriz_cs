// Port of atheriz/tests/test_delete.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDeleteTests
{
    private static GameObject MakeCaller(string name="Admin") => PortedHelpers.MakeCaller(name, Privilege.Admin);
    private static Node MakeRoom(string area="test")
    {
        var coord = new Coord(area, 0, 0, 0);
        var nh = NodeHandler.GetCurrent() ?? new NodeHandler();
        var n = new Node(coord, desc: "room");
        nh.AddNode(n);
        return n;
    }

    [Fact]
    public void ObjectDeleteNonRecursive()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var room = MakeRoom("del1");
        var container = GameObject.Create("Chest", isContainer: true);
        ObjectRegistry.AddObject(container);
        Assert.True(container.MoveTo(room));
        var item = GameObject.Create("Gold");
        ObjectRegistry.AddObject(item);
        Assert.True(item.MoveTo(container));
        Assert.Contains(item.Id, container.ContentsSnapshot);
        var ops = container.Delete(caller, recursive: false);
        Assert.NotNull(ops);
        // After non-recursive, item should be moved to room
        Assert.True(container.IsDeleted);
        Assert.DoesNotContain(container.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        Assert.Contains(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        var loc = item.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.NotNull(loc);
        Assert.Equal(room.Coord, loc!.Coord);
        Assert.Contains(item.Id, room.ContentsSnapshot);
    }

    [Fact]
    public void ObjectDeleteRecursive()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var room = MakeRoom("del2");
        var container = GameObject.Create("Chest", isContainer: true);
        ObjectRegistry.AddObject(container);
        container.MoveTo(room);
        var item = GameObject.Create("Gold");
        ObjectRegistry.AddObject(item);
        item.MoveTo(container);
        var ops = container.Delete(caller, recursive: true);
        Assert.NotNull(ops);
        Assert.True(container.IsDeleted);
        Assert.True(item.IsDeleted);
        Assert.DoesNotContain(container.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        Assert.DoesNotContain(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
    }

    [Fact]
    public void ObjectDeleteLock()
    {
        using var env = GlobalTestEnv.Enter();
        var caller2 = GameObject.Create("caller", privilege: Privilege.Player);
        ObjectRegistry.AddObject(caller2);
        var item = GameObject.Create("Protected");
        ObjectRegistry.AddObject(item);
        var room = MakeRoom("del3");
        item.MoveTo(room);
        item.AddLock("delete", _ => false);
        var ops = item.Delete(caller2);
        Assert.Null(ops);
        Assert.Contains(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public void ObjectDeleteSameId()
    {
        using var env = GlobalTestEnv.Enter();
        var item = GameObject.Create("Blehhh");
        ObjectRegistry.AddObject(item);
        item.Id = 888; // override after registry add — need to re-add under new id
        ObjectRegistry.AddObject(item);
        var room = MakeRoom("del4");
        item.MoveTo(room);
        var caller2 = GameObject.Create("caller_yay", privilege: Privilege.Player);
        ObjectRegistry.AddObject(caller2);
        caller2.Id = 888;
        ObjectRegistry.AddObject(caller2);
        var ops = item.Delete(caller2);
        Assert.Null(ops);
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public void NodeDeleteNonRecursive()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var room = MakeRoom("del5");
        var item = GameObject.Create("Lamp"); ObjectRegistry.AddObject(item);
        item.MoveTo(room);
        var nh = NodeHandler.GetCurrent() ?? new NodeHandler();
        var homeRoom = new Node(new Coord("test", 1, 1, 1));
        nh.AddNode(homeRoom);
        item.Home = new Persistence.Dto.LocationRef.CoordLocation(homeRoom.Coord);
        Assert.Contains(item.Id, room.ContentsSnapshot);
        var result = room.Delete(caller, recursive: false);
        Assert.NotNull(result);
        var ops = result!.Value.ops;
        // item should be moved to home
        var loc = item.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.NotNull(loc);
        Assert.Equal(homeRoom.Coord, loc!.Coord);
        Assert.Contains(item.Id, homeRoom.ContentsSnapshot);
        Assert.True(room.IsDeleted);
        Assert.Null(NodeHandler.GetCurrent()?.GetNode(room.Coord));
    }

    [Fact]
    public void NodeDeleteRecursive()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var room = MakeRoom("del6");
        var item = GameObject.Create("Lamp"); ObjectRegistry.AddObject(item); item.MoveTo(room);
        var result = room.Delete(caller, recursive: true);
        Assert.NotNull(result);
        Assert.True(room.IsDeleted);
        Assert.True(item.IsDeleted);
        Assert.DoesNotContain(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
    }

    [Fact]
    public void NodeDeleteRegistry()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var nh = NodeHandler.GetCurrent() ?? new NodeHandler();
        var room = MakeRoom("del7");
        Assert.Equal(room, nh.GetNode(room.Coord));
        var result = room.Delete(caller);
        Assert.NotNull(result);
        Assert.Null(nh.GetNode(room.Coord));
    }

    [Fact]
    public void AccountDelete()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var acc = Account.Create("TestAccount", "password");
        Assert.Contains(acc.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        // Save to DB via GetSaveOps
        var path = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH")!;
        using var db = new AtherizDbContext(path);
        db.Database.EnsureCreated();
        var (sql, pars) = acc.GetSaveOps();
        // Use registry SaveObjects path instead of direct sql
        ObjectRegistry.SaveObjects(db, force:true);
        var res = acc.Delete(caller, false);
        Assert.True(res);
        Assert.True(acc.IsDeleted);
        Assert.DoesNotContain(acc.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        using var db2 = new AtherizDbContext(path);
        var row = db2.Objects.Find(acc.Id);
        Assert.Null(row);
    }

    [Fact]
    public void ChannelDelete()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var ch = Channel.Create("Public");
        Assert.Contains(ch.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        var path = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH")!;
        using var db = new AtherizDbContext(path);
        db.Database.EnsureCreated();
        ObjectRegistry.SaveObjects(db, force:true);
        var res = ch.Delete(caller, false);
        Assert.NotNull(res);
        if (res != null)
        {
            var ops = new List<(string Sql, object[] Params)>();
            foreach (var o in res.Value.ops)
            {
                if (o is ValueTuple<string, object[]> vt) ops.Add((vt.Item1, vt.Item2));
                else if (o is Tuple<string, object[]> tt) ops.Add((tt.Item1, tt.Item2));
                else ops.Add(ch.GetDelOps());
            }
            if (ops.Count > 0) ObjectRegistry.DeleteObjects(db, ops);
            else ObjectRegistry.DeleteObjects(db, new List<(string, object[])>{ ch.GetDelOps() });
        }
        Assert.True(ch.IsDeleted);
        Assert.DoesNotContain(ch.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        using var db2 = new AtherizDbContext(path);
        Assert.Null(db2.Objects.Find(ch.Id));
    }

    [Fact]
    public void DeleteObjectsUtility()
    {
        using var env = GlobalTestEnv.Enter();
        var item = GameObject.Create("To-be-deleted");
        ObjectRegistry.AddObject(item);
        var path = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH")!;
        using var db = new AtherizDbContext(path);
        db.Database.EnsureCreated();
        ObjectRegistry.SaveObjects(db, force:true);
        using var dbCheck = new AtherizDbContext(path);
        Assert.NotNull(dbCheck.Objects.Find(item.Id));
        var ops = new List<(string Sql, object[] Params)> { item.GetDelOps() };
        ObjectRegistry.DeleteObjects(db, ops);
        using var db2 = new AtherizDbContext(path);
        Assert.Null(db2.Objects.Find(item.Id));
    }
}
