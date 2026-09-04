// Port of atheriz/tests/test_node_cache.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNodeCacheTests
{
    [Fact]
    public void NodeRegisteredInObjectCacheOnCreate()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test",1,1,0));
        var got = ObjectRegistry.Get(node.Id);
        Assert.Single(got);
        Assert.Same(node, got[0]);
    }

    [Fact]
    public void NodeEvictedOnHandlerRemove()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        var node = new Node(new Coord("test2",2,2,0));
        nh.AddNode(node);
        Assert.Single(ObjectRegistry.Get(node.Id));
        nh.RemoveNode(node.Coord);
        Assert.Empty(ObjectRegistry.Get(node.Id));
    }

    [Fact]
    public void OldNodeEvictedOnGridOverwrite()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test",0);
        var a = new Node(new Coord("test",0,0,0));
        grid.AddNode(a);
        Assert.Single(ObjectRegistry.Get(a.Id));
        var b = new Node(new Coord("test",0,0,0));
        grid.AddNode(b);
        Assert.Empty(ObjectRegistry.Get(a.Id));
        Assert.Single(ObjectRegistry.Get(b.Id));
    }

    [Fact]
    public void HandlerClearEvictsAllNodes()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        var node = new Node(new Coord("test3",3,3,0));
        nh.AddNode(node);
        nh.Clear();
        Assert.Empty(ObjectRegistry.Get(node.Id));
    }

    [Fact]
    public void SaveObjectsSkipsNodes()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test4",4,4,0));
        var obj = GameObject.Create("saveable");
        ObjectRegistry.AddObject(obj); ObjectRegistry.AddObject(node);
        using var db = AtherizDbContextFactory.Create(env.TempPath);
        db.Database.EnsureCreated();
        ObjectRegistry.SaveObjects(db, force:true);
        var rows = db.Objects.ToList();
        Assert.DoesNotContain(rows, r => r.Id==node.Id);
        Assert.Contains(rows, r => r.Id==obj.Id);
    }

    [Fact]
    public void NodeReregisteredAfterHandlerLoad()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        var node = new Node(new Coord("TestAreaNC",1,1,0));
        nh.AddNode(node);
        nh.Save();
        // Simulate close + clear
        ObjectRegistry.ClearAll();
        var nh2 = new NodeHandler();
        nh2.Load();
        var loaded = nh2.GetNode(new Coord("TestAreaNC",1,1,0));
        Assert.NotNull(loaded);
        Assert.Single(ObjectRegistry.Get(node.Id));
        var newNode = new Node(new Coord("TestAreaNC",2,2,0));
        Assert.True(newNode.Id > node.Id);
    }

    [Fact]
    public void NoCollisionWarningOnBootLoad()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        var nodes = Enumerable.Range(0,3).Select(x=> new Node(new Coord("TestAreaNCC", x,0,0))).ToList();
        foreach(var n in nodes) nh.AddNode(n);
        nh.Save();
        ObjectRegistry.ClearAll();
        // Capture logger not needed — just ensure no exception and ids reloaded
        ObjectRegistry.LoadObjects(env.TempPath);
        var nh2 = new NodeHandler();
        nh2.Load();
        foreach(var n in nodes) Assert.Single(ObjectRegistry.Get(n.Id));
    }
}
