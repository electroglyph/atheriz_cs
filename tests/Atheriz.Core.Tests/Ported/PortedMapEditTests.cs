// Port of atheriz/tests/test_mapedit.py:1 — Part1 grant/consume + Draw
using System.Threading;
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using System.Collections.Generic;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMapEditTests
{
    private sealed class FakeConn : BaseConnection
    {
        public FakeConn(string host="10.0.0.1") : base("test") { ClientHost = host; Session.Puppet = CreateBuilder(); }
        private static GameObject CreateBuilder() { var b = GameObject.Create("B", isPc:true); b.PrivilegeLevel = Privilege.Builder; return b; }
        public override void SendCommand(string cmd, List<object?>? args=null, Dictionary<string,object?>? kwargs=null) { lock(Sent) Sent.Add((cmd, args??new(), kwargs??new())); }
        public override void Close() {}
        public List<(string Cmd, List<object?> Args, Dictionary<string,object?> Kw)> Sent = new();
    }
    private static void Reset() { MapEdit.ResetForTesting(); InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler(); InputFuncs.NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler(); }
    private static MapInfo MakeMi(Dictionary<(int,int),string>? grid=null)
    {
        var mi = new MapInfo("TestArea");
        if(grid!=null) foreach(var kv in grid) mi.PreGrid[kv.Key]=kv.Value;
        return mi;
    }

    [Fact] public void GrantReturnsUniqueKeys()
    {
        Reset();
        var k1 = MapEdit.Grant("10.0.0.1","TestArea",0);
        var k2 = MapEdit.Grant("10.0.0.1","TestArea",0);
        Assert.NotNull(k1); Assert.NotNull(k2); Assert.NotEqual(k1,k2); Assert.True(k1.Length>16);
        Reset();
    }
    [Fact] public void ConsumeUnknownKey()
    {
        Reset();
        var r = MapEdit.Consume("bogus","10.0.0.1",0);
        Assert.Equal(MapEditStatus.Reject, r.Status);
        Assert.Equal("unknown_key", r.Reason);
    }
    [Fact] public void ConsumeHandshakeRotatesKey()
    {
        Reset();
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var r = MapEdit.Consume(key,"10.0.0.1",0);
        Assert.Equal(MapEditStatus.Processed, r.Status);
        Assert.NotEqual(key, r.NewKey);
        Assert.Equal(0, r.Chain!.Seq);
        Assert.Equal("TestArea", r.Chain.Area);
        Assert.Equal(0, r.Chain.Z);
    }
    [Fact] public void ConsumeEditThenRetry()
    {
        Reset();
        var k = MapEdit.Grant("10.0.0.1","TestArea",0);
        var h = MapEdit.Consume(k,"10.0.0.1",0);
        Assert.Equal(MapEditStatus.Processed, h.Status);
        var keyAfterHandshake = h.NewKey!;
        var edit = MapEdit.Consume(keyAfterHandshake,"10.0.0.1",1);
        Assert.Equal(MapEditStatus.Processed, edit.Status);
        Assert.Equal(1, edit.Chain!.Seq);
        var keyAfterEdit = edit.NewKey!;
        var retry = MapEdit.Consume(keyAfterHandshake,"10.0.0.1",1);
        Assert.Equal(MapEditStatus.Retry, retry.Status);
        Assert.Equal(keyAfterEdit, retry.NewKey);
        Assert.Equal(1, retry.Chain!.Seq);
    }
    [Fact] public void ConsumeReplay()
    {
        Reset();
        var k = MapEdit.Grant("10.0.0.1","TestArea",0);
        var h = MapEdit.Consume(k,"10.0.0.1",0);
        var cur = MapEdit.Consume(h.NewKey!,"10.0.0.1",0);
        Assert.Equal(MapEditStatus.Reject, cur.Status);
        Assert.Equal("replay", cur.Reason);
    }
    [Fact] public void ConsumeGap()
    {
        Reset();
        var k = MapEdit.Grant("10.0.0.1","TestArea",0);
        var h = MapEdit.Consume(k,"10.0.0.1",0);
        var r = MapEdit.Consume(h.NewKey!,"10.0.0.1",5);
        Assert.Equal(MapEditStatus.Reject, r.Status);
        Assert.Equal("gap", r.Reason);
    }
    [Fact] public void ConsumeWrongIp()
    {
        Reset();
        var k = MapEdit.Grant("10.0.0.1","TestArea",0);
        var r = MapEdit.Consume(k,"10.0.0.2",0);
        Assert.Equal(MapEditStatus.Reject, r.Status);
        Assert.Equal("ip", r.Reason);
    }
    [Fact] public void ConsumeOldKeyAfterRotationIsStale()
    {
        Reset();
        var k = MapEdit.Grant("10.0.0.1","TestArea",0);
        var h = MapEdit.Consume(k,"10.0.0.1",0);
        MapEdit.Consume(h.NewKey!,"10.0.0.1",1);
        var r = MapEdit.Consume(k,"10.0.0.1",0);
        Assert.Equal(MapEditStatus.Reject, r.Status);
        Assert.Equal("unknown_key", r.Reason);
    }
    [Fact] public void ConsumePreviousKeyWithWrongSeqIsReplay()
    {
        Reset();
        var k = MapEdit.Grant("10.0.0.1","TestArea",0);
        var h = MapEdit.Consume(k,"10.0.0.1",0);
        var edit = MapEdit.Consume(h.NewKey!,"10.0.0.1",1);
        Assert.Equal(MapEditStatus.Processed, edit.Status);
        Assert.Equal(1, edit.Chain!.Seq);
        var wrong = MapEdit.Consume(h.NewKey!,"10.0.0.1",5);
        Assert.Equal(MapEditStatus.Reject, wrong.Status);
        Assert.Equal("replay", wrong.Reason);
        var retry = MapEdit.Consume(h.NewKey!,"10.0.0.1",1);
        Assert.Equal(MapEditStatus.Retry, retry.Status);
        Assert.Equal(edit.NewKey, retry.NewKey);
    }

    [Fact] public void AccessBuilder()
    {
        var caller = GameObject.Create("B", isPc:true); caller.PrivilegeLevel = Privilege.Builder;
        Assert.True(new Atheriz.Core.Commands.LoggedIn.DrawCommand().Access(caller));
    }
    [Fact] public void AccessNonBuilder()
    {
        var caller = GameObject.Create("P", isPc:true); caller.PrivilegeLevel = Privilege.Player;
        Assert.False(new Atheriz.Core.Commands.LoggedIn.DrawCommand().Access(caller));
    }

    [Fact] public void RunSendsLaunchDrawWithPayload()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(new Dictionary<(int,int),string>{[(0,0)]="X",[(5,-2)]="Y"});
        mh.SetMapInfo("TestArea",0,mi);
        var node = new Node(new Coord("TestArea",3,7,0));
        // No NodeArea needed for this test - rooms should be empty
        var nh = new NodeHandler(autoLoad:false); NodeHandler.SetCurrent(nh);
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        caller.Session = new Session(conn); caller.Session.Connection = conn; conn.Session.Puppet = caller;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        Assert.Single(conn.Sent.Where(s=>s.Cmd=="launch_draw"));
        var args = conn.Sent.First(s=>s.Cmd=="launch_draw").Args;
        Assert.Equal(2, args.Count);
        var key = args[0] as string;
        Assert.NotNull(key); Assert.True(key!.Length>0);
        var payload = args[1] as Dictionary<string,object?>;
        Assert.NotNull(payload);
        Assert.Equal("TestArea", payload!["area"]);
        Assert.Equal(0, payload!["z"]);
        var gridList = payload!["grid"] as List<List<object?>>;
        Assert.NotNull(gridList);
        var set = new HashSet<(int,int,string)>(gridList!.Select(l=> ((int)l[0]!,(int)l[1]!, (string)l[2]!)));
        Assert.Contains((0,0,"X"), set); Assert.Contains((5,-2,"Y"), set);
        var rooms = payload!["rooms"] as List<Dictionary<string,object?>>;
        Assert.NotNull(rooms); Assert.Empty(rooms!);
        Assert.Contains("Opening AtheriZ Draw in a new tab.", caller.PeekMessages());
        var chain = MapEdit.Consume(key!,"10.0.0.1",0);
        Assert.Equal(MapEditStatus.Processed, chain.Status);
    }

    [Fact] public void RunSendsRoomData()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mi = MakeMi(new Dictionary<(int,int),string>{[(0,0)]=new AtherizSettings().RoomPlaceholder,[(5,-2)]="Y"});
        var mh = GlobalServices.GetMapHandler(); mh.SetMapInfo("TestArea",0,mi);
        var room = new Node(new Coord("TestArea",0,0,0)); room.Desc="A dusty hall.";
        room.AddLink(new NodeLink("North", new Coord("TestArea",0,1,0), new List<string>{"n"}));
        room.AddLink(new NodeLink("East", new Coord("TestArea",1,0,0)));
        room.AddLink(new NodeLink("Broken", default)); // coord None -> default Coord (empty area)
        // Make broken link's Coord default to represent None – we add but Draw should skip where Coord.Area==""?
        // In C# Draw skips if link.Coord.Equals(default) -> we need to ensure broken is skipped
        // We'll set broken to have empty area, but our AddLink will still consider it; Draw checks default equality – we need Coord with empty area to be considered default
        // For test, we can just not add broken, or make it null-like by not adding
        // Remove last link and re-add with empty check: actually NodeLink with default Coord will be skipped
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(0,0)]=room;
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        var callerNode = new Node(new Coord("TestArea",3,7,0));
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(callerNode.Coord);
        // callerNode not added to grid, so only room at (0,0) is in grid
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        // Need to patch handlers to use our nh/mh
        InputFuncs.MapHandlerFactory = () => mh;
        InputFuncs.NodeHandlerFactory = () => nh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = conn.Sent.First(s=>s.Cmd=="launch_draw").Args[1] as Dictionary<string,object?>;
        var rooms = payload!["rooms"] as List<Dictionary<string,object?>>;
        Assert.Single(rooms!);
        var r = rooms![0];
        Assert.Equal(0, r["x"]); Assert.Equal(0, r["y"]); Assert.Equal("A dusty hall.", r["desc"]);
        var exits = r["exits"] as List<Dictionary<string,object?>>;
        Assert.Equal(2, exits!.Count);
        Assert.Equal("North", exits![0]["name"]); Assert.Equal(new List<string>{"n"}, exits![0]["aliases"]);
        // East should have empty aliases
        Assert.Equal("East", exits![1]["name"]);
    }

    [Fact] public void RunSendsRoomsWithoutGlyphs()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mi = MakeMi(new Dictionary<(int,int),string>{[(0,0)]="│",[(2,0)]="│"});
        var mh = GlobalServices.GetMapHandler(); mh.SetMapInfo("TestArea",0,mi);
        var wallNode = new Node(new Coord("TestArea",0,0,0));
        var interior = new Node(new Coord("TestArea",1,0,0)); interior.Desc="A cozy room.";
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(0,0)]=wallNode; grid.Nodes[(1,0)]=interior;
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = GlobalServices.GetNodeHandler(); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        var callerNode = new Node(new Coord("TestArea",1,0,0)); // caller at interior
        // Ensure caller node is not same as interior? Use separate
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(new Coord("TestArea",1,0,0));
        // Need to ensure caller location node exists – use interior
        interior.AddObject(caller);
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        InputFuncs.MapHandlerFactory = () => mh;
        InputFuncs.NodeHandlerFactory = () => nh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = conn.Sent.First(s=>s.Cmd=="launch_draw").Args[1] as Dictionary<string,object?>;
        var gridList = payload!["grid"] as List<List<object?>>;
        Assert.Equal(2, gridList!.Count);
        var rooms = payload!["rooms"] as List<Dictionary<string,object?>>;
        Assert.Equal(2, rooms!.Count);
        var coords = rooms!.Select(r=> ((int)r["x"]!,(int)r["y"]!)).ToList();
        Assert.Contains((0,0), coords); Assert.Contains((1,0), coords);
        var interiorRoom = rooms!.First(r=> (int)r["x"]! ==1);
        Assert.Equal("A cozy room.", interiorRoom["desc"]);
    }

    [Fact] public void RunSendsRenderedSymbols()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var s = new AtherizSettings();
        var mi = MakeMi(new Dictionary<(int,int),string>{[(0,0)]=s.SingleWallPlaceholder});
        var mh = GlobalServices.GetMapHandler(); mh.SetMapInfo("TestArea",0,mi);
        var node = new Node(new Coord("TestArea",3,7,0));
        var nh = GlobalServices.GetNodeHandler(); var area = new NodeArea("TestArea"); var grid=new NodeGrid("TestArea",0); area.AddGrid(grid); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        grid.Nodes[(3,7)]=node;
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        InputFuncs.MapHandlerFactory = () => mh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = conn.Sent.First(s=>s.Cmd=="launch_draw").Args[1] as Dictionary<string,object?>;
        var gridList = payload!["grid"] as List<List<object?>>;
        Assert.Single(gridList!);
        Assert.Equal("─", gridList![0][2] as string);
    }

    [Fact] public void RunPreservesPostGridWhenPreGridEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mi = new MapInfo("TestArea"); mi.PostGrid[(0,0)]="╬"; mi.PostGrid[(1,0)]="═";
        var mh = GlobalServices.GetMapHandler(); mh.SetMapInfo("TestArea",0,mi);
        var node = new Node(new Coord("TestArea",3,7,0));
        var nh = GlobalServices.GetNodeHandler(); var area=new NodeArea("TestArea"); var grid=new NodeGrid("TestArea",0); area.AddGrid(grid); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        grid.Nodes[(3,7)]=node;
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        InputFuncs.MapHandlerFactory = () => mh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = conn.Sent.First(s=>s.Cmd=="launch_draw").Args[1] as Dictionary<string,object?>;
        var gridList = payload!["grid"] as List<List<object?>>;
        var set = new HashSet<(int,int,string)>(gridList!.Select(l=> ((int)l[0]!,(int)l[1]!, (string)l[2]!)));
        Assert.Contains((0,0,"╬"), set); Assert.Contains((1,0,"═"), set);
        Assert.Equal("╬", mi.PostGrid[(0,0)]);
    }

    [Fact] public void RunCreatesMapinfoWhenMissing()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        // Ensure no mapinfo
        Assert.Null(mh.GetMapInfo("TestArea",0));
        var node = new Node(new Coord("TestArea",0,0,0));
        var nh = GlobalServices.GetNodeHandler(); var area=new NodeArea("TestArea"); var grid=new NodeGrid("TestArea",0); area.AddGrid(grid); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        grid.Nodes[(0,0)]=node;
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        InputFuncs.MapHandlerFactory = () => mh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        Assert.NotNull(mh.GetMapInfo("TestArea",0));
        Assert.Empty(mh.GetMapInfo("TestArea",0)!.PreGrid);
        Assert.Single(conn.Sent.Where(s=>s.Cmd=="launch_draw"));
    }

    [Fact] public void RunNoLocation()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var conn = new FakeConn();
        var caller = GameObject.Create("C", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = Atheriz.Core.Persistence.Dto.LocationRef.NullLocation.Instance;
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        Assert.Empty(conn.Sent.Where(s=>s.Cmd=="launch_draw"));
        Assert.Contains(caller.PeekMessages(), m=> m.Contains("You must be in a valid location to open the map editor."));
    }

    [Fact] public void MapEditHandlesMissingSessionGracefully()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var caller = GameObject.Create("Builder", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Session = null!;
        var node = new Node(new Coord("TestArea",0,0,0));
        var nh = GlobalServices.GetNodeHandler(); var area=new NodeArea("TestArea"); var grid=new NodeGrid("TestArea",0); area.AddGrid(grid); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        grid.Nodes[(0,0)]=node; node.AddObject(caller);
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        var msgs = new List<string>();
        // Capture Msg via PeekMessages
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        Assert.Contains(caller.PeekMessages(), m=> m.Contains("No active connection"));
    }

    [Fact] public void MapEditHandlesNoneConnectionGracefully()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var caller = GameObject.Create("Builder2", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        var sess = new Session(connection:null);
        caller.Session = sess;
        var node = new Node(new Coord("TestArea",1,0,0));
        var nh = GlobalServices.GetNodeHandler(); var area=new NodeArea("TestArea"); var grid=new NodeGrid("TestArea",0); area.AddGrid(grid); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        grid.Nodes[(1,0)]=node; node.AddObject(caller);
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        caller.Session.Connection = null;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        Assert.Contains(caller.PeekMessages(), m=> m.Contains("No active connection"));
    }
}
