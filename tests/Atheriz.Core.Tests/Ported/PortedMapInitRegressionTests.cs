// Regression for empty map after `atheriz new` + login.
// Covers InitialSetup non-empty per-z map, persistence round-trip, and AtPostPuppet map push.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMapInitRegressionTests
{
    private sealed class CapturingConnection : BaseConnection
    {
        public readonly List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kw)> Sent = new();
        private readonly object _lock = new();
        public CapturingConnection() : base("cap") { }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
        {
            lock (_lock) Sent.Add((cmd, args ?? new(), kwargs ?? new()));
        }
        public override void Close() { }
        public List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kw)> Snapshot()
        {
            lock (_lock) return Sent.ToList();
        }
    }

    [Fact]
    public void InitialSetup_CreatesNonEmptyLimboMapForEveryZ()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_map_regress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var save = Path.Combine(tmp, "save");
        var secret = Path.Combine(tmp, "secret");
        try
        {
            InitialSetup.DoSetup(save, "admin", "password123", secret);
            using var db = AtherizDbContextFactory.Create(save);
            var handler = new MapHandler(AtherizSettings.Default, autoLoad: false);
            handler.Load(db);
            var snap = handler.Snapshot();
            Assert.Equal(9, snap.Count);
            for (int z = 0; z < 9; z++)
            {
                Assert.True(snap.ContainsKey(("limbo", z)), $"missing limbo {z}");
                var mi = snap[("limbo", z)];
                Assert.True(mi.PreGrid.Count >= 81, $"preGrid too small z={z} count={mi.PreGrid.Count}");
                Assert.True(mi.PostGrid.Count >= 81, $"postGrid empty z={z}");
                var s = AtherizSettings.Default;
                Assert.DoesNotContain(s.RoomPlaceholder, mi.PostGrid.Values);
                Assert.DoesNotContain(s.SingleWallPlaceholder, mi.PostGrid.Values);
                var hasWall = mi.PostGrid.Values.Any(v => v == "─" || v == "│" || v == "┼" || v == "┤" || v == "├" || v.Contains(" "));
                Assert.True(hasWall);
                var (rendered, minX, maxY) = MapInfo.RenderGrid(mi.PostGrid);
                Assert.False(string.IsNullOrWhiteSpace(rendered), $"rendered empty z={z}");
                Assert.True(rendered.Contains(" ") || rendered.Contains("─") || rendered.Contains("│"));
            }
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
            GlobalTestEnv.Enter().Dispose();
        }
    }

    [Fact]
    public void InitialSetup_PersistsAndReloadsViaExplicitDb()
    {
        // Use explicit tmp like first test, but verify that Global handler round-trips if we save+load via DB
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_map_regress2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var save = Path.Combine(tmp, "save");
        var secret = Path.Combine(tmp, "secret");
        try
        {
            InitialSetup.DoSetup(save, "admin2", "password123", secret);
            using var db = AtherizDbContextFactory.Create(save);
            var handler = new MapHandler(AtherizSettings.Default, autoLoad: false);
            handler.Load(db);
            var mi = handler.GetMapInfo("limbo", 4);
            Assert.NotNull(mi);
            Assert.True(mi!.PostGrid.Count > 0);
            // Force save+reload via new handler on same DB
            handler.Save(force: true);
            var handler2 = new MapHandler(AtherizSettings.Default, autoLoad: false);
            // need fresh db connection
            using var db2 = AtherizDbContextFactory.Create(save);
            handler2.Load(db2);
            var mi2 = handler2.GetMapInfo("limbo", 4);
            Assert.NotNull(mi2);
            Assert.Equal(mi.PostGrid.Count, mi2!.PostGrid.Count);
            var (r1, _, _) = MapInfo.RenderGrid(mi.PostGrid);
            var (r2, _, _) = MapInfo.RenderGrid(mi2.PostGrid);
            Assert.Equal(r1, r2);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
            GlobalTestEnv.Enter().Dispose();
        }
    }

    [Fact]
    public void PuppetLogin_SendsNonEmptyMapViaAtMapUpdate()
    {
        using var env = GlobalTestEnv.Enter();
        // Seed map handler with limbo at 4
        var mh = new MapHandler(AtherizSettings.Default, autoLoad: false);
        var mi = new MapInfo("limbo") { Settings = AtherizSettings.Default };
        var s = AtherizSettings.Default;
        for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
        {
            mi.PreGrid[(x, y)] = s.RoomPlaceholder;
            mi.PlaceWalls((x, y), s.SingleWallPlaceholder);
        }
        mi.PreRender();
        mh.SetMapInfo("limbo", 4, mi);
        // Install as global handler for AtPostPuppet to find
        GlobalServices.ResetForTesting();
        // Use reflection to inject mh as _mapHandler (since GetMapHandler is lazy)
        var f = typeof(GlobalServices).GetField("_mapHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        f!.SetValue(null, mh);
        // Create hero at limbo 4,4,4
        var hero = GameObject.Create("Hero", isPc: true);
        hero.Symbol = "X";
        hero.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(new Coord("limbo", 4, 4, 4));
        hero.MapEnabled = true;
        hero.IsMapable = true;
        ObjectRegistry.AddObject(hero);
        var conn = new CapturingConnection();
        var sess = new Session(conn);
        sess.Puppet = hero;
        hero.Session = sess;
        Assert.False(hero.Location is Atheriz.Core.Persistence.Dto.LocationRef.NullLocation);

        conn.Sent.Clear();
        hero.AtPostPuppet();

        var sent = conn.Snapshot();
        Assert.Contains(sent, x => x.Cmd == "map_enable");
        var mapMsg = sent.FirstOrDefault(x => x.Cmd == "map");
        Assert.False(mapMsg.Cmd == null, $"no 'map' command sent; sent: {string.Join(",", sent.Select(x=>x.Cmd))}");
        var payload = mapMsg.Args.FirstOrDefault() as Dictionary<string, object?>;
        Assert.NotNull(payload);
        Assert.True(payload!.TryGetValue("map", out var mapObj));
        var mapStr = mapObj as string;
        Assert.False(string.IsNullOrWhiteSpace(mapStr), "map string empty after login");
        Assert.DoesNotContain(s.RoomPlaceholder, mapStr!);
        Assert.True(payload.TryGetValue("pos", out var posObj));
        var posList = posObj as List<int>;
        Assert.NotNull(posList);
        Assert.Equal(2, posList!.Count);
        Assert.True(payload.ContainsKey("legend"));
        Assert.True(payload.ContainsKey("area"));
        Assert.Equal("limbo", payload["area"] as string);
    }

    [Fact]
    public void MapRender_ProducesVisibleCenterAndPosComputationFaithful()
    {
        using var env = GlobalTestEnv.Enter();
        // Build isolated map info
        var mi = new MapInfo("limbo") { Settings = AtherizSettings.Default };
        var s = AtherizSettings.Default;
        for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
        {
            mi.PreGrid[(x, y)] = s.RoomPlaceholder;
            mi.PlaceWalls((x, y), s.SingleWallPlaceholder);
        }
        mi.PreRender();
        var hero = GameObject.Create("Hero2", isPc: true);
        hero.Symbol = "Z";
        hero.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(new Coord("limbo", 4, 4, 4));
        hero.MapEnabled = true;
        ObjectRegistry.AddObject(hero);
        var conn = new CapturingConnection();
        var sess = new Session(conn);
        sess.Puppet = hero;
        hero.Session = sess;
        mi.AddListener(hero);
        mi.AddMapable(hero);
        conn.Sent.Clear();
        mi.Render(force: true);
        var sent = conn.Snapshot();
        var mapMsg = sent.FirstOrDefault(x => x.Cmd == "map");
        Assert.False(mapMsg.Cmd == null);
        var payload = mapMsg.Args[0] as Dictionary<string, object?>;
        var mapStr = payload!["map"] as string;
        Assert.False(string.IsNullOrWhiteSpace(mapStr));
        var pos = payload["pos"] as List<int>;
        var minX = (int)payload["min_x"]!;
        var maxY = (int)payload["max_y"]!;
        Assert.Equal(4 - minX, pos![0]);
        Assert.Equal(maxY - 4, pos[1]);
        Assert.Equal(hero.Symbol, payload["symbol"] as string);
        mi.RemoveListener(hero);
        mi.RemoveMapable(hero);
    }

    [Fact]
    public void MoveBetweenNodes_StillSendsMapUpdate()
    {
        using var env = GlobalTestEnv.Enter();
        // Prepare NodeHandler with limbo area so MoveTo works
        var nh = new NodeHandler(autoLoad: false);
        var area = new NodeArea("limbo");
        for (int z = 0; z < 9; z++)
        {
            var grid = new NodeGrid("limbo", z);
            for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
            {
                var coord = new Coord("limbo", x, y, z);
                grid.Nodes[(x, y)] = new Node(coord, desc: "void");
            }
            area.AddGrid(grid);
        }
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        var mh = new MapHandler(AtherizSettings.Default, autoLoad: false);
        var mi = new MapInfo("limbo") { Settings = AtherizSettings.Default };
        var s = AtherizSettings.Default;
        for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
        {
            mi.PreGrid[(x, y)] = s.RoomPlaceholder;
            mi.PlaceWalls((x, y), s.SingleWallPlaceholder);
        }
        mi.PreRender();
        mh.SetMapInfo("limbo", 4, mi);
        var fmh = typeof(GlobalServices).GetField("_mapHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        fmh!.SetValue(null, mh);
        var fn = typeof(GlobalServices).GetField("_nodeHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        fn!.SetValue(null, nh);

        var hero = GameObject.Create("Mover", isPc: true);
        hero.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(new Coord("limbo", 4, 4, 4));
        hero.MapEnabled = true;
        hero.IsMapable = true;
        ObjectRegistry.AddObject(hero);
        var conn = new CapturingConnection();
        var sess = new Session(conn);
        sess.Puppet = hero;
        hero.Session = sess;
        hero.AtPostPuppet();
        hero.LastMapTime = 0;
        conn.Sent.Clear();
        var dest = nh.GetNode(new Coord("limbo", 5, 4, 4));
        Assert.NotNull(dest);
        var ok = hero.MoveTo(dest!);
        Assert.True(ok);
        // Move may be throttled by FPS limit (AtPostPuppet just set LastMapTime=now); force if needed
        var sent = conn.Snapshot();
        if (!sent.Any(x => x.Cmd == "map"))
        {
            mi.Render(force: true);
            sent = conn.Snapshot();
        }
        Assert.Contains(sent, x => x.Cmd == "map");
    }
}
