// Port of atheriz/tests/test_build_command.py:1 (part 2 — grid, movement, links)
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedBuildTestsPart2
{
    private static (NodeHandler nh, MapHandler mh, NodeArea area, NodeGrid grid, Node start, GameObject caller) Setup()
    {
        var nh = GlobalServices.GetNodeHandler();
        var mh = GlobalServices.GetMapHandler();
        try { nh.RemoveArea("TestArea"); } catch { }
        var area = new NodeArea("TestArea");
        var grid = new NodeGrid("TestArea", 0);
        var start = new Node(new Coord("TestArea", 0, 0, 0), desc: "Start");
        grid.Nodes[(0,0)] = start;
        area.AddGrid(grid);
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        var caller = GameObject.Create("Builder", isPc:true);
        caller.PrivilegeLevel = Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(start.Coord);
        start.AddObject(caller);
        ObjectRegistry.AddObject(caller);
        return (nh,mh,area,grid,start,caller);
    }
    private static BuildArgs MakeArgs(bool n=false,bool e=false,bool s=false,bool w=false,bool u=false,bool d=false,bool x=false,bool room=false,bool road=false,bool path=false,string? desc=null,bool single=false,bool dbl=false,bool round=false,bool none=false)
        => new BuildArgs{N=n,E=e,S=s,W=w,U=u,D=d,X=x,Room=room,Road=road,Path=path,Desc=desc,Single=single,Double=dbl,Round=round,None=none};

    [Fact] public void LinkNotDuplicatedOnRebuild()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,grid,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true));
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(start.Coord);
        start.AddObject(caller);
        cmd.Run(caller, MakeArgs(n:true, room:true));
        Assert.Single(start.GetLinks().Where(l=> l.Name=="north"));
    }
    [Fact] public void BuildCallerMovedAfterSingle()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true));
        var newNode = nh.GetNode(new Coord("TestArea",0,1,0));
        Assert.Equal(newNode!.Coord, ((Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)caller.Location).Coord);
    }
    [Fact] public void BuildMultipleDirectionsCreatesBoth()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(start.Coord);
        cmd.Run(caller, MakeArgs(n:true, e:true, room:true));
        Assert.NotNull(nh.GetNode(new Coord("TestArea",0,1,0)));
        Assert.NotNull(nh.GetNode(new Coord("TestArea",1,0,0)));
    }
    [Fact] public void Build2x2Grid()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        var room3 = start;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(room3.Coord); room3.AddObject(caller);
        cmd.Run(caller, MakeArgs(n:true, room:true));
        var room1 = nh.GetNode(new Coord("TestArea",0,1,0)); Assert.NotNull(room1);
        cmd.Run(caller, MakeArgs(e:true, room:true));
        var room2 = nh.GetNode(new Coord("TestArea",1,1,0)); Assert.NotNull(room2);
        cmd.Run(caller, MakeArgs(s:true, room:true));
        var room4 = nh.GetNode(new Coord("TestArea",1,0,0)); Assert.NotNull(room4);
        cmd.Run(caller, MakeArgs(w:true, room:true));
        // links
        var r1 = room1!.GetLinks().ToDictionary(l=> l.Name, l=> l.Coord);
        Assert.Equal(new Coord("TestArea",0,0,0), r1["south"]);
        Assert.Equal(new Coord("TestArea",1,1,0), r1["east"]);
        var r3 = room3.GetLinks().ToDictionary(l=> l.Name, l=> l.Coord);
        Assert.Equal(new Coord("TestArea",0,1,0), r3["north"]);
        Assert.Equal(new Coord("TestArea",1,0,0), r3["east"]);
        foreach (var room in new[]{room1,room2!,room4!,room3})
        {
            var names = room.GetLinks().Select(l=> l.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }
    [Fact] public void Build2x2EnsureLinksWithSingle()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var room3 = start;
        var cmd = new BuildCommand();
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(room3.Coord); room3.AddObject(caller);
        cmd.Run(caller, MakeArgs(x:true, room:true, single:true));
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(room3.Coord); room3.AddObject(caller);
        cmd.Run(caller, MakeArgs(n:true, room:true, single:true));
        var room1 = nh.GetNode(new Coord("TestArea",0,1,0))!; Assert.NotNull(room1);
        cmd.Run(caller, MakeArgs(e:true, room:true, single:true));
        var room2 = nh.GetNode(new Coord("TestArea",1,1,0))!; Assert.NotNull(room2);
        cmd.Run(caller, MakeArgs(s:true, room:true, single:true));
        var room4 = nh.GetNode(new Coord("TestArea",1,0,0))!; Assert.NotNull(room4);
        var r3 = room3.GetLinks().ToDictionary(l=> l.Name, l=> l.Coord);
        Assert.True(r3.ContainsKey("north"));
        Assert.True(r3.ContainsKey("east"));
        var r4 = room4.GetLinks().ToDictionary(l=> l.Name, l=> l.Coord);
        Assert.True(r4.ContainsKey("north"));
        Assert.True(r4.ContainsKey("west"));
    }
    [Fact] public void BuildMultiDirectionDoesNotTeleport()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, e:true, s:true, room:true));
        Assert.NotNull(nh.GetNode(new Coord("TestArea",0,1,0)));
        Assert.NotNull(nh.GetNode(new Coord("TestArea",1,0,0)));
        Assert.NotNull(nh.GetNode(new Coord("TestArea",0,-1,0)));
        var loc = (caller.ResolveLocationObject() as Node)?.Coord ?? ((Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)caller.Location).Coord;
        Assert.Equal(start.Coord, loc);
    }
    [Fact] public void BuildSingleDirectionMovesCallerMultiDoesNot()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, e:true, s:true, room:true));
        var loc = (caller.ResolveLocationObject() as Node)?.Coord ?? ((Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)caller.Location).Coord;
        Assert.Equal(start.Coord, loc);
        // now single
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(start.Coord);
        start.AddObject(caller);
        cmd.Run(caller, MakeArgs(e:true, room:true));
        var loc2 = (caller.ResolveLocationObject() as Node)?.Coord ?? ((Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)caller.Location).Coord;
        Assert.Equal(new Coord("TestArea",1,0,0), loc2);
    }
}
