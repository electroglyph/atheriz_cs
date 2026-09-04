// Port of atheriz/tests/test_mapedit_building_move.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMapEditBuildingMoveTests
{
    private const string AREA = "testbuilding";
    private const int Z = 0;

    private sealed class FakeConn : BaseConnection
    {
        public FakeConn(string host="10.0.0.1") : base("test_building") { ClientHost = host; Session.Puppet = CreateBuilder(); }
        private static GameObject CreateBuilder(){ var b=GameObject.Create("Builder", isPc:true); b.PrivilegeLevel=Privilege.Builder; return b; }
        public override void SendCommand(string cmd, List<object?>? args=null, Dictionary<string,object?>? kwargs=null) { lock(Sent) Sent.Add((cmd, args??new(), kwargs??new())); }
        public override void Close() {}
        public List<(string Cmd, List<object?> Args, Dictionary<string,object?> Kw)> Sent = new();
    }

    private static Dictionary<(int,int), string> MakeBuildingGrid()
    {
        var g = new Dictionary<(int,int), string>();
        int W=11, H=7;
        for(int x=1;x<W-1;x++){ g[(x,0)]="─"; g[(x,H-1)]="─"; }
        for(int y=1;y<H-1;y++){ g[(0,y)]="│"; g[(W-1,y)]="│"; }
        g[(0,0)]="┌"; g[(W-1,0)]="┐"; g[(0,H-1)]="└"; g[(W-1,H-1)]="┘";
        g[(0,3)]="├"; g[(W-1,3)]="┤";
        var doorGlyph = GameUtils.WrapTruecolor("━", 35, fgBright:65);
        for(int x=1;x<W-1;x++) g[(x,3)] = x==5 ? doorGlyph : "─";
        g[(4,2)]="─"; g[(5,2)]="─"; g[(6,2)]="─";
        g[(1,4)]="─"; g[(2,4)]="─"; g[(3,4)]="─";
        g[(2,5)]=GameUtils.WrapTruecolor("█",50);
        g[(8,4)]="▟";
        return g;
    }

    private static (MapInfo mi, NodeHandler nh, NodeGrid gridObj, Door door) MakeFixture()
    {
        var grid = MakeBuildingGrid();
        var mi = new MapInfo(AREA);
        foreach(var kv in grid) mi.PostGrid[kv.Key]=kv.Value;
        // pre_grid stays empty
        var upper = new Node(new Coord(AREA,5,2,Z));
        var lower = new Node(new Coord(AREA,5,4,Z));
        var gridObj = new NodeGrid(AREA, Z);
        gridObj.Nodes[(upper.Coord.X, upper.Coord.Y)] = upper;
        gridObj.Nodes[(lower.Coord.X, lower.Coord.Y)] = lower;
        var area = new NodeArea(AREA);
        area.AddGrid(gridObj);
        var nh = new NodeHandler(autoLoad:false);
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        var mh = new MapHandler(autoLoad:false);
        mh.SetMapInfo(AREA, Z, mi);
        MapHandlerHolder.Set(mh);
        // Door
        var door = new Door(new Coord(AREA,5,2,Z), new Coord(AREA,5,4,Z), "north","south", (5,3), GameUtils.WrapTruecolor("━",35,fgBright:65), GameUtils.WrapTruecolor("┚",35,fgBright:65), true,false);
        nh.AddDoor(door);
        // Ensure factories
        InputFuncs.MapHandlerFactory = () => mh;
        InputFuncs.NodeHandlerFactory = () => nh;
        return (mi, nh, gridObj, door);
    }

    private static (Dictionary<(int,int),string> after, List<List<object?>> cells) ClientDiff(Dictionary<(int,int),string> before, (int dx,int dy) delta)
    {
        var after = before.ToDictionary(kv => (kv.Key.Item1 + delta.dx, kv.Key.Item2 + delta.dy), kv => kv.Value);
        var all = new HashSet<(int,int)>(before.Keys);
        foreach(var k in after.Keys) all.Add(k);
        var cells = new List<List<object?>>();
        foreach(var coord in all.OrderBy(k=>k.Item1).ThenBy(k=>k.Item2))
        {
            before.TryGetValue(coord, out var b);
            after.TryGetValue(coord, out var a);
            bool hasB = before.ContainsKey(coord);
            bool hasA = after.ContainsKey(coord);
            string? bVal = hasB ? b : null;
            string? aVal = hasA ? a : null;
            if (bVal != aVal)
            {
                cells.Add(new List<object?>{ coord.Item1, coord.Item2, aVal ?? "" });
            }
        }
        return (after, cells);
    }

    private static string Handshake(FakeConn conn)
    {
        // Ensure builder puppet
        if (conn.Session.Puppet == null || !conn.Session.Puppet.IsBuilder)
        {
            var b = GameObject.Create("Builder", isPc:true); b.PrivilegeLevel = Privilege.Builder;
            conn.Session.Puppet = b;
        }
        var key = MapEdit.Grant("10.0.0.1", AREA, Z);
        new InputFuncs().MapEditHandler(conn, new List<object?>{ key, 0, new List<object?>() }, new Dictionary<string,object?>());
        var last = conn.Sent.Last();
        // last Args[1] is newKey
        return last.Args[1] as string ?? throw new Exception("handshake failed");
    }

    [Fact]
    public void TestBuildingMovedNortheastSyncsLosslessly()
    {
        using var env = GlobalTestEnv.Enter();
        var (mi, nh, gridObj, door) = MakeFixture();
        var mh = MapHandlerHolder.Get()!;
        // Ensure MapEdit clean
        MapEdit.ResetForTesting();
        var conn = new FakeConn();
        conn.ClientHost = "10.0.0.1";
        // Re-set factories after reset
        InputFuncs.MapHandlerFactory = () => mh;
        InputFuncs.NodeHandlerFactory = () => nh;

        var delta = (dx:3, dy:-2);
        var before = new Dictionary<(int,int),string>(mi.PostGrid);
        var (expectedAfter, glyphCells) = ClientDiff(before, delta);
        var key = Handshake(conn);
        var roomOps = new List<List<object?>>
        {
            new List<object?>{"room",5,2,5+delta.dx,2+delta.dy},
            new List<object?>{"room",5,4,5+delta.dx,4+delta.dy},
        };
        var allCells = new List<object?>();
        foreach(var c in glyphCells) allCells.Add(c);
        foreach(var r in roomOps) allCells.Add(r);

        new InputFuncs().MapEditHandler(conn, new List<object?>{ key, 1, allCells }, new Dictionary<string,object?>());

        // --- the in-game map must show EVERY tile at its new position ---
        Assert.Equal(expectedAfter.Count, mi.PostGrid.Count);
        foreach(var kv in expectedAfter) Assert.Equal(kv.Value, mi.PostGrid[kv.Key]);
        // also check that stale keys are gone
        foreach(var k in mi.PostGrid.Keys.ToList()) Assert.True(expectedAfter.ContainsKey(k), $"stale key {k} found");
        // pre_grid also should equal expectedAfter (batch seeded + edited)
        Assert.Equal(expectedAfter.Count, mi.PreGrid.Count);
        foreach(var kv in expectedAfter) Assert.Equal(kv.Value, mi.PreGrid[kv.Key]);

        // --- rooms re-keyed ---
        Assert.NotNull(gridObj.GetNode((5+delta.dx, 2+delta.dy)));
        Assert.NotNull(gridObj.GetNode((5+delta.dx, 4+delta.dy)));
        Assert.Null(gridObj.GetNode((5,2)));
        Assert.Null(gridObj.GetNode((5,4)));

        // --- door fully follows its rooms ---
        Assert.Equal(new Coord(AREA,8,0,Z), door.FromCoord);
        Assert.Equal(new Coord(AREA,8,2,Z), door.ToCoord);
        Assert.Equal((8,1), door.SymbolCoord!.Value);

        // --- a later open/close stamps the NEW position only ---
        var oldSymbolCoord = (5,3);
        var counterGlyph = before[(2,5)];
        Assert.Equal(counterGlyph, mi.PostGrid[oldSymbolCoord]);
        door.MapClose();
        Assert.Equal(door.ClosedSymbol, mi.PostGrid[(8,1)]);
        Assert.Equal(door.ClosedSymbol, mi.PreGrid[(8,1)]);
        Assert.Equal(counterGlyph, mi.PostGrid[oldSymbolCoord]);
    }

    [Fact]
    public void TestBatchUpdateSeedsPreGridFromPostGrid()
    {
        using var env = GlobalTestEnv.Enter();
        var mi = new MapInfo(AREA);
        mi.PostGrid[(0,0)]="X"; mi.PostGrid[(1,0)]="─";
        Assert.Empty(mi.PreGrid);
        using (mi.BatchUpdate()) { }
        Assert.Equal(2, mi.PreGrid.Count);
        Assert.Equal("X", mi.PreGrid[(0,0)]);
        Assert.Equal("─", mi.PreGrid[(1,0)]);
        Assert.Equal(mi.PostGrid.Count, mi.PreGrid.Count);
        foreach(var kv in mi.PostGrid) Assert.Equal(kv.Value, mi.PreGrid[kv.Key]);
    }

    [Fact]
    public void TestDoorSymbolFollowsMove()
    {
        using var env = GlobalTestEnv.Enter();
        var mi = new MapInfo(AREA);
        var grid = MakeBuildingGrid();
        foreach(var kv in grid) mi.PostGrid[kv.Key]=kv.Value;
        var upper = new Node(new Coord(AREA,5,2,Z));
        var lower = new Node(new Coord(AREA,5,4,Z));
        var gridObj = new NodeGrid(AREA, Z);
        gridObj.Nodes[(5,2)]=upper; gridObj.Nodes[(5,4)]=lower;
        var area = new NodeArea(AREA); area.AddGrid(gridObj);
        var nh = new NodeHandler(autoLoad:false); nh.AddArea(area); NodeHandler.SetCurrent(nh);
        var mh = new MapHandler(autoLoad:false); mh.SetMapInfo(AREA,Z,mi); MapHandlerHolder.Set(mh);
        var door = new Door(new Coord(AREA,5,2,Z), new Coord(AREA,5,4,Z), "north","south",(5,3), GameUtils.WrapTruecolor("━",35,fgBright:65), GameUtils.WrapTruecolor("┚",35,fgBright:65));
        nh.AddDoor(door);
        InputFuncs.MapHandlerFactory=()=>mh; InputFuncs.NodeHandlerFactory=()=>nh;
        MapEdit.ResetForTesting();
        var conn = new FakeConn();
        var key = MapEdit.Grant("10.0.0.1", AREA, Z);
        new InputFuncs().MapEditHandler(conn, new List<object?>{key,0,new List<object?>()}, new Dictionary<string,object?>());
        var hk = conn.Sent.Last().Args[1] as string;
        var roomOps = new List<object?>{ new List<object?>{"room",5,2,8,0}, new List<object?>{"room",5,4,8,2} };
        // Need glyph cells for building move: compute diff
        var before = new Dictionary<(int,int),string>(grid);
        var (expected, cells) = ClientDiff(before, (3,-2));
        var all = new List<object?>();
        foreach(var c in cells) all.Add(c);
        foreach(var r in (List<List<object?>>)new List<List<object?>>{ new List<object?>{"room",5,2,8,0}, new List<object?>{"room",5,4,8,2} }) all.Add(r);
        new InputFuncs().MapEditHandler(conn, new List<object?>{hk!,1,all}, new Dictionary<string,object?>());
        Assert.Equal((8,1), door.SymbolCoord!.Value);
        Assert.Equal(new Coord(AREA,8,0,Z), door.FromCoord);
        Assert.Equal(new Coord(AREA,8,2,Z), door.ToCoord);
    }
}
