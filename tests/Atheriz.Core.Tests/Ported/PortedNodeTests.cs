// Port of atheriz/tests/test_node.py:1
// Port of atheriz/tests/test_nodes.py:1
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNodeTests
{
    [Fact] public void NodeLinkInit()
    {
        var link = new NodeLink("north", new Coord("TestArea",0,1,0), new List<string>{"n"});
        Assert.Equal("north", link.Name);
        Assert.Equal(new Coord("TestArea",0,1,0), link.Coord);
        Assert.Equal(new List<string>{"n"}, link.Aliases);
    }
    [Fact] public void NodeLinkStr()
    {
        var link = new NodeLink("south", new Coord("TestArea",0,0,0));
        var s = link.ToString();
        Assert.Contains("south", s);
        Assert.Contains("TestArea", s);
    }
    [Fact] public void NodeInit()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,1,2,3), desc:"A dark room");
        Assert.Equal(new Coord(area,1,2,3), node.Coord);
        Assert.Equal("A dark room", node.Desc);
        Assert.Empty(node.Links);
    }
    [Fact] public void NodeWithLinks()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var link = new NodeLink("north", new Coord(area,0,1,0));
        var node = new Node(new Coord(area,0,0,0), links: new List<NodeLink>{link});
        Assert.Single(node.Links); Assert.Equal("north", node.Links[0].Name);
    }
    [Fact] public void NodeAddLink()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,0,0,0));
        var link = new NodeLink("east", new Coord(area,1,0,0));
        node.AddLink(link);
        Assert.Single(node.Links); Assert.Equal("east", node.Links[0].Name);
    }
    [Fact] public void NodeNouns()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,0,0,0));
        node.AddNoun("fountain", "A marble fountain with clear water");
        Assert.Equal("A marble fountain with clear water", node.GetNoun("fountain"));
        node.RemoveNoun("fountain");
        Assert.Null(node.GetNoun("fountain"));
    }
    [Fact] public void RemoveNounNonexistentNoCrash()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,0,0,0));
        var ex = Record.Exception(()=> node.RemoveNoun("nonexistent"));
        Assert.Null(ex);
    }
    [Fact] public void NodeEquality()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var n1 = new Node(new Coord(area,0,0,0));
        var n2 = new Node(new Coord(area,0,0,0));
        var n3 = new Node(new Coord(area,1,0,0));
        Assert.Equal(n1, n1);
        Assert.NotEqual(n1, n2);
        Assert.Equal(n1.Coord, n2.Coord);
        Assert.NotEqual(n1, n3);
    }
    [Fact] public void NodeGridInit()
    {
        var grid = new NodeGrid("test",5);
        Assert.Equal(5, grid.Z);
        Assert.Equal(0, grid.Count);
    }
    [Fact] public void NodeGridAddGetNode()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var grid = new NodeGrid(area,0);
        var node = new Node(new Coord(area,1,2,0));
        grid.Nodes[(1,2)] = node;
        Assert.Equal(1, grid.Count);
        Assert.Same(node, grid.GetNode((1,2)));
        Assert.Null(grid.GetNode((0,0)));
    }
    [Fact] public void NodeGridClear()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var grid = new NodeGrid(area,0);
        grid.Nodes[(0,0)] = new Node(new Coord(area,0,0,0));
        grid.Nodes[(1,1)] = new Node(new Coord(area,1,1,0));
        Assert.Equal(2, grid.Count);
        grid.Clear(); Assert.Equal(0, grid.Count);
    }
    [Fact] public void NodeAreaInit()
    {
        var area = new NodeArea("Forest", theme:"nature");
        Assert.Equal("Forest", area.Name);
        Assert.Equal("nature", area.Theme);
        Assert.Equal(0, area.Count);
    }
    [Fact] public void NodeAreaAddGetGrid()
    {
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea",0);
        area.AddGrid(grid);
        Assert.Equal(1, area.Count);
        Assert.Same(grid, area.GetGrid(0));
        Assert.Equal("TestArea", grid.Area);
    }
    [Fact] public void NodeAreaRemoveGrid()
    {
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea",0);
        area.AddGrid(grid);
        area.RemoveGrid(0);
        Assert.Equal(0, area.Count);
    }
    [Fact] public void RemoveGridNonexistentNoCrash()
    {
        var area = new NodeArea("TestArea");
        var ex = Record.Exception(()=> area.RemoveGrid(999));
        Assert.Null(ex);
    }
    [Fact] public void NodeAreaClear()
    {
        var area = new NodeArea("TestArea");
        area.AddGrid(new NodeGrid("TestArea",0));
        area.AddGrid(new NodeGrid("TestArea",1));
        Assert.Equal(2, area.Count);
        area.Clear(); Assert.Equal(0, area.Count);
    }
    [Fact] public void NodeAreaGetNodes()
    {
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea",0);
        var n1 = new Node(new Coord("TestArea",0,0,0));
        var n2 = new Node(new Coord("TestArea",1,1,0));
        grid.Nodes[(0,0)]=n1; grid.Nodes[(1,1)]=n2;
        area.AddGrid(grid);
        var nodes = area.GetNodes(new List<(int,int,int)>{(0,0,0),(1,1,0),(99,99,0)});
        Assert.Equal(2, nodes.Count); Assert.Contains(n1, nodes); Assert.Contains(n2, nodes);
    }
    [Fact] public void TransitionInit()
    {
        var t = new Transition(new Coord("Area1",0,0,0), new Coord("Area2",0,0,0), "north");
        Assert.Equal(new Coord("Area1",0,0,0), t.FromCoord);
        Assert.Equal(new Coord("Area2",0,0,0), t.ToCoord);
        Assert.Equal("north", t.Name);
    }
    [Fact] public void NodeHandlerAddGetArea()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area = new NodeArea("TestArea");
        handler.AddArea(area);
        Assert.Same(area, handler.GetArea("TestArea"));
        Assert.Null(handler.GetArea("Nonexistent"));
    }
    [Fact] public void NodeHandlerGetAreas()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        handler.AddArea(new NodeArea("Area1"));
        handler.AddArea(new NodeArea("Area2"));
        Assert.Equal(2, handler.GetAreas().Count);
    }
    [Fact] public void NodeHandlerRemoveArea()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area = new NodeArea("TestArea");
        handler.AddArea(area);
        handler.RemoveArea("TestArea");
        Assert.Null(handler.GetArea("TestArea"));
    }
    [Fact] public void NodeHandlerClear()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        handler.AddArea(new NodeArea("Area1"));
        handler.AddArea(new NodeArea("Area2"));
        handler.Clear();
        Assert.Empty(handler.GetAreas());
    }
    [Fact] public void NodeHandlerGetNode()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea",0);
        var node = new Node(new Coord("TestArea",5,10,0));
        grid.Nodes[(5,10)]=node;
        area.AddGrid(grid);
        handler.AddArea(area);
        var res = handler.GetNode(new Coord("TestArea",5,10,0));
        Assert.Same(node, res);
        Assert.Null(handler.GetNode(new Coord("TestArea",99,99,0)));
        Assert.Null(handler.GetNode(new Coord("NonexistentArea",0,0,0)));
    }
    [Fact] public void NodeHandlerGetNodes()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea",0);
        var n1 = new Node(new Coord("TestArea",0,0,0));
        var n2 = new Node(new Coord("TestArea",1,1,0));
        grid.Nodes[(0,0)]=n1; grid.Nodes[(1,1)]=n2;
        area.AddGrid(grid); handler.AddArea(area);
        var nodes = handler.GetNodes(new List<Coord>{new Coord("TestArea",0,0,0), new Coord("TestArea",1,1,0)});
        Assert.Equal(2, nodes.Count);
    }
    [Fact] public void NodeHandlerAddRemoveTransition()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var t = new Transition(new Coord("Area1",0,0,0), new Coord("Area2",0,0,0), "north");
        handler.AddTransition(t);
        // Check via internal dict via FindTransitions
        var found = handler.FindTransitions(toArea:"Area2");
        Assert.Single(found);
        handler.RemoveTransition(new Coord("Area2",0,0,0));
        Assert.Empty(handler.FindTransitions(toArea:"Area2"));
    }
    [Fact] public void NodeHandlerFindTransitions()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var t1 = new Transition(new Coord("Area1",0,0,0), new Coord("Area2",0,0,0), "north");
        var t2 = new Transition(new Coord("Area1",0,0,1), new Coord("Area3",0,0,1), "up");
        var t3 = new Transition(new Coord("Area2",0,0,0), new Coord("Area1",0,0,0), "south");
        handler.AddTransition(t1); handler.AddTransition(t2); handler.AddTransition(t3);
        Assert.Equal(2, handler.FindTransitions(fromArea:"Area1").Count);
        Assert.Single(handler.FindTransitions(toArea:"Area2"));
    }
    [Fact] public void FindTransitionsMultiCriteria()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var t1 = new Transition(new Coord("A",0,0,1), new Coord("B",0,0,2), "north");
        var t2 = new Transition(new Coord("A",0,0,1), new Coord("C",0,0,3), "east");
        var t3 = new Transition(new Coord("D",0,0,2), new Coord("E",0,0,2), "south");
        handler.AddTransition(t1); handler.AddTransition(t2); handler.AddTransition(t3);
        Assert.Single(handler.FindTransitions(fromZ:1, toZ:2));
        Assert.Equal(t1, handler.FindTransitions(fromZ:1, toZ:2)[0]);
        Assert.Equal(2, handler.FindTransitions(fromZ:1).Count);
        Assert.Equal(2, handler.FindTransitions(toZ:2).Count);
        Assert.Single(handler.FindTransitions(fromArea:"A", toArea:"B"));
    }
    [Fact] public void NodeHandlerAddDoor()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var door = new Door(new Coord("Area1",0,0,0), new Coord("Area2",0,0,0), "north","south");
        handler.AddDoor(door);
        var doorsFrom = handler.GetDoors(new Coord("Area1",0,0,0));
        var doorsTo = handler.GetDoors(new Coord("Area2",0,0,0));
        Assert.NotNull(doorsFrom); Assert.NotNull(doorsTo);
        Assert.True(doorsFrom!.ContainsKey("north"));
        Assert.True(doorsTo!.ContainsKey("south"));
        Assert.Same(door, doorsFrom["north"]);
        Assert.Same(door, doorsTo["south"]);
    }
    [Fact] public void NodeGridAddNodeCreatesTransition()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area1 = new NodeArea("Area1");
        var area2 = new NodeArea("Area2");
        handler.AddArea(area1); handler.AddArea(area2);
        var grid = new NodeGrid("Area1",0);
        area1.AddGrid(grid);
        var link = new NodeLink("north", new Coord("Area2",0,0,0));
        var node = new Node(new Coord("Area1",0,0,0), links: new List<NodeLink>{link});
        grid.AddNode(node);
        var found = handler.FindTransitions(toArea:"Area2");
        Assert.Single(found);
        Assert.Equal(new Coord("Area1",0,0,0), found[0].FromCoord);
        Assert.Equal("north", found[0].Name);
    }
    [Fact] public void NodeGridRemoveNodeRemovesTransition()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area1 = new NodeArea("Area1");
        var area2 = new NodeArea("Area2");
        handler.AddArea(area1); handler.AddArea(area2);
        var grid = new NodeGrid("Area1",0);
        area1.AddGrid(grid);
        var link = new NodeLink("north", new Coord("Area2",0,0,0));
        var node = new Node(new Coord("Area1",0,0,0), links: new List<NodeLink>{link});
        grid.AddNode(node);
        Assert.Single(handler.FindTransitions(toArea:"Area2"));
        grid.RemoveNode((0,0));
        Assert.Empty(handler.FindTransitions(toArea:"Area2"));
    }
    [Fact] public void NodeRemoveLinkRemovesTransition()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area1 = new NodeArea("Area1");
        var area2 = new NodeArea("Area2");
        handler.AddArea(area1); handler.AddArea(area2);
        var grid = new NodeGrid("Area1",0);
        area1.AddGrid(grid);
        var link = new NodeLink("north", new Coord("Area2",0,0,0));
        var node = new Node(new Coord("Area1",0,0,0), links: new List<NodeLink>{link});
        grid.AddNode(node);
        Assert.Single(handler.FindTransitions(toArea:"Area2"));
        node.RemoveLink("north");
        Assert.Empty(handler.FindTransitions(toArea:"Area2"));
        Assert.Empty(node.Links);
    }
    [Fact] public void NodeRemoveLinkSameAreaNoTransition()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var area = new NodeArea("TestArea");
        handler.AddArea(area);
        var grid = new NodeGrid("TestArea",0);
        area.AddGrid(grid);
        var link = new NodeLink("north", new Coord("TestArea",0,1,0));
        var node = new Node(new Coord("TestArea",0,0,0), links: new List<NodeLink>{link});
        grid.AddNode(node);
        Assert.Empty(handler.FindTransitions(toArea:"TestArea"));
        node.RemoveLink("north");
        Assert.Empty(node.Links);
    }
    [Fact] public void GetDisplayNameNoLooker()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,0,0,0));
        var res = node.GetDisplayName(null);
        Assert.Equal("", res);
    }
    // ----- tests/test_nodes.py -----
    [Fact] public void GetRandomNodeOnEmptyGridReturnsNone()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test",0);
        Assert.Null(grid.GetRandomNode());
    }
    [Fact] public void GetRandomNodeReturnsExistingNode()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var grid = new NodeGrid(area,0);
        var node = new Node(new Coord(area,3,3,0));
        grid.AddNode(node);
        Assert.Same(node, grid.GetRandomNode());
    }
    [Fact] public void RemoveDataMissingKeyIsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var area = new NodeArea("testarea");
        var ex = Record.Exception(()=> area.RemoveData("missing"));
        Assert.Null(ex);
    }
    [Fact] public void StrIncludesAreaName()
    {
        using var env = GlobalTestEnv.Enter();
        var area = new NodeArea("testarea");
        var grid = new NodeGrid("testarea",0);
        grid.AddNode(new Node(new Coord("testarea",0,0,0)));
        area.AddGrid(grid);
        Assert.Contains("testarea", area.ToString());
    }
    [Fact] public void NodeIsHashable()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var n = new Node(new Coord(area,0,0,0));
        var set = new HashSet<Node>{n};
        Assert.Contains(n, set);
    }
    [Fact] public void RemoveAreaMissingIsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var ex = Record.Exception(()=> handler.RemoveArea("missing"));
        Assert.Null(ex);
    }
    [Fact] public void RemoveTransitionMissingIsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler();
        var ex = Record.Exception(()=> handler.RemoveTransition(new Coord("missing",0,0,0)));
        Assert.Null(ex);
    }
    [Fact] public void NonRecursiveDeleteDoesNotOrphanContents()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,5,5,0));
        var fallback = new Node(new Coord(area,5,4,0));
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(fallback.Coord); fallback.AddObject(caller);
        var obj = GameObject.Create("item"); ObjectRegistry.AddObject(obj);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(obj);
        Assert.Equal(node.Coord, ((Persistence.Dto.LocationRef.CoordLocation)obj.Location).Coord);
        node.Delete(caller, recursive:false);
        Assert.NotNull(obj.Location);
        Assert.False(obj.Location is Persistence.Dto.LocationRef.CoordLocation cl && cl.Coord.Equals(node.Coord));
    }
    [Fact] public void NonRecursiveDeleteUsesHome()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(); NodeHandler.SetCurrent(nh);
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,5,5,0));
        var homeNode = new Node(new Coord(area,0,0,0));
        var caller = GameObject.Create("caller"); ObjectRegistry.AddObject(caller);
        var obj = GameObject.Create("item"); ObjectRegistry.AddObject(obj);
        obj.Home = new Persistence.Dto.LocationRef.CoordLocation(homeNode.Coord);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(obj);
        node.Delete(caller, recursive:false);
        Assert.Equal(homeNode.Coord, ((Persistence.Dto.LocationRef.CoordLocation)obj.Location).Coord);
    }
    [Fact] public void NonRecursiveDeleteFallsBackToCallersLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(); NodeHandler.SetCurrent(nh);
        var area = $"test_area_{Guid.NewGuid():N}";
        var node = new Node(new Coord(area,5,5,0));
        var fallback = new Node(new Coord(area,5,4,0));
        var caller = GameObject.Create("caller"); ObjectRegistry.AddObject(caller);
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(fallback.Coord); fallback.AddObject(caller);
        var obj = GameObject.Create("item"); ObjectRegistry.AddObject(obj);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(obj);
        node.Delete(caller, recursive:false);
        Assert.Equal(fallback.Coord, ((Persistence.Dto.LocationRef.CoordLocation)obj.Location).Coord);
    }
    [Fact]
    public void AddNodeOverwriteWarns()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test",0);
        var a = new Node(new Coord("test",0,0,0)); a.AddLink(new NodeLink("north", new Coord("test",0,2,0)));
        grid.AddNode(a);
        var b = new Node(new Coord("test",0,0,0)); b.AddLink(new NodeLink("south", new Coord("test",0,-2,0)));
        var sw = new StringWriter(); var oldErr = Console.Error; Console.SetError(sw);
        try { grid.AddNode(b); } finally { Console.SetError(oldErr); }
        var log = sw.ToString().ToLower();
        Assert.Contains("overwrit", log);
    }
    [Fact]
    public void AddNodeSameInstanceDoesNotWarn()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("test",0);
        var node = new Node(new Coord("test",0,0,0));
        grid.AddNode(node);
        var sw = new StringWriter(); var oldErr = Console.Error; Console.SetError(sw);
        try { grid.AddNode(node); } finally { Console.SetError(oldErr); }
        var log = sw.ToString().ToLower();
        Assert.DoesNotContain("overwrit", log);
    }
}
