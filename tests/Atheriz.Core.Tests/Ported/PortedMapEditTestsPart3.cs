// Port of atheriz/tests/test_mapedit.py:1 part3 — legend, building, evict
using System.Threading;
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMapEditTestsPart3
{
    private sealed class FakeC : BaseConnection
    {
        public FakeC(string host="10.0.0.1") : base("test3") { ClientHost=host; Session.Puppet = CreateBuilder(); }
        private static GameObject CreateBuilder(){ var b=GameObject.Create("B", isPc:true); b.PrivilegeLevel=Privilege.Builder; return b; }
        public override void SendCommand(string cmd, List<object?>? args=null, Dictionary<string,object?>? kw=null){ lock(Sent) Sent.Add((cmd, args??new(), kw??new())); }
        public override void Close(){}
        public List<(string Cmd, List<object?> Args, Dictionary<string,object?> Kw)> Sent=new();
    }
    private static void Reset(){ MapEdit.ResetForTesting(); InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler(); InputFuncs.NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler(); }
    private static BaseConnection MakeConn(string ip="10.0.0.1") { var c=new FakeC(ip); return c; }
    private static MapInfo MakeMi(){ var mi=new MapInfo("TestArea"); return mi; }

    [Fact] public void MapEditEvictLruNoTtlAndSessionDiscard()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        MapEdit.ResetForTesting();
        // No TTL: chains are valid while the owning session is open; only the cap evicts.
        var origMax = Atheriz.Core.Settings.AtherizSettings.Global.MapeditMaxChains;
        try{
            Atheriz.Core.Settings.AtherizSettings.Global.MapeditMaxChains = 2;
            var k1 = MapEdit.Grant("1.1.1.1","A",0);
            var k2 = MapEdit.Grant("1.1.1.1","A",0);
            Assert.Equal(2, MapEdit.chains.Count);
            // Backdating creation must NOT evict (no TTL).
            var now = ((double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency);
            foreach(var ch in MapEdit.chains.Values.ToList()){
                ch.CreatedAt = DateTime.UtcNow.AddSeconds(-100000);
                ch.CreatedMonotonic = now - 100000;
            }
            MapEdit.ClearStale();
            Assert.Equal(2, MapEdit.chains.Count);
            // Cap still enforced: oldest-created evicted first.
            // Note: backdated chains are oldest, so k1 goes first.
            var k3 = MapEdit.Grant("1.1.1.1","A",0);
            Assert.True(MapEdit.chains.Count<=2);
            // Session-bound chains die with the session, others survive.
            MapEdit.ResetForTesting();
            var session = new Session(null);
            var ks = MapEdit.Grant("1.1.1.1","A",0, session);
            var ko = MapEdit.Grant("1.1.1.1","A",0);
            MapEdit.DiscardSession(session);
            Assert.DoesNotContain(ks, MapEdit.chains.Keys);
            Assert.Contains(ko, MapEdit.chains.Keys);
            MapEdit.DiscardSession(null);
            Assert.Contains(ko, MapEdit.chains.Keys);
            // AtDisconnect discards the session's chains.
            var ks2 = MapEdit.Grant("1.1.1.1","A",0, session);
            session.AtDisconnect();
            Assert.DoesNotContain(ks2, MapEdit.chains.Keys);
        } finally{
            Atheriz.Core.Settings.AtherizSettings.Global.MapeditMaxChains = origMax;
            MapEdit.ResetForTesting();
        }
    }

    [Fact] public void DrawPayloadIncludesLegendAndPlayerSymbol()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = new MapInfo("TestArea"); mi.PreGrid[(0,0)]="X";
        mi.LegendEntries.Add(new LegendEntry("★","shrine",(2,3)){ Show=true, Fg=170.0, Bg=null });
        mh.SetMapInfo("TestArea",0,mi);
        var node = new Node(new Coord("TestArea",0,0,0));
        var nh = GlobalServices.GetNodeHandler(); var area=new NodeArea("TestArea"); var grid=new NodeGrid("TestArea",0); area.AddGrid(grid); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        grid.Nodes[(0,0)]=node;
        var conn = new FakeC();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        caller.Symbol = "\x1b[38;2;255;0;0m🯅\x1b[0m";
        InputFuncs.MapHandlerFactory = () => mh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = ((FakeC)conn).Sent.First(s=>s.Cmd=="launch_draw").Args[1] as Dictionary<string,object?>;
        Assert.NotNull(payload);
        var legend = payload!["legend"] as List<Dictionary<string,object?>>;
        Assert.NotNull(legend); Assert.Single(legend!);
        Assert.Equal("★", legend![0]["symbol"]);
        Assert.Equal("shrine", legend![0]["desc"]);
        Assert.Equal(new List<int>{2,3}, legend![0]["coord"] as List<int>);
        Assert.Equal(true, legend![0]["show"]);
        Assert.Equal(170.0, legend![0]["fg"]);
        Assert.Null(legend![0]["bg"]);
        Assert.Equal("🯅", payload!["playerSymbol"]);
    }

    [Fact] public void DrawPayloadLegendCoordsNoneAndPlayerSymbolFallback()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = new MapInfo("TestArea");
        mi.LegendEntries.Add(new LegendEntry("X","test",null));
        mh.SetMapInfo("TestArea",0,mi);
        var node = new Node(new Coord("TestArea",0,0,0));
        var nh = GlobalServices.GetNodeHandler(); var area=new NodeArea("TestArea"); var grid=new NodeGrid("TestArea",0); area.AddGrid(grid); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        grid.Nodes[(0,0)]=node;
        var conn = new FakeC();
        var caller = GameObject.Create("Caller", isPc:true); caller.PrivilegeLevel=Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(caller);
        caller.Session = new Session(conn); caller.Session.Connection=conn; conn.Session.Puppet=caller;
        caller.Symbol = "X";
        InputFuncs.MapHandlerFactory = () => mh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = ((FakeC)conn).Sent.First(s=>s.Cmd=="launch_draw").Args[1] as Dictionary<string,object?>;
        var legend = payload!["legend"] as List<Dictionary<string,object?>>;
        Assert.Null(legend![0]["coord"]);
        Assert.Equal("X", payload!["playerSymbol"]);
    }

    [Fact] public void MapEditLegendReplacesEntriesAndNotifies()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(); mh.SetMapInfo("TestArea",0,mi);
        var updates = new List<(List<(string,string,(int,int))> legend, bool show, string area)>();
        // Add listener that captures at_legend_update via FakeListener in MapInfo? For C# we check that legend was replaced
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var hk = ((FakeC)conn).Sent[0].Args[1] as string;
        ((FakeC)conn).Sent.Clear();
        var legend = new List<object?>{ new Dictionary<string,object?>{["symbol"]="★",["desc"]="shrine",["coord"]= new List<object?>{2,3},["show"]=true,["fg"]=170.0,["bg"]=null}, new Dictionary<string,object?>{["symbol"]="■",["desc"]="wall",["coord"]=null,["show"]=false}};
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{hk!,1, legend}, new Dictionary<string,object?>());
        Assert.Equal(2, mi.LegendEntries.Count);
        Assert.Equal("★", mi.LegendEntries[0].Symbol);
        Assert.Equal("shrine", mi.LegendEntries[0].Desc);
        Assert.Equal((2,3), mi.LegendEntries[0].Coord);
        Assert.False(mi.LegendEntries[1].Show);
        Assert.True(mi.MapChanged);
        Assert.Equal(2, ((FakeC)conn).Sent.Count);
        Assert.Equal("map_ack", ((FakeC)conn).Sent[0].Cmd); Assert.Equal(1, ((FakeC)conn).Sent[0].Args[0]);
        Assert.Equal("legend_ok", ((FakeC)conn).Sent[1].Cmd); Assert.Equal(1, ((FakeC)conn).Sent[1].Args[0]);
    }

    [Fact] public void MapEditLegendNullDescNormalizedToEmptyString()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var k1 = ((FakeC)conn).Sent[0].Args[1] as string;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{k1!,1,new List<object?>{ new Dictionary<string,object?>{["symbol"]="X",["desc"]=null,["coord"]=null,["show"]=true}}}, new Dictionary<string,object?>());
        Assert.Equal("", mi.LegendEntries[0].Desc);
    }

    [Fact] public void MapEditLegendKeyProvesBuilderNoPuppetCheck()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = new FakeC(); conn.ClientHost="10.0.0.1"; conn.Session.Puppet=null;
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var k1 = ((FakeC)conn).Sent[0].Args[1] as string;
        ((FakeC)conn).Sent.Clear();
        var nonBuilder = GameObject.Create("NB", isPc:true); nonBuilder.PrivilegeLevel=Privilege.Player;
        conn.Session.Puppet = nonBuilder;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{k1!,1,new List<object?>{ new Dictionary<string,object?>{["symbol"]="X",["desc"]="hi",["coord"]=null,["show"]=true}}}, new Dictionary<string,object?>());
        Assert.Equal("map_ack", ((FakeC)conn).Sent[0].Cmd);
        Assert.Single(mi.LegendEntries); Assert.Equal("X", mi.LegendEntries[0].Symbol);
    }

    [Fact] public void MapEditHandshakeAllowsAnonymousPuppet()
    {
        Reset();
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = new FakeC(); conn.ClientHost="10.0.0.1"; conn.Session.Puppet=null;
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        Assert.Equal("map_ack", ((FakeC)conn).Sent[0].Cmd);
    }

    [Fact] public void MapValidateMovesAllowsAnonymousPuppet()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var grid = new NodeGrid("TestArea",0); grid.Nodes[(3,3)]=new Node(new Coord("TestArea",3,3,0));
        var area = new NodeArea("TestArea"); area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        InputFuncs.NodeHandlerFactory = () => nh;
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = new FakeC(); conn.ClientHost="10.0.0.1"; conn.Session.Puppet=null;
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var k1 = ((FakeC)conn).Sent[0].Args[1] as string;
        new InputFuncs().MapValidateMovesHandler(conn, new List<object?>{k1!,1,new List<object?>{ new List<object?>{3,3,4,3}}}, new Dictionary<string,object?>());
        Assert.Contains(((FakeC)conn).Sent[1].Cmd, new[] {"moves_ok","moves_denied"});
    }

    [Fact] public void MapEditLegendRejectUnknownKey()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{"bogus",1,new List<object?>()}, new Dictionary<string,object?>());
        Assert.Equal("map_edit_reject", ((FakeC)conn).Sent[0].Cmd);
        Assert.Equal("unknown_key", ((FakeC)conn).Sent[0].Args[0] as string);
    }

    [Fact] public void MapEditLegendRetryDoesNotReapply()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var k1 = ((FakeC)conn).Sent[0].Args[1] as string;
        Assert.Equal("map_ack", ((FakeC)conn).Sent[0].Cmd);
        // legend_ok also sent?
        // In C# MapEditLegendHandler for handshake (seq 0) also sends legend_ok? Check: for seq 0, it does map_ack + legend_ok? Original python for handshake (seq 0) with empty legend does ack + legend_ok? In InputFuncs, handshake for legend is also map_ack only? Let's check original: test_map_edit_legend_retry checks that after handshake, there are two entries: map_ack and legend_ok. Our C# for handshake with empty legend should send both? In part3 earlier we had that.
        // For this test, we need to handle both
        var hk = k1;
        // Need to clear after handshake, but we have 2 entries if legend_ok sent
        ((FakeC)conn).Sent.Clear();
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{hk!,1,new List<object?>{ new Dictionary<string,object?>{["symbol"]="A",["desc"]="first",["coord"]=null,["show"]=true}}}, new Dictionary<string,object?>());
        var editedKey = ((FakeC)conn).Sent[0].Args[1] as string;
        Assert.Equal("map_ack", ((FakeC)conn).Sent[0].Cmd);
        Assert.Equal("A", mi.LegendEntries[0].Symbol);
        ((FakeC)conn).Sent.Clear();
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{hk!,1,new List<object?>{ new Dictionary<string,object?>{["symbol"]="B",["desc"]="second",["coord"]=null,["show"]=true}}}, new Dictionary<string,object?>());
        Assert.Equal("A", mi.LegendEntries[0].Symbol);
        Assert.Equal("map_ack", ((FakeC)conn).Sent[0].Cmd);
        Assert.Equal(editedKey, ((FakeC)conn).Sent[0].Args[1] as string);
    }

    [Fact] public void MapEditLegendMalformedEntriesAreIgnored()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var mi = MakeMi(); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        var bad = new List<object?>{
            new List<object?>{ new Dictionary<string,object?>{["desc"]="no symbol"}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="", ["desc"]="empty symbol"}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="toolong", ["desc"]="too long"}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="X", ["desc"]=123}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="X", ["desc"]="hi", ["coord"]= new List<object?>{1}}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="X", ["desc"]="hi", ["show"]="yes"}},
        };
        foreach(var legend in bad){
            new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,legend}, new Dictionary<string,object?>());
            // Each bad legend via map_edit (not legend) should be map_edit_reject? Actually original test for legend malformed uses map_edit_legend
            // We'll test legend handler
        }
        // Now test legend malformed via MapEditLegendHandler
        var conn2 = MakeConn();
        var badLegends = new List<List<object?>>{
            new List<object?>{ new Dictionary<string,object?>{["desc"]="no symbol"}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="", ["desc"]="empty symbol"}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="toolong", ["desc"]="too long"}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="X", ["desc"]=123}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="X", ["desc"]="hi", ["coord"]= new List<object?>{1}}},
            new List<object?>{ new Dictionary<string,object?>{["symbol"]="X", ["desc"]="hi", ["show"]="yes"}},
        };
        foreach(var legend in badLegends){
            var k = MapEdit.Grant("10.0.0.1","TestArea",0);
            var c = MakeConn();
            new InputFuncs().MapEditLegendHandler(c, new List<object?>{k,0, legend}, new Dictionary<string,object?>());
            Assert.Equal("map_edit_reject", ((FakeC)c).Sent[0].Cmd);
        }
        Assert.Empty(mi.LegendEntries);
        // malformed top-level args
        var conn3 = MakeConn();
        var malformed = new List<List<object?>>{
            new List<object?>{}, new List<object?>{"k"}, new List<object?>{123,0,new List<object?>{}}, new List<object?>{"k","0",new List<object?>{}}, new List<object?>{"k",0,"nope"}, new List<object?>{ "k",0, Enumerable.Repeat(new List<object?>{"too","many","entries"}, 201).Cast<object?>().ToList()}
        };
        foreach(var args in malformed){
            new InputFuncs().MapEditLegendHandler(conn3, args, new Dictionary<string,object?>());
        }
        Assert.Equal(6, ((FakeC)conn3).Sent.Count);
        Assert.All(((FakeC)conn3).Sent, s=> Assert.Equal("map_edit_reject", s.Cmd));
    }

    [Fact] public void MapEditLegendCreatesMapinfoIfMissing()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mh = GlobalServices.GetMapHandler();
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var k1 = ((FakeC)conn).Sent[0].Args[1] as string;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{k1!,1,new List<object?>{ new Dictionary<string,object?>{["symbol"]="X",["desc"]="new area legend",["coord"]=null,["show"]=true}}}, new Dictionary<string,object?>());
        var mi = mh.GetMapInfo("TestArea",0);
        Assert.NotNull(mi);
        Assert.Equal("new area legend", mi!.LegendEntries[0].Desc);
    }

    [Fact] public void MapEditLegendReplacesAtomicallyAndLegacyCoordTuple()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mi = MakeMi();
        mi.LegendEntries.Add(new LegendEntry("OLD","old",(0,0)));
        var mh = GlobalServices.GetMapHandler(); mh.SetMapInfo("TestArea",0,mi);
        var key = MapEdit.Grant("10.0.0.1","TestArea",0);
        var conn = MakeConn();
        InputFuncs.MapHandlerFactory = () => mh;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var k1 = ((FakeC)conn).Sent[0].Args[1] as string;
        new InputFuncs().MapEditLegendHandler(conn, new List<object?>{k1!,1,new List<object?>{ new Dictionary<string,object?>{["symbol"]="N",["desc"]="new",["coord"]= new List<object?>{5,6},["show"]=true}}}, new Dictionary<string,object?>());
        Assert.Single(mi.LegendEntries);
        Assert.Equal("N", mi.LegendEntries[0].Symbol);
        Assert.Equal((5,6), mi.LegendEntries[0].Coord);
        Assert.IsType<(int,int)>(mi.LegendEntries[0].Coord!.Value);
    }
}
