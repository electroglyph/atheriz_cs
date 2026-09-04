// Port of atheriz/tests/test_nodes.py:1 — faithful 15 tests
using System.IO;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNodesTests
{
    // TestNodeGrid.test_get_random_node_on_empty_grid_returns_none
    [Fact] public void GetRandomNodeOnEmptyGridReturnsNone() // test_nodes.py:19
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test", 0);
        Assert.Null(grid.GetRandomNode());
    }

    // test_nodes.py:25 test_get_random_node_returns_existing_node
    [Fact] public void GetRandomNodeReturnsExistingNode() // test_nodes.py:25
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test", 0);
        var node = new Node(new Coord("test", 3, 3, 0));
        grid.AddNode(node);
        Assert.Same(node, grid.GetRandomNode());
    }

    // TestNodeArea.test_remove_data_missing_key_is_noop
    [Fact] public void RemoveDataMissingKeyIsNoop() // test_nodes.py:33
    {
        using var env = GlobalTestEnv.Enter();
        var area = new NodeArea("testarea");
        var ex = Record.Exception(()=> area.RemoveData("missing"));
        Assert.Null(ex);
    }

    // test_nodes.py:38 test_str_includes_area_name
    [Fact] public void StrIncludesAreaName() // test_nodes.py:38
    {
        using var env = GlobalTestEnv.Enter();
        var area = new NodeArea("testarea");
        var grid = new NodeGrid("testarea", 0);
        grid.AddNode(new Node(new Coord("testarea", 0, 0, 0)));
        area.AddGrid(grid);
        Assert.Contains("testarea", area.ToString());
    }

    // TestNodeHashability.test_node_is_hashable
    [Fact] public void NodeIsHashable() // test_nodes.py:48
    {
        using var env = GlobalTestEnv.Enter();
        var n = new Node(new Coord("test", 0, 0, 0));
        ObjectRegistry.AddObject(n);
        var s = new HashSet<Node>{n};
        Assert.Contains(n, s);
    }

    // TestNodeHandler.test_remove_area_missing_is_noop
    [Fact] public void RemoveAreaMissingIsNoop() // test_nodes.py:60
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(handler);
        var ex = Record.Exception(()=> handler.RemoveArea("missing"));
        Assert.Null(ex);
    }

    // test_nodes.py:65 test_remove_transition_missing_is_noop
    [Fact] public void RemoveTransitionMissingIsNoop() // test_nodes.py:65
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(handler);
        var ex = Record.Exception(()=> handler.RemoveTransition(new Coord("missing",0,0,0)));
        Assert.Null(ex);
    }

    // TestNodeDeleteRelocation.test_nonrecursive_delete_does_not_orphan_contents
    [Fact] public void NonRecursiveDeleteDoesNotOrphanContents() // test_nodes.py:72
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("test",5,5,0));
        var fallback = new Node(new Coord("test",5,4,0));
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(fallback.Coord);
        fallback.AddObject(caller);
        var obj = GameObject.Create("item");
        ObjectRegistry.AddObject(obj);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(obj);
        Assert.Same(node, obj.ResolveLocationObject());
        node.Delete(caller, recursive:false);
        Assert.NotNull(obj.ResolveLocationObject());
        Assert.NotSame(node, obj.ResolveLocationObject());
    }

    // test_nodes.py:91 test_nonrecursive_delete_uses_home
    [Fact] public void NonRecursiveDeleteUsesHome() // test_nodes.py:91
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("test",5,5,0));
        var homeNode = new Node(new Coord("test",0,0,0));
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        var obj = GameObject.Create("item");
        ObjectRegistry.AddObject(obj);
        obj.Home = new Persistence.Dto.LocationRef.CoordLocation(homeNode.Coord);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(obj);
        node.Delete(caller, recursive:false);
        Assert.Equal(homeNode.Coord, ((Persistence.Dto.LocationRef.CoordLocation)obj.Home).Coord);
        Assert.Same(homeNode, obj.ResolveLocationObject());
    }

    // test_nodes.py:107 test_nonrecursive_delete_falls_back_to_callers_location
    [Fact] public void NonRecursiveDeleteFallsBackToCallersLocation() // test_nodes.py:107
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("test",5,5,0));
        var fallback = new Node(new Coord("test",5,4,0));
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(fallback.Coord);
        fallback.AddObject(caller);
        var obj = GameObject.Create("item");
        ObjectRegistry.AddObject(obj);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(obj);
        node.Delete(caller, recursive:false);
        Assert.Same(fallback, obj.ResolveLocationObject());
    }

    // test_nodes.py:123 test_nonrecursive_delete_self_fallback_leaves_contents
    [Fact] public void NonRecursiveDeleteSelfFallbackLeavesContents() // test_nodes.py:123
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("test",5,5,0));
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        var obj = GameObject.Create("item");
        ObjectRegistry.AddObject(obj);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(obj);
        node.Delete(caller, recursive:false);
        Assert.NotSame(node, obj.ResolveLocationObject());
    }

    // TestNodeGridOverwrite.test_add_node_overwrite_warns
    [Fact] public void AddNodeOverwriteWarns() // test_nodes.py:140
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test", 0);
        var nodeA = new Node(new Coord("test",0,0,0));
        nodeA.AddLink(new NodeLink("north", new Coord("test",0,2,0), new List<string>{"n"}));
        grid.AddNode(nodeA);
        var nodeB = new Node(new Coord("test",0,0,0));
        nodeB.AddLink(new NodeLink("south", new Coord("test",0,-2,0), new List<string>{"s"}));
        var sw = new StringWriter();
        var oldErr = Console.Error;
        Console.SetError(sw);
        try { grid.AddNode(nodeB); } finally { Console.SetError(oldErr); }
        var log = sw.ToString().ToLowerInvariant();
        Assert.Contains("overwrit", log);
    }

    // test_nodes.py:155 test_add_node_same_instance_does_not_warn
    [Fact] public void AddNodeSameInstanceDoesNotWarn() // test_nodes.py:155
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test", 0);
        var node = new Node(new Coord("test",0,0,0));
        grid.AddNode(node);
        var sw = new StringWriter();
        var oldErr = Console.Error;
        Console.SetError(sw);
        try { grid.AddNode(node); } finally { Console.SetError(oldErr); }
        var log = sw.ToString().ToLowerInvariant();
        Assert.DoesNotContain("overwrit", log);
    }

    // TestNodeNonRecursiveDeleteStranded.test_nonrecursive_delete_with_no_home_and_no_fallback_does_not_strand
    [Fact] public void NonRecursiveDeleteWithNoHomeAndNoFallbackDoesNotStrand() // test_nodes.py:168
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test",9,9,0));
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = Persistence.Dto.LocationRef.NullLocation.Instance;
        caller.Home = Persistence.Dto.LocationRef.NullLocation.Instance;
        var item = GameObject.Create("stranded_item");
        ObjectRegistry.AddObject(item);
        item.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(item);
        item.Home = Persistence.Dto.LocationRef.NullLocation.Instance;
        Assert.Same(node, item.ResolveLocationObject());
        // patch get_node_handler to return mock-like empty handler (no fallback)
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var result = node.Delete(caller, recursive:false);
        Assert.NotNull(result);
        Assert.NotSame(node, item.ResolveLocationObject());
        Assert.True(item.ResolveLocationObject()==null || item.IsDeleted);
    }

    // test_nodes.py:185 test_nonrecursive_delete_caller_on_node_with_no_fallback
    [Fact] public void NonRecursiveDeleteCallerOnNodeWithNoFallback() // test_nodes.py:185
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test",8,8,0));
        var caller = GameObject.Create("caller2");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        var item = GameObject.Create("item2");
        ObjectRegistry.AddObject(item);
        item.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(item);
        item.Home = Persistence.Dto.LocationRef.NullLocation.Instance;
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        node.Delete(caller, recursive:false);
        Assert.NotSame(node, item.ResolveLocationObject());
        Assert.NotSame(node, caller.ResolveLocationObject());
    }
}
