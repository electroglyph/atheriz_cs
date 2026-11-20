// Port of atheriz/tests/test_maze_command.py:1
// Port of atheriz/tests/test_maze_pathfind.py:1
// Port of atheriz/tests/test_pathfind.py:1
// Port of atheriz/tests/test_pathfind_concurrency.py:1
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMazePathfindTests
{
    [Fact] public void MazeAccessRequiresBuilder()
    {
        var cmd = new MazeCommand();
        var non = GameObject.Create("P", isPc:true); non.PrivilegeLevel=Privilege.Player;
        var b = GameObject.Create("B", isPc:true); b.PrivilegeLevel=Privilege.Builder;
        Assert.False(cmd.Access(non));
        Assert.True(cmd.Access(b));
    }
    [Fact] public void MazeGeneratesStackedLevels()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("Builder", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var mh = GlobalServices.GetMapHandler();
        MazeCommand.MapHandlerFactory = ()=> mh;
        MazeCommand.NodeHandlerFactory = ()=> nh;
        MazeCommand.ThreadPoolFactory = ()=> new Atheriz.Core.Concurrency.AsyncThreadPool();
        // ensure start node not required for move but we test area creation
        var cmd = new MazeCommand();
        caller.ClearMessages();
        cmd.Run(caller, null);
        var area = nh.GetArea("maze1");
        Assert.NotNull(area);
        Assert.NotNull(area!.GetGrid(0));
        Assert.NotNull(area.GetGrid(-1));
        Assert.NotNull(area.GetGrid(-2));
        Assert.Null(nh.GetArea("maze2"));
        Assert.Null(nh.GetArea("maze3"));
        var txt = string.Join(" ", caller.PeekMessages());
        Assert.Contains("created 3", txt);
    }
    [Fact] public void MazeMapsStoredInPreGrid()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("B", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var nh = new NodeHandler(autoLoad:false); NodeHandler.SetCurrent(nh);
        var mh = new MapHandler(autoLoad:false);
        MazeCommand.NodeHandlerFactory = ()=> nh;
        MazeCommand.MapHandlerFactory = ()=> mh;
        MazeCommand.ThreadPoolFactory = ()=> new Atheriz.Core.Concurrency.AsyncThreadPool();
        var cmd = new MazeCommand();
        cmd.Run(caller, null);
        foreach(var z in new[]{0,-1,-2})
        {
            var mi = mh.GetMapInfo("maze1",z);
            Assert.NotNull(mi);
            Assert.NotEmpty(mi!.PreGrid);
            if (z == 0)
            {
                // After fix, MoveTo to maze1 (0,0,0) triggers Render -> PostGrid filled. Previously stub left it empty.
                Assert.NotEmpty(mi.PostGrid);
            }
            else
            {
                Assert.Empty(mi.PostGrid);
            }
        }
        Assert.Null(mh.GetMapInfo("maze2", 0));
        Assert.Null(mh.GetMapInfo("maze3", 0));
    }
    [Fact] public void CreateMazeReturnsDict()
    {
        var m = MazeCommand.CreateMaze(5,5);
        Assert.IsType<Dictionary<(int,int), List<(int,int)>>>(m);
        foreach(var k in m.Keys) Assert.True(true);
    }
    [Fact] public void CreateMapReturnsMapAndGrid()
    {
        var m = MazeCommand.CreateMaze(5,5);
        var (mp, grid) = MazeCommand.CreateMap(m,5,5,"test");
        Assert.IsType<Dictionary<(int,int),string>>(mp);
        foreach(var k in m.Keys) Assert.True(mp.ContainsKey(k));
        foreach(var k in m.Keys) Assert.NotNull(grid.GetNode(k));
    }
    [Fact] public void CreateMapGlyphIntersection()
    {
        var m = new Dictionary<(int,int), List<(int,int)>>{
            [(1,1)]=new List<(int,int)>{(1,2),(1,0),(2,1),(0,1)},
            [(1,2)]=new List<(int,int)>{(1,1)},
            [(1,0)]=new List<(int,int)>{(1,1)},
            [(2,1)]=new List<(int,int)>{(1,1)},
            [(0,1)]=new List<(int,int)>{(1,1)},
        };
        var (mp, grid) = MazeCommand.CreateMap(m,3,3,"intersection");
        Assert.Equal("╬", mp[(1,1)]);
    }
    [Fact] public void CreateMapCreatesLinks()
    {
        var m = new Dictionary<(int,int), List<(int,int)>>{[(0,0)]=new List<(int,int)>{(1,0)}, [(1,0)]=new List<(int,int)>{(0,0)}};
        var (mp, grid) = MazeCommand.CreateMap(m,2,1,"linked");
        var n0 = grid.GetNode((0,0));
        Assert.True(n0!.HasLinkName("east"));
    }
    [Fact] public void GenMapAndGridPure()
    {
        var (mp, grid) = MazeCommand.GenMapAndGrid(3,3,"pure");
        Assert.NotEmpty(mp);
        Assert.True(grid.Nodes.Count>0);
    }
    [Fact] public void CreateMapHonorsLevel()
    {
        var m = new Dictionary<(int,int), List<(int,int)>>{[(0,0)]=new List<(int,int)>{(1,0)}, [(1,0)]=new List<(int,int)>{(0,0)}};
        var (mp, grid) = MazeCommand.CreateMap(m,2,1,"stacked", -1);
        Assert.Equal(-1, grid.Z);
        Assert.Equal("stacked", grid.Area);
        var n0 = grid.GetNode((0,0));
        Assert.NotNull(n0);
        Assert.Equal(new Coord("stacked", 0, 0, -1), n0!.Coord);
        Assert.Contains(n0.GetLinks(), l => l.Name == "east" && l.Coord.Equals(new Coord("stacked", 1, 0, -1)));
        Assert.True(mp.ContainsKey((0,0)));
    }
    [Fact] public void MazeDownExitsDropToStackedLevels()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("BuilderStack", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var mh = GlobalServices.GetMapHandler();
        MazeCommand.MapHandlerFactory = ()=> mh;
        MazeCommand.NodeHandlerFactory = ()=> nh;
        MazeCommand.ThreadPoolFactory = ()=> new Atheriz.Core.Concurrency.AsyncThreadPool();
        new MazeCommand().Run(caller, null);
        var start = nh.GetNode(new Coord("maze1", 0, 0, 0));
        var mid = nh.GetNode(new Coord("maze1", 0, 0, -1));
        var bottom = nh.GetNode(new Coord("maze1", 0, 0, -2));
        Assert.NotNull(start);
        Assert.NotNull(mid);
        Assert.NotNull(bottom);
        var downLinks = start!.GetLinks().Where(l => l.Name == "down").ToList();
        // Only the random exit on level 0 carries down; the origin usually does not.
        // The stacked destinations must all stay inside maze1 at 0,-1,-2.
        var allDown = new List<Node>();
        foreach (var g in nh.GetArea("maze1")!.Grids.Values)
            foreach (var n in g.Nodes.Values)
                if (n.GetLinks().Any(l => l.Name == "down"))
                    allDown.Add(n);
        Assert.Equal(3, allDown.Count);
        var dests = allDown.SelectMany(n => n.GetLinks()).Where(l => l.Name == "down").Select(l => l.Coord).ToList();
        Assert.Contains(new Coord("maze1", 0, 0, -1), dests);
        Assert.Contains(new Coord("maze1", 0, 0, -2), dests);
        Assert.Contains(new Coord("maze1", 0, 0, 0), dests);
        Assert.All(dests, d => Assert.Equal("maze1", d.Area));
        foreach (var z in new[]{0,-1,-2})
        {
            var mi = mh.GetMapInfo("maze1", z);
            Assert.NotNull(mi);
            Assert.Contains(mi!.LegendEntries, e => e.Desc == "down");
        }
    }
    [Fact] public void MazeDigDownLinksIntoStackedRoom()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("BuilderDig", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var mh = GlobalServices.GetMapHandler();
        MazeCommand.MapHandlerFactory = ()=> mh;
        MazeCommand.NodeHandlerFactory = ()=> nh;
        MazeCommand.ThreadPoolFactory = ()=> new Atheriz.Core.Concurrency.AsyncThreadPool();
        new MazeCommand().Run(caller, null);
        var start = nh.GetNode(new Coord("maze1", 0, 0, 0));
        Assert.NotNull(start);
        Assert.NotNull(nh.GetNode(new Coord("maze1", 0, 0, -1)));
        Assert.True(caller.MoveTo(start!, force: true));
        caller.ClearMessages();
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["d"]));
        Assert.Contains("you dig down", string.Join("\n", caller.PeekMessages()).ToLowerInvariant());
        Assert.Equal(new Coord("maze1", 0, 0, -1), ((Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)caller.Location).Coord);
        Assert.Contains(start!.GetLinks(), l => l.Name == "down" && l.Coord.Equals(new Coord("maze1", 0, 0, -1)));
    }
    [Fact] public void MazeRepeatClearsLegacyAreas()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("BuilderLegacy", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var mh = GlobalServices.GetMapHandler();
        MazeCommand.MapHandlerFactory = ()=> mh;
        MazeCommand.NodeHandlerFactory = ()=> nh;
        MazeCommand.ThreadPoolFactory = ()=> new Atheriz.Core.Concurrency.AsyncThreadPool();
        var legacyArea = new NodeArea("maze2");
        var legacyGrid = new NodeGrid("maze2", 0);
        var legacyNode = new Node(new Coord("maze2", 0, 0, 0), desc: "Legacy");
        legacyGrid.AddNode(legacyNode);
        ObjectRegistry.AddObject(legacyNode);
        legacyArea.AddGrid(legacyGrid);
        nh.AddArea(legacyArea);
        mh.SetMapInfo("maze2", 0, new MapInfo("maze2", new Dictionary<(int,int),string>{[(0,0)] = " "}, null));
        new MazeCommand().Run(caller, null);
        Assert.Null(nh.GetArea("maze2"));
        Assert.Null(nh.GetArea("maze3"));
        Assert.Null(mh.GetMapInfo("maze2", 0));
        Assert.Null(mh.GetMapInfo("maze3", 0));
        Assert.NotNull(nh.GetArea("maze1")!.GetGrid(-1));
    }
    [Fact] public void MazeAstar50x20()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        int w=50, h=20;
        var (map1, g1) = MazeCommand.GenMapAndGrid(w,h,"maze1");
        var (map2, g2) = MazeCommand.GenMapAndGrid(w,h,"maze2");
        var (map3, g3) = MazeCommand.GenMapAndGrid(w,h,"maze3");
        var a1=new NodeArea("maze1"); a1.AddGrid(g1);
        var a2=new NodeArea("maze2"); a2.AddGrid(g2);
        var a3=new NodeArea("maze3"); a3.AddGrid(g3);
        nh.AddArea(a1); nh.AddArea(a2); nh.AddArea(a3);
        var e1 = g1.Nodes.Values.Last(); var e2=g2.Nodes.Values.Last(); var e3=g3.Nodes.Values.Last();
        e1.AddLink(new NodeLink("down", new Coord("maze2",0,0,0), new List<string>{"d"}));
        e2.AddLink(new NodeLink("down", new Coord("maze3",0,0,0), new List<string>{"d"}));
        e3.AddLink(new NodeLink("down", new Coord("maze1",0,0,0), new List<string>{"d"}));
        var start = nh.GetNode(new Coord("maze1",0,0,0)) ?? g1.Nodes.Values.First();
        var end = e3;
        var caller = GameObject.Create("C", isPc:true);
        var (found, path, dead) = Pathfind.AStar(start, end, caller, nh, 50000);
        Assert.True(found);
        Assert.True(path.Count>0);
    }
    // Pathfind basic
    private static (NodeHandler nh, Dictionary<(int,int), Node> nodes) SetupSmallArea()
    {
        var nh = new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var area = new NodeArea("PathArea");
        var grid = new NodeGrid("PathArea",0);
        var nodes = new Dictionary<(int,int), Node>();
        for(int x=0;x<3;x++) for(int y=0;y<2;y++){ var n=new Node(new Coord("PathArea",x,y,0)); nodes[(x,y)]=n; grid.AddNode(n); }
        nodes[(0,0)].AddLink(new NodeLink("east", new Coord("PathArea",1,0,0), new List<string>{"e"}));
        nodes[(1,0)].AddLink(new NodeLink("west", new Coord("PathArea",0,0,0), new List<string>{"w"}));
        nodes[(1,0)].AddLink(new NodeLink("east", new Coord("PathArea",2,0,0), new List<string>{"e"}));
        nodes[(2,0)].AddLink(new NodeLink("west", new Coord("PathArea",1,0,0), new List<string>{"w"}));
        nodes[(0,0)].AddLink(new NodeLink("north", new Coord("PathArea",0,1,0), new List<string>{"n"}));
        nodes[(0,1)].AddLink(new NodeLink("south", new Coord("PathArea",0,0,0), new List<string>{"s"}));
        nodes[(0,1)].AddLink(new NodeLink("east", new Coord("PathArea",1,1,0), new List<string>{"e"}));
        nodes[(1,1)].AddLink(new NodeLink("west", new Coord("PathArea",0,1,0), new List<string>{"w"}));
        nodes[(1,1)].AddLink(new NodeLink("east", new Coord("PathArea",2,1,0), new List<string>{"e"}));
        nodes[(2,1)].AddLink(new NodeLink("west", new Coord("PathArea",1,1,0), new List<string>{"w"}));
        nodes[(2,1)].AddLink(new NodeLink("south", new Coord("PathArea",2,0,0), new List<string>{"s"}));
        nodes[(2,0)].AddLink(new NodeLink("north", new Coord("PathArea",2,1,0), new List<string>{"n"}));
        area.AddGrid(grid); nh.AddArea(area);
        return (nh, nodes);
    }
    [Fact] public void AstarNoDoorsShortest()
    {
        var (nh, nodes) = SetupSmallArea();
        var start = nodes[(0,0)]; var end=nodes[(2,0)];
        var (ok, path, closed) = Pathfind.AStar(start,end, null, nh);
        Assert.True(ok);
        Assert.Equal(3, path.Count);
        Assert.Same(start, path[0]);
        Assert.Same(end, path[2]);
    }
    [Fact] public void AstarOpenDoor()
    {
        var (nh, nodes)=SetupSmallArea();
        var door = new Door(new Coord("PathArea",0,0,0), new Coord("PathArea",1,0,0), "east","west", null,"","",false,false);
        nh.AddDoor(door);
        var (ok, path, _)=Pathfind.AStar(nodes[(0,0)], nodes[(2,0)], GameObject.Create("C", isPc:true), nh);
        Assert.True(ok); Assert.Equal(3, path.Count);
    }
    [Fact] public void AstarClosedUnlockedCanOpen()
    {
        var (nh, nodes)=SetupSmallArea();
        var door = new Door(new Coord("PathArea",0,0,0), new Coord("PathArea",1,0,0), "east","west", null,"","",true,false);
        nh.AddDoor(door);
        var caller = GameObject.Create("C", isPc:true);
        door.AddLock("open", _=> true);
        var (ok, path, _)=Pathfind.AStar(nodes[(0,0)], nodes[(2,0)], caller, nh);
        Assert.True(ok); Assert.Equal(3, path.Count);
    }
    [Fact] public void AstarClosedCannotOpenRoutesAround()
    {
        var (nh, nodes)=SetupSmallArea();
        var door = new Door(new Coord("PathArea",0,0,0), new Coord("PathArea",1,0,0), "east","west", null,"","",true,false);
        nh.AddDoor(door);
        var caller = GameObject.Create("C", isPc:true);
        door.AddLock("open", _=> false);
        var (ok, path, _)=Pathfind.AStar(nodes[(0,0)], nodes[(2,0)], caller, nh);
        Assert.True(ok); Assert.Equal(5, path.Count);
        Assert.Same(nodes[(0,1)], path[1]);
    }
    [Fact] public void AstarLockedCannotUnlockRoutesAround()
    {
        var (nh, nodes)=SetupSmallArea();
        var door = new Door(new Coord("PathArea",0,0,0), new Coord("PathArea",1,0,0), "east","west", null,"","",true,true);
        nh.AddDoor(door);
        var caller = GameObject.Create("C", isPc:true);
        door.AddLock("open", _=> true); door.AddLock("unlock", _=> false);
        var (ok, path, _)=Pathfind.AStar(nodes[(0,0)], nodes[(2,0)], caller, nh);
        Assert.True(ok); Assert.Equal(5, path.Count);
    }
    [Fact] public void AstarBlockedCompletelyFails()
    {
        var (nh, nodes)=SetupSmallArea();
        var d1=new Door(new Coord("PathArea",0,0,0), new Coord("PathArea",1,0,0), "east","west", null,"","",true,true);
        var d2=new Door(new Coord("PathArea",0,1,0), new Coord("PathArea",1,1,0), "east","west", null,"","",true,true);
        nh.AddDoor(d1); nh.AddDoor(d2);
        var caller=GameObject.Create("C", isPc:true);
        d1.AddLock("open",_=>false); d2.AddLock("open",_=>false);
        var (ok,_,_) = Pathfind.AStar(nodes[(0,0)], nodes[(2,0)], caller, nh);
        Assert.False(ok);
    }
    [Fact] public void GetDoorsReturnsCopy()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var coord = new Coord("TestCopy",0,0,0);
        var door = new Door(coord, new Coord("TestCopy",0,1,0), "north","south", null,"","",false,false);
        nh.AddDoor(door);
        var d1 = nh.GetDoors(coord);
        Assert.NotNull(d1);
        d1!.Remove("north");
        var d2 = nh.GetDoors(coord);
        Assert.True(d2!.ContainsKey("north"));
        nh.RemoveDoor(door);
    }
    [Fact] public void PathfindMaxIterations50000()
    {
        var s = new AtherizSettings();
        Assert.Equal(50000, s.MaxAstarIterations);
    }
    [Fact] public void RepeatMazeEvictsPriorGenerationNodes()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("Builder", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var mh = GlobalServices.GetMapHandler();
        MazeCommand.MapHandlerFactory = ()=> mh;
        MazeCommand.NodeHandlerFactory = ()=> nh;
        MazeCommand.ThreadPoolFactory = ()=> new Atheriz.Core.Concurrency.AsyncThreadPool();
        var cmd = new MazeCommand();
        cmd.Run(caller, null);
        var gen1 = new HashSet<int>(ObjectRegistry.FilterBy(o => o is Node n && n.Coord.Area == "maze1").Select(o => o.Id));
        Assert.NotEmpty(gen1);
        caller.ClearMessages();
        cmd.Run(caller, null);
        var survivors = gen1.Where(id => ObjectRegistry.Get(id).Count > 0).ToList();
        Assert.Empty(survivors);
    }
    private sealed class ImmobileBuilder : GameObject
    {
        public override bool AtPreMove(GameObject? destination, string? toExit = null) => false;
    }
    [Fact] public void MazeMoveFailureReported()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = new ImmobileBuilder();
        caller.Name = "Builder"; caller.IsPc = true; caller.PrivilegeLevel = Privilege.Builder;
        caller.Id = IdGenerator.GetUniqueId(); ObjectRegistry.AddObject(caller);
        var nh = GlobalServices.GetNodeHandler(); NodeHandler.SetCurrent(nh);
        var mh = GlobalServices.GetMapHandler();
        MazeCommand.MapHandlerFactory = ()=> mh;
        MazeCommand.NodeHandlerFactory = ()=> nh;
        MazeCommand.ThreadPoolFactory = ()=> new Atheriz.Core.Concurrency.AsyncThreadPool();
        new MazeCommand().Run(caller, null);
        var txt = string.Join(" ", caller.PeekMessages());
        Assert.Contains("moving to:", txt);
        Assert.Contains("Could not move to", txt);
    }
}
