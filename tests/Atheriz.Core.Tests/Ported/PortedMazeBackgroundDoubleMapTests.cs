// Regression for maze background loss due to duplicate map sends.
// Python atheriz/maze.py queues do_pathfind then move_to with no delay; C# MazeCommand does same.
// GameObject.MoveTo previously called MoveListener+MoveMapable separately for IsPc+IsMapable PCs,
// producing two map messages for same toMap. Webclient webclient/src/webclient/main.ts merges pendingBackground
// into first map then second map overwrites without background -> highlight lost. Also background sent before map
// should merge via pending.
// This test simulates webclient message ordering and asserts final mapPayload.background survives and render contains ANSI 48;2;83;128;56.
using System.Text.Json;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMazeBackgroundDoubleMapTests
{
    private sealed class CapturingConnection : BaseConnection
    {
        public readonly List<(string Cmd, List<object?> Args)> Sent = new();
        private readonly object _lock = new();
        public CapturingConnection() : base("cap-double") { }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
        {
            lock (_lock) Sent.Add((cmd, args ?? new()));
        }
        public override void Close() { }
        public List<(string Cmd, List<object?> Args)> Snapshot() { lock (_lock) return Sent.ToList(); }
    }

    private static void InjectBoth(MapHandler mh, NodeHandler nh)
    {
        NodeHandler.SetCurrent(nh);
        var fm = typeof(GlobalServices).GetField("_mapHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        fm!.SetValue(null, mh);
        var fn = typeof(GlobalServices).GetField("_nodeHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        fn!.SetValue(null, nh);
        try { MapHandlerHolder.Set(mh); } catch { }
        try
        {
            var t = typeof(GameObject).Assembly.GetType("Atheriz.Core.Objects.MapHandlerSingleton");
            var m = t?.GetMethod("Set", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            m?.Invoke(null, new object[] { mh });
        }
        catch { }
    }

    // Minimal client simulation matching webclient/src/webclient/main.ts handleMessage for map/background/unbackground
    // and webclient/src/webclient/map.ts applyBackground / renderMap
    private sealed class ClientSim
    {
        public Dictionary<string, object?>? MapPayload;
        public Dictionary<string, object?>? PendingBackground;
        public List<string> Renders = new();

        private static Dictionary<string, object?>? ParseBackground(object? value)
        {
            if (value is Dictionary<string, object?> d)
            {
                if (!d.TryGetValue("color", out var c) || c == null) return null;
                if (!d.TryGetValue("coords", out var co) || co == null) return null;
                // already parsed
                return d;
            }
            return null;
        }
        private static Dictionary<string, object?> Merge(Dictionary<string, object?>? a, Dictionary<string, object?>? b)
        {
            if (a == null) return b == null ? new() : new Dictionary<string, object?>(b);
            if (b == null) return new Dictionary<string, object?>(a);
            // simple: merge backgrounds — for test we only care that background survives, so keep b if a is null else keep a's coords+color
            // replicate mergeBackgrounds: later pending overwrites? Actually webclient mergeBackgrounds concatenates coords if same color? For test, just return b if a != null? Use b.
            return new Dictionary<string, object?>(b);
        }
        private static string RenderMapText(Dictionary<string, object?> payload, int cols = 80, int rows = 24)
        {
            // Use actual map rendering if available? For test we just check background payload influences rendered string.
            // Simulate map.ts applyBackground: inject ANSI 48;2;r;g;b before cell.
            // We don't have full grid; just produce a sentinel containing background color if present.
            if (payload.TryGetValue("background", out var bg) && bg is Dictionary<string, object?> bgd)
            {
                if (bgd.TryGetValue("color", out var col) && col is List<int> color && color.Count==3)
                {
                    return $"\u001b[48;2;{color[0]};{color[1]};{color[2]}mX\u001b[0m";
                }
                if (col is System.Collections.IEnumerable en)
                {
                    var lst = new List<int>();
                    foreach (var it in en) lst.Add(Convert.ToInt32(it));
                    if (lst.Count==3) return $"\u001b[48;2;{lst[0]};{lst[1]};{lst[2]}mX\u001b[0m";
                }
            }
            return "no-bg";
        }

        public void Handle(string cmd, List<object?> args)
        {
            switch (cmd)
            {
                case "map":
                    {
                        var p = args[0] as Dictionary<string, object?>;
                        if (p == null && args[0] is JsonElement je)
                            p = JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());
                        // clone
                        MapPayload = p == null ? null : new Dictionary<string, object?>(p);
                        if (PendingBackground != null)
                        {
                            // merge pending into mapPayload.background
                            var existing = MapPayload != null && MapPayload.TryGetValue("background", out var eb) ? eb as Dictionary<string, object?> : null;
                            MapPayload!["background"] = Merge(existing, PendingBackground);
                            PendingBackground = null;
                        }
                        if (MapPayload != null) Renders.Add(RenderMapText(MapPayload));
                        break;
                    }
                case "background":
                    {
                        var bg = args[0] as Dictionary<string, object?>;
                        if (bg == null) break;
                        if (MapPayload != null)
                        {
                            var existing = MapPayload.TryGetValue("background", out var eb) ? eb as Dictionary<string, object?> : null;
                            MapPayload["background"] = Merge(existing, bg);
                            Renders.Add(RenderMapText(MapPayload));
                        }
                        else
                        {
                            PendingBackground = Merge(PendingBackground, bg);
                        }
                        break;
                    }
                case "unbackground":
                    {
                        PendingBackground = null;
                        if (MapPayload != null)
                        {
                            MapPayload.Remove("background");
                            Renders.Add(RenderMapText(MapPayload));
                        }
                        break;
                    }
                default: break;
            }
        }
    }

    [Fact]
    public async Task Maze_BackgroundNotLostToDuplicateMap_BeforeMapAndAfterMapBothPreserved()
    {
        using var env = GlobalTestEnv.Enter();
        var mh = GlobalServices.GetMapHandler();
        var nh = GlobalServices.GetNodeHandler();
        NodeHandler.SetCurrent(nh);
        InjectBoth(mh, nh);

        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 1000);
        var origPool = MazeCommand.ThreadPoolFactory;
        var origMapF = MazeCommand.MapHandlerFactory;
        var origNodeF = MazeCommand.NodeHandlerFactory;
        MazeCommand.ThreadPoolFactory = () => pool;
        MazeCommand.MapHandlerFactory = () => mh;
        MazeCommand.NodeHandlerFactory = () => nh;
        try
        {
            var hero = GameObject.Create("MazeDouble", isPc: true);
            hero.PrivilegeLevel = Privilege.Builder;
            hero.Symbol = "D";
            hero.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(new Coord("limbo", 0, 0, 0));
            hero.MapEnabled = true;
            hero.IsMapable = true;
            ObjectRegistry.AddObject(hero);
            var conn = new CapturingConnection();
            var sess = new Session(conn);
            sess.Puppet = hero;
            hero.Session = sess;

            var cmd = new MazeCommand();
            cmd.Run(hero, null);

            // Wait for background and map (order racy)
            var gotBoth = await PortedHelpers.WaitAsync(() => conn.Snapshot().Any(x => x.Cmd == "background") && conn.Snapshot().Any(x => x.Cmd == "map"), 2000);
            Assert.True(gotBoth, $"background+map not sent; cmds: {string.Join(",", conn.Snapshot().Select(x=>x.Cmd))}");

            // Give a little time for second duplicate map to arrive if bug present
            await Task.Delay(300);
            var snap = conn.Snapshot();

            // Key assertion from user: client simulation must preserve background in final mapPayload and render must contain ANSI 48;2;83;128;56 or 90,0,0
            var sim = new ClientSim();
            foreach (var (c, a) in snap) sim.Handle(c, a);

            Assert.NotNull(sim.MapPayload);
            Assert.True(sim.MapPayload!.TryGetValue("background", out var bgObj) && bgObj != null, $"final mapPayload.background missing after replay; payload keys {string.Join(",", sim.MapPayload.Keys)} cmds order {string.Join("->", snap.Select(x=>x.Cmd))} renders {string.Join("|", sim.Renders)}");
            var bgDict = bgObj as Dictionary<string, object?>;
            Assert.NotNull(bgDict);
            // color must be valid
            var colorObj = bgDict!["color"];
            var color = new List<int>();
            if (colorObj is List<int> li) color = li;
            else if (colorObj is System.Collections.IEnumerable en) foreach (var it in en) color.Add(Convert.ToInt32(it));
            Assert.True(color.SequenceEqual(new[]{83,128,56}) || color.SequenceEqual(new[]{90,0,0}), $"unexpected color {string.Join(",", color)}");

            // Render must contain ANSI background sequence
            var lastRender = sim.Renders.LastOrDefault() ?? "";
            // Accept either green or red ANSI
            bool hasGreen = lastRender.Contains("\u001b[48;2;83;128;56");
            bool hasRed = lastRender.Contains("\u001b[48;2;90;0;0");
            Assert.True(hasGreen || hasRed, $"final render missing ANSI background; render='{lastRender}' cmds {string.Join("->", snap.Select(x=>x.Cmd))}");

            // Also assert duplicate map not present when background before map: after fix there should be exactly 1 map for first maze (not 2)
            // Count maps that correspond to maze1 area — the first maze's area is maze1
            var mapCount = snap.Count(x => x.Cmd == "map");
            // With fix, first maze should produce 1 map; bug produces 2. Allow 1 or 2 but final background must survive — strict: should be 1
            Assert.Equal(1, mapCount); // this will fail before fix (bug gives 2)
        }
        finally
        {
            MazeCommand.ThreadPoolFactory = origPool;
            MazeCommand.MapHandlerFactory = origMapF;
            MazeCommand.NodeHandlerFactory = origNodeF;
            try { pool.Stop(wait: true); } catch { }
        }
    }
}
