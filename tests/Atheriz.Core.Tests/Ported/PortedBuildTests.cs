// Port of atheriz/tests/test_build.py:1
// Port of atheriz/tests/test_build_command.py:1
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedBuildTests
{
    private static (NodeHandler nh, MapHandler mh, NodeArea area, NodeGrid grid, Node start, GameObject caller) Setup()
    {
        var nh = GlobalServices.GetNodeHandler();
        var mh = GlobalServices.GetMapHandler();
        // Ensure clean: clear previous TestArea
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
        return (nh, mh, area, grid, start, caller);
    }
    private static BuildArgs MakeArgs(bool n=false,bool e=false,bool s=false,bool w=false,bool u=false,bool d=false,bool x=false,bool room=false,bool road=false,bool path=false,string? desc=null,bool single=false,bool dbl=false,bool round=false,bool none=false)
        => new BuildArgs{N=n,E=e,S=s,W=w,U=u,D=d,X=x,Room=room,Road=road,Path=path,Desc=desc,Single=single,Double=dbl,Round=round,None=none};

    [Fact] public void BuildCommandAttributes()
    {
        var cmd = new BuildCommand();
        Assert.Equal("build", cmd.Key);
        Assert.Equal("Building", cmd.Category);
    }
    [Fact] public void BuildParserSetup()
    {
        var cmd = new BuildCommand();
        var parser = cmd.Parser;
        Assert.NotNull(parser);
        var parsed = parser!.ParseArgs(new[]{"-n","--room","--single"});
        Assert.True(parsed.GetBool("n"));
        Assert.True(parsed.GetBool("room"));
        Assert.True(parsed.GetBool("single"));
    }
    [Fact] public void BuildNoLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,_,_,_,_,caller) = Setup();
        caller.Location = Atheriz.Core.Persistence.Dto.LocationRef.NullLocation.Instance;
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true));
        var txt = string.Join(" ", caller.PeekMessages());
        Assert.Contains("valid location", txt.ToLowerInvariant());
    }
    [Fact] public void BuildNoArgsShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,_,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        caller.ClearMessages();
        cmd.Run(caller, MakeArgs());
        Assert.True(caller.PeekMessages().Count>0);
    }
    [Fact] public void BuildAccessDeniedForNonBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BuildCommand();
        var non = GameObject.Create("NonBuilder", isPc:true); non.PrivilegeLevel=Privilege.Player;
        Assert.False(cmd.Access(non));
    }
    [Fact] public void BuildAccessGrantedForBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BuildCommand();
        var builder = GameObject.Create("Builder2", isPc:true); builder.PrivilegeLevel=Privilege.Builder;
        Assert.True(cmd.Access(builder));
    }
    [Fact] public void BuildXAloneBuildsHere()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        caller.ClearMessages();
        cmd.Run(caller, MakeArgs(x:true));
        // caller should have moved to start (here)
        var loc = caller.ResolveLocationObject() as Node;
        Assert.NotNull(loc);
        Assert.Equal(start.Coord, loc!.Coord);
    }
    [Fact] public void BuildXWithRoundBuildsHere()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(x:true, round:true));
        var loc = caller.ResolveLocationObject() as Node;
        Assert.Equal(start.Coord, loc!.Coord);
    }
    [Fact] public void BuildRoomNorthLinksBothWays()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true));
        var newNode = nh.GetNode(new Coord("TestArea",0,1,0));
        Assert.NotNull(newNode);
        Assert.Contains(start.GetLinks(), l=> l.Name=="north" && l.Coord.Equals(new Coord("TestArea",0,1,0)));
        Assert.Contains(newNode!.GetLinks(), l=> l.Name=="south" && l.Coord.Equals(new Coord("TestArea",0,0,0)));
        Assert.Equal(newNode.Coord, ((Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)caller.Location).Coord);
    }
    [Fact] public void BuildRoomSouth()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(s:true, room:true));
        var n = nh.GetNode(new Coord("TestArea",0,-1,0));
        Assert.NotNull(n);
        Assert.Contains(start.GetLinks(), l=> l.Name=="south");
    }
    [Fact] public void BuildRoomEast()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(e:true, room:true));
        Assert.NotNull(nh.GetNode(new Coord("TestArea",1,0,0)));
        Assert.Contains(start.GetLinks(), l=> l.Name=="east");
    }
    [Fact] public void BuildRoomWest()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(w:true, room:true));
        Assert.NotNull(nh.GetNode(new Coord("TestArea",-1,0,0)));
    }
    [Fact] public void BuildRoomUp()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(u:true, room:true));
        var newNode = nh.GetNode(new Coord("TestArea",0,0,1));
        Assert.NotNull(newNode);
        Assert.Contains(start.GetLinks(), l=> l.Name=="up");
        Assert.Contains(newNode!.GetLinks(), l=> l.Name=="down");
        Assert.Equal(new Coord("TestArea",0,0,1), newNode!.Coord);
        var upLink = start.GetLinks().First(l=> l.Name=="up");
        Assert.Equal(new Coord("TestArea",0,0,1), upLink.Coord);
        var downLink = newNode.GetLinks().First(l=> l.Name=="down");
        Assert.Equal(new Coord("TestArea",0,0,0), downLink.Coord);
    }
    [Fact] public void BuildRoomDown()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(d:true, room:true));
        var newNode = nh.GetNode(new Coord("TestArea",0,0,-1));
        Assert.NotNull(newNode);
        Assert.Contains(start.GetLinks(), l=> l.Name=="down");
        Assert.Contains(newNode!.GetLinks(), l=> l.Name=="up");
        Assert.Equal(new Coord("TestArea",0,0,-1), newNode!.Coord);
        var downLink = start.GetLinks().First(l=> l.Name=="down");
        Assert.Equal(new Coord("TestArea",0,0,-1), downLink.Coord);
        var upLink = newNode.GetLinks().First(l=> l.Name=="up");
        Assert.Equal(new Coord("TestArea",0,0,0), upLink.Coord);
    }
    [Fact] public void BuildRoomHereNoLinks()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(x:true, room:true, desc:"Updated room"));
        Assert.Equal("Updated room", start.Desc);
        Assert.Empty(start.GetLinks());
    }
    [Fact] public void BuildWithDesc()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true, desc:"A magical forest"));
        var n = nh.GetNode(new Coord("TestArea",0,1,0));
        Assert.Equal("A magical forest", n!.Desc);
    }
    [Fact] public void SetDescOnlyUpdatesCurrent()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,_,_,_,start,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(desc:"New description"));
        Assert.Equal("New description", start.Desc);
        Assert.Contains(caller.PeekMessages(), m=> m.Contains("Updated"));
    }
    [Fact] public void BuildExistingNodeUpdatesDesc()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,_,area,grid,_,caller) = Setup();
        var north = new Node(new Coord("TestArea",0,1,0), desc:"Old desc");
        grid.Nodes[(0,1)] = north;
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true, desc:"New desc"));
        Assert.Equal("New desc", north.Desc);
        Assert.Contains(caller.PeekMessages(), m => m.Contains("Updating"));
    }
    [Fact] public void BuildRoadPlaceholder()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,mh,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, road:true));
        var mi = mh.GetMapInfo("TestArea",0);
        Assert.NotNull(mi);
        var expected = AtherizSettings.Global.RoadPlaceholder;
        // also verify via fresh settings instance matches global (faithful to settings.ROAD_PLACEHOLDER)
        Assert.Equal(expected, new AtherizSettings().RoadPlaceholder);
        Assert.Equal(expected, mi!.PreGrid[(0,1)]);
    }
    [Fact] public void BuildPathPlaceholder()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,mh,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, path:true));
        var mi = mh.GetMapInfo("TestArea",0);
        Assert.NotNull(mi);
        var expected = AtherizSettings.Global.PathPlaceholder;
        Assert.Equal(expected, new AtherizSettings().PathPlaceholder);
        Assert.Equal(expected, mi!.PreGrid[(0,1)]);
    }
    [Fact] public void BuildDefaultModeIsRoom()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,mh,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true));
        var mi = mh.GetMapInfo("TestArea",0);
        var expected = AtherizSettings.Global.RoomPlaceholder;
        Assert.Equal(expected, new AtherizSettings().RoomPlaceholder);
        Assert.Equal(expected, mi!.PreGrid[(0,1)]);
    }
    [Fact] public void BuildWithSingleWalls()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,mh,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true, single:true));
        var expected = AtherizSettings.Global.RoomPlaceholder;
        Assert.Equal(expected, mh.GetMapInfo("TestArea",0)!.PreGrid[(0,1)]);
    }
    [Fact] public void BuildWithDoubleWalls()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,mh,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true, dbl:true));
        var expected = AtherizSettings.Global.RoomPlaceholder;
        Assert.Equal(expected, mh.GetMapInfo("TestArea",0)!.PreGrid[(0,1)]);
    }
    [Fact] public void BuildWithRoundWalls()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,mh,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true, round:true));
        var expected = AtherizSettings.Global.RoomPlaceholder;
        Assert.Equal(expected, mh.GetMapInfo("TestArea",0)!.PreGrid[(0,1)]);
    }
    [Fact] public void BuildNoWallsNoPlaceholder()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,mh,_,_,_,caller) = Setup();
        var cmd = new BuildCommand();
        cmd.Run(caller, MakeArgs(n:true, room:true, none:true));
        var mi = mh.GetMapInfo("TestArea",0);
        Assert.False(mi!.PreGrid.ContainsKey((0,1)));
    }
    [Fact] public void BuildDirectionsConstant()
    {
        Assert.Equal((0,1,0,"north","south"), BuildCommand.Directions["n"]);
        Assert.Equal((0,-1,0,"south","north"), BuildCommand.Directions["s"]);
        Assert.Equal((1,0,0,"east","west"), BuildCommand.Directions["e"]);
        Assert.Equal((-1,0,0,"west","east"), BuildCommand.Directions["w"]);
        Assert.Equal((0,0,1,"up","down"), BuildCommand.Directions["u"]);
        Assert.Equal((0,0,-1,"down","up"), BuildCommand.Directions["d"]);
        Assert.Equal((0,0,0,"here","here"), BuildCommand.Directions["x"]);
    }
    [Fact] public void HasLink()
    {
        using var env = GlobalTestEnv.Enter();
        var (_,_,_,_,start,_) = Setup();
        var cmd = new BuildCommand();
        Assert.False(cmd.HasLink(start, "north"));
        start.AddLink(new NodeLink("north", new Coord("TestArea",0,1,0), new List<string>{"n"}));
        Assert.True(cmd.HasLink(start, "north"));
        Assert.False(cmd.HasLink(start, "south"));
    }
    [Fact] public void GetAlias()
    {
        var cmd = new BuildCommand();
        Assert.Equal("n", cmd.GetAlias("north"));
        Assert.Equal("s", cmd.GetAlias("south"));
        Assert.Equal("e", cmd.GetAlias("east"));
        Assert.Equal("w", cmd.GetAlias("west"));
        Assert.Equal("u", cmd.GetAlias("up"));
        Assert.Equal("d", cmd.GetAlias("down"));
        Assert.Equal("", cmd.GetAlias("unknown"));
    }
}
