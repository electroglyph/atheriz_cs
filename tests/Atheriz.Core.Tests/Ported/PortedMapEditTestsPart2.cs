// Port of atheriz/tests/test_mapedit.py:1 part2 — InputFuncs map_edit + validate
using System.Threading;
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMapEditTestsPart2
{
    private sealed class FakeConn2 : BaseConnection
    {
        public FakeConn2(string host="10.0.0.1") : base("test2") { ClientHost = host; Session.Puppet = CreateBuilder(); }
        private static GameObject CreateBuilder(){ var b=GameObject.Create("B", isPc:true); b.PrivilegeLevel=Privilege.Builder; return b; }
        public override void SendCommand(string cmd, List<object?>? args=null, Dictionary<string,object?>? kwargs=null) { lock(Sent) Sent.Add((cmd, args??new(), kwargs??new())); }
        public override void Close() {}
        public List<(string Cmd, List<object?> Args, Dictionary<string,object?> Kw)> Sent = new();
    }
    private static BaseConnection MakeConn(string ip="10.0.0.1") { var c=new FakeConn2(ip); return c; }
    private static void Reset() { MapEdit.ResetForTesting(); InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler(); InputFuncs.NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler(); }
    private static string Handshake(BaseConnection conn)
    {
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        return ((FakeConn2)conn).Sent.Last().Args[1] as string ?? throw new Exception("no handshake");
    }
    private static MapInfo MakeMi(Dictionary<(int,int),string>? grid=null){ var mi=new MapInfo("TestArea"); if(grid!=null) foreach(var kv in grid) mi.PreGrid[kv.Key]=kv.Value; return mi; }
    private static NodeGrid GridOf(NodeHandler nh) => nh.GetArea("TestArea")!.GetGrid(0)!;

    [Fact] public void MapEditHandshake()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var sent = ((FakeConn2)conn).Sent;
        Assert.Single(sent); Assert.Equal("map_ack", sent[0].Cmd);
        Assert.Equal(0, sent[0].Args[0]);
        Assert.NotEqual(key, sent[0].Args[1] as string);
        Assert.True(MapEdit.chains.ContainsKey(sent[0].Args[1] as string ?? ""));
    }

    [Fact] public void MapEditAppliesColorCells()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var hk = ((FakeConn2)conn).Sent[0].Args[1] as string;
        var cellA = new List<object?>{2,3,"B"};
        cellA.Add(new List<object?>{255,0,0});
        cellA.Add(new List<object?>{-1,-1,-1});
        cellA.Add(new List<object?>{"bold"});
        var cellB = new List<object?>{4,4,"C"};
        cellB.Add(new List<object?>{10,20,30});
        cellB.Add(new List<object?>{1,2,3});
        cellB.Add(new List<object?>{"italic","underline"});
        var inner = new List<object?>{cellA, cellB};
        var args2 = new List<object?>{hk!,1,inner};
        new InputFuncs().MapEditHandler(conn, args2, new Dictionary<string,object?>());
        Assert.Equal("\x1b[1m\x1b[38;2;255;0;0m\x1b[48;2;0;0;0mB\x1b[0m", mi.PreGrid[(2,3)]);
        Assert.Equal("\x1b[4m\x1b[3m\x1b[38;2;10;20;30m\x1b[48;2;1;2;3mC\x1b[0m", mi.PreGrid[(4,4)]);
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("map_ack", ((FakeConn2)conn).Sent[1].Cmd); Assert.Equal(1, ((FakeConn2)conn).Sent[1].Args[0]);
    }

    [Fact] public void MapEditAppliesCells()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(new Dictionary<(int,int),string>{[(2,3)]="A"}); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var hk = ((FakeConn2)conn).Sent[0].Args[1] as string;
        new InputFuncs().MapEditHandler(conn, new List<object?>{hk!,1,new List<object?>{ new List<object?>{2,3,"B"}, new List<object?>{9,9,""}, new List<object?>{4,4,"C"}}}, new Dictionary<string,object?>());
        Assert.Equal("B", mi.PreGrid[(2,3)]);
        Assert.Equal("C", mi.PreGrid[(4,4)]);
        Assert.False(mi.PreGrid.ContainsKey((9,9)));
        Assert.True(mi.PreGrid.ContainsKey((2,3)));
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("map_ack", ((FakeConn2)conn).Sent[1].Cmd);
    }

    [Fact] public void MapEditRetryDoesNotReapply()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var hk = ((FakeConn2)conn).Sent[0].Args[1] as string;
        new InputFuncs().MapEditHandler(conn, new List<object?>{hk!,1,new List<object?>{ new List<object?>{0,0,"X"}}}, new Dictionary<string,object?>());
        var ek = ((FakeConn2)conn).Sent[1].Args[1] as string;
        new InputFuncs().MapEditHandler(conn, new List<object?>{hk!,1,new List<object?>{ new List<object?>{0,0,"Y"}}}, new Dictionary<string,object?>());
        Assert.Equal("X", mi.PreGrid[(0,0)]);
        Assert.Equal(3, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("map_ack", ((FakeConn2)conn).Sent[2].Cmd);
        Assert.Equal(ek, ((FakeConn2)conn).Sent[2].Args[1]);
    }

    [Fact] public void MapEditRejectUnknownKey()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var conn = MakeConn();
        new InputFuncs().MapEditHandler(conn, new List<object?>{"bogus",1,new List<object?>()}, new Dictionary<string,object?>());
        Assert.Single(((FakeConn2)conn).Sent);
        Assert.Equal("map_edit_reject", ((FakeConn2)conn).Sent[0].Cmd);
        Assert.Equal("unknown_key", ((FakeConn2)conn).Sent[0].Args[0] as string);
    }

    [Fact] public void MapEditRejectReplay()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var cur = ((FakeConn2)conn).Sent[0].Args[1] as string;
        new InputFuncs().MapEditHandler(conn, new List<object?>{cur!,0,new List<object?>()}, new Dictionary<string,object?>());
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("map_edit_reject", ((FakeConn2)conn).Sent[1].Cmd);
        Assert.Equal("replay", ((FakeConn2)conn).Sent[1].Args[0] as string);
    }

    [Fact] public void MapEditMalformedArgsAreIgnored()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var conn = MakeConn();
        var cases = new List<List<object?>>{
            new List<object?>{}, new List<object?>{"key"}, new List<object?>{123,0,new List<object?>{}}, new List<object?>{"key","0",new List<object?>{}}, new List<object?>{"key",0,"cells"}, new List<object?>{ "key",0,new List<object?>{ new List<object?>{1}}}, new List<object?>{ "key",0,new List<object?>{ new List<object?>{"a","b","c"}}}, new List<object?>{ "key",0,new List<object?>{ new List<object?>{0,0,1}}}, new List<object?>{ "key",0,new List<object?>{ new List<object?>{"room",0,0}}}
        };
        foreach(var a in cases) new InputFuncs().MapEditHandler(conn, a, new Dictionary<string,object?>());
        Assert.Empty(((FakeConn2)conn).Sent);
    }

    [Fact] public void MapEditRoomOpMovesNode()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var room = new Node(new Coord("TestArea",0,0,0), desc:"A room.");
        var neighbor = new Node(new Coord("TestArea",1,0,0)); neighbor.AddLink(new NodeLink("West", new Coord("TestArea",0,0,0)));
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(0,0)]=room; grid.Nodes[(1,0)]=neighbor;
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        var mh = GlobalServices.GetMapHandler(); var mi = MakeMi(new Dictionary<(int,int),string>{}); mh.SetMapInfo("TestArea",0,mi);
        InputFuncs.MapHandlerFactory = () => mh; InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn();
        var key = Handshake(conn);
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{"room",0,0,5,2}}}, new Dictionary<string,object?>());
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("map_ack", ((FakeConn2)conn).Sent[1].Cmd); Assert.Equal(1, ((FakeConn2)conn).Sent[1].Args[0]);
        Assert.Null(grid.GetNode((0,0)));
        var moved = grid.GetNode((5,2));
        Assert.Same(room, moved);
        Assert.Equal(new Coord("TestArea",5,2,0), moved!.Coord);
        Assert.Equal(new Coord("TestArea",5,2,0), neighbor.GetLinks()[0].Coord);
    }

    [Fact] public void MapValidateMovesAllowed()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(3,3)]=new Node(new Coord("TestArea",3,3,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn(); var key = Handshake(conn);
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{3,3,4,3}}}, new Dictionary<string,object?>());
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("moves_ok", ((FakeConn2)conn).Sent[1].Cmd);
        var seq = (int)((FakeConn2)conn).Sent[1].Args[0]!; var newKey = ((FakeConn2)conn).Sent[1].Args[1] as string;
        Assert.Equal(1, seq); Assert.NotNull(newKey);
        Assert.True(MapEdit.chains.ContainsKey(newKey!));
        Assert.True(MapEdit.chains[newKey!].Validation!.Count==0);
    }

    [Fact] public void MapValidateMovesDenied()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(3,3)]=new Node(new Coord("TestArea",3,3,0)); grid.Nodes[(4,3)]=new Node(new Coord("TestArea",4,3,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn(); var key = Handshake(conn);
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{3,3,4,3}}}, new Dictionary<string,object?>());
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("moves_denied", ((FakeConn2)conn).Sent[1].Cmd);
        var seq = (int)((FakeConn2)conn).Sent[1].Args[0]!;
        var rawDenied = ((FakeConn2)conn).Sent[1].Args[2];
        var denied = rawDenied as List<int>;
        if(denied==null){
            if(rawDenied is List<object?> lo) denied = lo.Cast<int>().ToList();
            else if(rawDenied is System.Collections.Generic.List<int> li) denied = li;
            else denied = new List<int>{0};
        }
        // In C# denied is List<int>
        var deniedList = ((FakeConn2)conn).Sent[1].Args[2] as List<int>;
        if(deniedList==null) deniedList = ((FakeConn2)conn).Sent[1].Args[2] as List<object?> != null ? ((List<object?>)((FakeConn2)conn).Sent[1].Args[2]!).Select(o=> Convert.ToInt32(o)).ToList() : null;
        Assert.NotNull(deniedList); Assert.Equal(new List<int>{0}, deniedList!);
        Assert.NotNull(GridOf(nh).GetNode((3,3)));
        Assert.NotNull(GridOf(nh).GetNode((4,3)));
    }

    [Fact] public void MapValidateMovesRetryResendsVerdict()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(3,3)]=new Node(new Coord("TestArea",3,3,0)); grid.Nodes[(4,3)]=new Node(new Coord("TestArea",4,3,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn(); var key = Handshake(conn);
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{3,3,4,3}}}, new Dictionary<string,object?>());
        var verdictKey = ((FakeConn2)conn).Sent[1].Args[1] as string;
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{3,3,4,3}}}, new Dictionary<string,object?>());
        Assert.Equal(3, ((FakeConn2)conn).Sent.Count);
        Assert.Equal(((FakeConn2)conn).Sent[1].Cmd, ((FakeConn2)conn).Sent[2].Cmd);
        Assert.Equal(((FakeConn2)conn).Sent[1].Args[0], ((FakeConn2)conn).Sent[2].Args[0]);
        Assert.Equal(((FakeConn2)conn).Sent[1].Args[1], ((FakeConn2)conn).Sent[2].Args[1]);
        Assert.Equal(((FakeConn2)conn).Sent[1].Args[2], ((FakeConn2)conn).Sent[2].Args[2]);
        Assert.True(MapEdit.chains[verdictKey!].Validation!.SequenceEqual(new List<int>{0}));
    }

    [Fact] public void MapValidateMovesUnknownAreaDeniesAll()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var conn = MakeConn(); var key = Handshake(conn);
        var nh = new NodeHandler(autoLoad:false); // empty, get_area returns null
        InputFuncs.NodeHandlerFactory = () => nh;
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{0,0,1,0}}}, new Dictionary<string,object?>());
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("moves_denied", ((FakeConn2)conn).Sent[1].Cmd);
        var denied = ((FakeConn2)conn).Sent[1].Args[2] as List<int>;
        if(denied==null) denied = ((List<object?>)((FakeConn2)conn).Sent[1].Args[2]!).Select(o=>Convert.ToInt32(o)).ToList();
        Assert.Equal(new List<int>{0}, denied!);
    }

    [Fact] public void MapValidateMovesContextFreesVacatedDestination()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(0,0)]=new Node(new Coord("TestArea",0,0,0)); grid.Nodes[(1,0)]=new Node(new Coord("TestArea",1,0,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn(); var key = Handshake(conn);
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{1,0,0,0}}, new List<object?>{ new List<object?>{0,0,5,5}}}, new Dictionary<string,object?>());
        Assert.Equal(2, ((FakeConn2)conn).Sent.Count);
        Assert.Equal("moves_ok", ((FakeConn2)conn).Sent[1].Cmd);
        Assert.Equal(1, ((FakeConn2)conn).Sent[1].Args[0]);
        Assert.NotNull(GridOf(nh).GetNode((0,0)));
        Assert.Null(GridOf(nh).GetNode((5,5)));
    }

    [Fact] public void MapValidateMovesWithoutContextStillDenies()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(0,0)]=new Node(new Coord("TestArea",0,0,0)); grid.Nodes[(1,0)]=new Node(new Coord("TestArea",1,0,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn(); var key = Handshake(conn);
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{1,0,0,0}}}, new Dictionary<string,object?>());
        Assert.Equal("moves_denied", ((FakeConn2)conn).Sent[1].Cmd);
        var deniedTmp = ((FakeConn2)conn).Sent[1].Args[2] as List<int>;
        if(deniedTmp==null) deniedTmp = ((List<object?>)((FakeConn2)conn).Sent[1].Args[2]!).Select(o=>Convert.ToInt32(o)).ToList();
        var denied = deniedTmp;
        Assert.Equal(new List<int>{0}, denied);
    }

    [Fact] public void MapValidateMovesMalformedContextDropsMessage()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(3,3)]=new Node(new Coord("TestArea",3,3,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn(); var key = Handshake(conn);
        var badContexts = new List<object?>{"nope", new List<object?>{ new List<object?>{"room",0}}, new List<object?>{ new List<object?>{0,0}}, new List<object?>{"x"}, new List<object?>{ new List<object?>{ new List<object>{1,2,3,4}}}, new List<object?>{ new List<object?>{0,0,"a","b"}}};
        foreach(var bad in badContexts)
        {
            new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{3,3,4,3}}, bad}, new Dictionary<string,object?>());
        }
        Assert.Single(((FakeConn2)conn).Sent); // only handshake
    }

    [Fact] public void MapValidateMovesExtraArgsDropped()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var conn = MakeConn(); var key = Handshake(conn);
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{0,0,1,0}}, new List<object?>(), new List<object?>{"extra"}}, new Dictionary<string,object?>());
        Assert.Single(((FakeConn2)conn).Sent);
    }

    [Fact] public void MapValidateMovesRetryReplaysVerdictWithContext()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(0,0)]=new Node(new Coord("TestArea",0,0,0)); grid.Nodes[(1,0)]=new Node(new Coord("TestArea",1,0,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var conn = MakeConn(); var key = Handshake(conn);
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{1,0,0,0}}, new List<object?>{ new List<object?>{0,0,9,9}}}, new Dictionary<string,object?>());
        var verdictKey = ((FakeConn2)conn).Sent[1].Args[1] as string;
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{key,1,new List<object?>{ new List<object?>{1,0,0,0}}, new List<object?>{ new List<object?>{0,0,9,9}}}, new Dictionary<string,object?>());
        Assert.Equal(3, ((FakeConn2)conn).Sent.Count);
        Assert.Equal(((FakeConn2)conn).Sent[1].Cmd, ((FakeConn2)conn).Sent[2].Cmd);
        Assert.Equal(verdictKey, ((FakeConn2)conn).Sent[2].Args[1] as string);
        Assert.Empty(MapEdit.chains[verdictKey!].Validation!);
    }
}
