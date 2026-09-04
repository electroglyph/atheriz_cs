// Port of atheriz/tests/test_move_nodes.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMoveNodesTests
{
    private class TrackingCmdSet : CmdSet
    {
        public List<string> RemovedTags = new();
        public List<Command> Added = new();
        public override void RemoveByTag(string tag)
        {
            RemovedTags.Add(tag);
            base.RemoveByTag(tag);
        }
        public override void Adds(IEnumerable<Command> commands, string? tag = null)
        {
            var list = commands.ToList();
            Added.AddRange(list);
            base.Adds(list, tag);
        }
    }

    private static (NodeGrid grid, NodeHandler nh) MakeGridWithHandler(params Node[] nodes)
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        MapHandlerHolder.Set(new MapHandler(autoLoad:false));
        var grid = new NodeGrid("TestArea",0);
        foreach(var n in nodes) grid.Nodes[(n.Coord.X, n.Coord.Y)]=n;
        // Need to ensure we also have an Area to hold grid? For ApplyMoves doors/transitions, handler not needed for grid nodes but for remap we need handler set.
        // We do not add grid to area, but ApplyMoves will use NodeHandler.SetCurrent for remap.
        // However NodeGrid.ApplyMoves expects NodeHandler to remap doors; we set current.
        return (grid, nh);
    }

    private static (NodeGrid grid, object nh) MakeGrid(params Node[] nodes)
    {
        var grid = new NodeGrid("TestArea",0);
        foreach(var n in nodes) grid.Nodes[(n.Coord.X, n.Coord.Y)]=n;
        return (grid, new object());
    }

    [Fact] public void TestCheckMovesDeniesOccupiedDestination(){ using var env=GlobalTestEnv.Enter(); var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var (g,_)=MakeGrid(a,b); Assert.Equal(new HashSet<int>{0}, g.CheckMoves(new List<((int,int),(int,int))>{((0,0),(1,0))})); }
    [Fact] public void TestCheckMovesAllowsFreeDestination(){ using var env=GlobalTestEnv.Enter(); var a=new Node(new Coord("TestArea",0,0,0)); var (g,_)=MakeGrid(a); Assert.Empty(g.CheckMoves(new List<((int,int),(int,int))>{((0,0),(5,5))})); }

    [Fact]
    public void TestApplyMovesRekeysNodeAndInboundLinks()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); a.Desc="A"; var b=new Node(new Coord("TestArea",1,0,0)); b.Desc="B";
        b.Links.Add(new NodeLink("West", new Coord("TestArea",0,0,0)));
        var (grid,_)=MakeGrid(a,b);
        NodeHandler.SetCurrent(new NodeHandler(autoLoad:false));
        var failed=grid.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(5,0))});
        Assert.Empty(failed);
        Assert.Null(grid.GetNode((0,0)));
        var moved=grid.GetNode((5,0));
        Assert.Same(a, moved);
        Assert.Equal(new Coord("TestArea",5,0,0), moved!.Coord);
        Assert.Equal(new Coord("TestArea",5,0,0), b.Links[0].Coord);
    }

    [Fact]
    public void TestApplyMovesSupportsSwap()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0));
        a.Links.Add(new NodeLink("East", new Coord("TestArea",1,0,0))); b.Links.Add(new NodeLink("West", new Coord("TestArea",0,0,0)));
        var (grid,_)=MakeGrid(a,b);
        NodeHandler.SetCurrent(new NodeHandler(autoLoad:false));
        var failed=grid.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(1,0)),((1,0),(0,0))});
        Assert.Empty(failed);
        Assert.Same(a, grid.GetNode((1,0)));
        Assert.Same(b, grid.GetNode((0,0)));
        Assert.Equal(new Coord("TestArea",0,0,0), a.Links[0].Coord);
        Assert.Equal(new Coord("TestArea",1,0,0), b.Links[0].Coord);
    }

    [Fact]
    public void TestApplyMovesSupportsChains()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var (g,_)=MakeGrid(a,b);
        NodeHandler.SetCurrent(new NodeHandler(autoLoad:false));
        var failed=g.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(1,0)),((1,0),(2,0))});
        Assert.Empty(failed);
        Assert.Same(a, g.GetNode((1,0))); Assert.Same(b, g.GetNode((2,0)));
    }

    [Fact]
    public void TestApplyMovesReportsFailedIndicesWithoutTouchingThem()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var c=new Node(new Coord("TestArea",2,0,0)); var (g,_)=MakeGrid(a,b,c);
        NodeHandler.SetCurrent(new NodeHandler(autoLoad:false));
        var failed=g.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(5,0)),((1,0),(2,0))});
        Assert.Equal(new[]{1}, failed);
        Assert.Same(b, g.GetNode((1,0))); Assert.Same(c, g.GetNode((2,0))); Assert.Same(a, g.GetNode((5,0)));
    }

    [Fact]
    public void TestApplyMovesRekeysDoors()
    {
        using var env=GlobalTestEnv.Enter();
        var a = new Node(new Coord("TestArea",0,0,0));
        var b = new Node(new Coord("TestArea",1,0,0));
        var (grid, nh) = MakeGridWithHandler(a,b);
        // Door from (0,0) East to (1,0) West
        var door = new Door(new Coord("TestArea",0,0,0), new Coord("TestArea",1,0,0), "East","West", (0,0), "", "", true,false);
        nh.AddDoor(door);
        // Verify initial doors
        var failed = grid.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(4,0))});
        Assert.Empty(failed);
        var newFull = new Coord("TestArea",4,0,0);
        // Use GetDoors to verify rekey
        var oldDoors = nh.GetDoors(new Coord("TestArea",0,0,0));
        Assert.Null(oldDoors); // should be null or empty
        var newDoors = nh.GetDoors(newFull);
        Assert.NotNull(newDoors);
        Assert.True(newDoors!.ContainsKey("East"));
        Assert.Same(door, newDoors["East"]);
        var toDoors = nh.GetDoors(new Coord("TestArea",1,0,0));
        Assert.NotNull(toDoors);
        Assert.True(toDoors!.ContainsKey("West"));
        Assert.Same(door, toDoors["West"]);
        Assert.Equal(newFull, door.FromCoord);
        Assert.Equal(new Coord("TestArea",1,0,0), door.ToCoord);
    }

    [Fact]
    public void TestApplyMovesRefreshesCrossAreaTransitions()
    {
        using var env=GlobalTestEnv.Enter();
        var a = new Node(new Coord("TestArea",0,0,0));
        a.Links.Add(new NodeLink("Portal", new Coord("OtherArea",0,0,0)));
        var (grid, nh) = MakeGridWithHandler(a);
        // Need to ensure transition exists via AddNode side-effect: node.AddLink creates transition; but we built grid manually without AddNode.
        // Manually add transition
        nh.AddTransition(new Transition(a.Coord, new Coord("OtherArea",0,0,0), "Portal"));
        var failed = grid.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(3,0))});
        Assert.Empty(failed);
        // Retrieve transition via FindTransitions or reflection
        var found = nh.FindTransitions(toArea:"OtherArea");
        Assert.Single(found);
        var t = found[0];
        Assert.Equal(new Coord("TestArea",3,0,0), t.FromCoord);
        // Also verify via dictionary reflection
        var field = typeof(NodeHandler).GetField("_transitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = field!.GetValue(nh) as Dictionary<Coord, Transition>;
        Assert.NotNull(dict);
        Assert.True(dict!.ContainsKey(new Coord("OtherArea",0,0,0)));
        Assert.Equal(new Coord("TestArea",3,0,0), dict[new Coord("OtherArea",0,0,0)].FromCoord);
    }

    [Fact]
    public void TestApplyMovesRebuildsExitsForMovedRoomContents()
    {
        using var env=GlobalTestEnv.Enter();
        var a = new Node(new Coord("TestArea",0,0,0));
        a.Links.Add(new NodeLink("East", new Coord("TestArea",1,0,0)));
        var (grid, nh) = MakeGridWithHandler(a);
        var occupant = GameObject.Create("occupant");
        occupant.InternalCmdSet = new TrackingCmdSet();
        // Need to ensure occupant is in ObjectRegistry and node contents
        ObjectRegistry.AddObject(occupant);
        // Add to node: Use Node.AddObject which also adds exits but we want to track later rebuild
        // First clear tracking after initial Add
        a.AddObject(occupant);
        // Now occupant has an ExitCommand for East; reset tracking
        var tracking = (TrackingCmdSet)occupant.InternalCmdSet!;
        tracking.RemovedTags.Clear();
        tracking.Added.Clear();
        // Also ensure occupant location is via node's contents
        occupant.Location = new Persistence.Dto.LocationRef.CoordLocation(a.Coord);
        // Need to ensure ObjectRegistry can resolve occupant via node's contents: GetContents uses ObjectRegistry.Get(ids)
        // Already done via AddObject.

        var failed = grid.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(5,0))});
        Assert.Empty(failed);
        Assert.Equal(new List<string>{"exits"}, tracking.RemovedTags);
        Assert.Single(tracking.Added);
        var ec = tracking.Added[0];
        Assert.NotNull(ec);
        // Use reflection to get Location/Destination regardless of ExitCommand type (Objects.ExitCommand vs LoggedInExitCommand)
        var locProp = ec.GetType().GetProperty("Location");
        var dstProp = ec.GetType().GetProperty("Destination");
        var ecLoc = (Coord?)locProp!.GetValue(ec);
        var ecDst = (Coord?)dstProp!.GetValue(ec);
        Assert.Equal(new Coord("TestArea",5,0,0), ecLoc!.Value);
        Assert.Equal(new Coord("TestArea",1,0,0), ecDst!.Value);
    }

    [Fact]
    public void TestApplyMovesRebuildsExitsForNeighborContents()
    {
        using var env=GlobalTestEnv.Enter();
        var a = new Node(new Coord("TestArea",0,0,0));
        var b = new Node(new Coord("TestArea",1,0,0));
        b.Links.Add(new NodeLink("West", new Coord("TestArea",0,0,0)));
        var (grid, nh) = MakeGridWithHandler(a,b);
        var neighborOccupant = GameObject.Create("neighbor");
        neighborOccupant.InternalCmdSet = new TrackingCmdSet();
        ObjectRegistry.AddObject(neighborOccupant);
        b.AddObject(neighborOccupant);
        var tracking = (TrackingCmdSet)neighborOccupant.InternalCmdSet!;
        tracking.RemovedTags.Clear();
        tracking.Added.Clear();
        neighborOccupant.Location = new Persistence.Dto.LocationRef.CoordLocation(b.Coord);

        var failed = grid.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(7,0))});
        Assert.Empty(failed);
        Assert.Equal(new List<string>{"exits"}, tracking.RemovedTags);
        Assert.Single(tracking.Added);
        var ec = tracking.Added[0];
        Assert.NotNull(ec);
        var locProp2 = ec.GetType().GetProperty("Location");
        var dstProp2 = ec.GetType().GetProperty("Destination");
        var keyProp = ec.GetType().GetProperty("ExitName") ?? ec.GetType().GetProperty("Key") ?? ec.GetType().GetProperty("Name");
        var ecLoc2 = (Coord?)locProp2!.GetValue(ec);
        var ecDst2 = (Coord?)dstProp2!.GetValue(ec);
        var ecKey = keyProp?.GetValue(ec) as string;
        Assert.Equal(new Coord("TestArea",1,0,0), ecLoc2!.Value);
        Assert.Equal(new Coord("TestArea",7,0,0), ecDst2!.Value);
        Assert.Equal("West", ecKey);
    }

    [Fact]
    public void TestCheckMovesContextAllowsContextVacatedDestination()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var (g,_)=MakeGrid(a,b);
        var failed=g.CheckMoves(new List<((int,int),(int,int))>{((1,0),(0,0))}, context:new List<((int,int),(int,int))>{((0,0),(5,0))});
        Assert.Empty(failed);
    }

    [Fact]
    public void TestCheckMovesContextStillDeniesGenuineCollision()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var (g,_)=MakeGrid(a,b);
        Assert.Equal(new HashSet<int>{0}, g.CheckMoves(new List<((int,int),(int,int))>{((1,0),(0,0))}, context:new List<((int,int),(int,int))>{((9,9),(8,8))}));
        Assert.Equal(new HashSet<int>{0}, g.CheckMoves(new List<((int,int),(int,int))>{((1,0),(0,0))}));
    }

    [Fact]
    public void TestCheckMovesContextDoesNotMakeSourceAvailable()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var (g,_)=MakeGrid(a,b);
        Assert.Equal(new HashSet<int>{0}, g.CheckMoves(new List<((int,int),(int,int))>{((0,0),(5,5))}, context:new List<((int,int),(int,int))>{((0,0),(3,3))}));
    }

    [Fact]
    public void TestCheckMovesContextHonorsNewMoveVacatedSources()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var c=new Node(new Coord("TestArea",2,0,0)); var (g,_)=MakeGrid(a,b,c);
        var failed=g.CheckMoves(new List<((int,int),(int,int))>{((0,0),(1,0)),((1,0),(0,0))}, context:new List<((int,int),(int,int))>{((2,0),(9,9))});
        Assert.Empty(failed);
    }

    [Fact]
    public void TestApplyMovesIgnoresContextAndAppliesInOrder()
    {
        using var env=GlobalTestEnv.Enter();
        var a=new Node(new Coord("TestArea",0,0,0)); var b=new Node(new Coord("TestArea",1,0,0)); var (g,_)=MakeGrid(a,b);
        NodeHandler.SetCurrent(new NodeHandler(autoLoad:false));
        var failed=g.ApplyMoves(new List<((int,int),(int,int))>{((0,0),(1,0)),((1,0),(0,0))});
        Assert.Empty(failed); Assert.Same(a, g.GetNode((1,0))); Assert.Same(b, g.GetNode((0,0)));
    }
}
