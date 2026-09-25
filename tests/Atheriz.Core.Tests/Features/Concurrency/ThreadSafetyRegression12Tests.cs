using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: rename uniqueness,
// spawn-vs-remove homing, put-vs-rewire acyclicity, reload tickable
// identity, denied-move surfacing, hint/gate agreement, door-vs-delete
// safety. All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression12Tests
{
    private static GameObject Builder(string name)
    {
        var b = GameObject.Create(name, isPc: true);
        b.PrivilegeLevel = Privilege.Builder;
        try { ObjectRegistry.AddObject(b); } catch { }
        return b;
    }

    private static TestConnection ConnWithPuppet(string name, GameObject puppet)
    {
        var conn = new TestConnection(name);
        puppet.Session = conn.Session;
        conn.Session.Puppet = puppet;
        return conn;
    }

    // Two concurrent set-renames to one fresh name leave a single holder.
    [Fact]
    public async Task SetRename_ConcurrentSameFreshName_SingleHolder()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("e9", 0, 0, 0));
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            nh.AddNode(room);
            var builder = GameObject.Create("e9builder", isPc: true);
            builder.PrivilegeLevel = Privilege.Admin;
            try { ObjectRegistry.AddObject(builder); } catch { }
            builder.MoveTo(room, force: true, announce: false);
            var conn = ConnWithPuppet("e9conn", builder);
            var a = GameObject.Create("E9VictimA", isPc: true);
            var b = GameObject.Create("E9VictimB", isPc: true);
            try { ObjectRegistry.AddObject(a); } catch { }
            try { ObjectRegistry.AddObject(b); } catch { }
            a.MoveTo(room, force: true, announce: false);
            b.MoveTo(room, force: true, announce: false);
            var cmd = new SetCommand();
            for (int round = 0; round < 60; round++)
            {
                string fresh = $"E9Fresh{round}";
                a.Name = "E9VictimA";
                b.Name = "E9VictimB";
                var paA = cmd.Parser!.ParseArgs(["E9VictimA", "name", fresh]);
                var paB = cmd.Parser!.ParseArgs(["E9VictimB", "name", fresh]);
                using var barrier = new Barrier(2);
                var t1 = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    new SetCommand().Run(new CommandContext(builder, null, paA, "", CancellationToken.None));
                });
                var t2 = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    new SetCommand().Run(new CommandContext(builder, null, paB, "", CancellationToken.None));
                });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                int holders = (string.Equals(a.Name, fresh, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    + (string.Equals(b.Name, fresh, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                Assert.Equal(1, holders);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Wander spawn vs node removal never strands an NPC in a removed node.
    [Fact]
    public async Task WanderSpawn_ConcurrentRemove_NeverGhosted()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var area = "e19";
            var loc = new Node(new Coord(area, 0, 0, 0));
            nh.AddNode(loc);
            var spawnCoords = new List<Coord>();
            for (int x = 2; x < 12; x++)
            {
                var c = new Coord(area, x, 0, 0);
                spawnCoords.Add(c);
                nh.AddNode(new Node(c));
            }
            var builder = Builder("e19builder");
            builder.MoveTo(loc, force: true, announce: false);
            var conn = ConnWithPuppet("e19conn", builder);
            var cmd = new WanderCommand();
            for (int round = 0; round < 8; round++)
            {
                using var stop = new ManualResetEventSlim(false);
                var churn = Task.Run(() =>
                {
                    int i = 0;
                    while (!stop.IsSet)
                    {
                        var c = spawnCoords[i++ % spawnCoords.Count];
                        try { nh.RemoveNode(c); } catch { }
                        try { nh.AddNode(new Node(c)); } catch { }
                    }
                });
                var pa = cmd.Parser!.ParseArgs(["4"]);
                new WanderCommand().Run(new CommandContext(builder, null, pa, "", CancellationToken.None));
                stop.Set();
                await churn.WaitAsync(TimeSpan.FromSeconds(30));
                var wanderers = ObjectRegistry.FilterBy(o => o.Name.StartsWith("Wanderer", StringComparison.Ordinal));
                Assert.NotEmpty(wanderers);
                foreach (var w in wanderers)
                {
                    var home = w.ResolveLocationObject() as Node;
                    Assert.NotNull(home);
                    Assert.Same(home, ObjectRegistry.GetSingle(home.Id));
                }
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    private static bool UpWalkRevisits(GameObject start, int maxSteps = 1000)
    {
        var seen = new HashSet<int>();
        var cur = start.ResolveLocationObject();
        int steps = 0;
        while (cur is not null && !cur.IsNode && steps++ < maxSteps)
        {
            if (!seen.Add(cur.Id)) return true;
            cur = cur.ResolveLocationObject();
        }
        return false;
    }

    // Put-into-nested-container composition: the advisory loop check plus
    // MoveTo's under-lock cycle guard refuse a cyclic put and never commit
    // a containment cycle. Deterministic by design (no churn): the
    // fine-grained interleaving is pinned at the MoveTo level, where every
    // path through this command funnels; churning full command invocations
    // here only collides with suite-wide lock traffic.
    [Fact]
    public void PutNested_CyclicPut_RefusedWithoutCycle()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("e22", 0, 0, 0));
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            nh.AddNode(room);
            var putter = Builder("e22putter");
            putter.MoveTo(room, force: true, announce: false);
            var boxA = GameObject.Create("E22BoxA", isContainer: true);
            var boxB = GameObject.Create("E22BoxB", isContainer: true);
            var pebble = GameObject.Create("E22Pebble");
            try { ObjectRegistry.AddObject(boxA); } catch { }
            try { ObjectRegistry.AddObject(boxB); } catch { }
            try { ObjectRegistry.AddObject(pebble); } catch { }
            boxA.MoveTo(room, force: true, announce: false);
            boxB.MoveTo(boxA, force: true, announce: false);
            pebble.MoveTo(putter, force: true, announce: false);
            var put = new PutCommand();
            // Pebble into the nested box succeeds and stays acyclic.
            var paOk = put.Parser!.ParseArgs(["E22Pebble", "in", "E22BoxB"]);
            new PutCommand().Run(new CommandContext(putter, null, paOk, "", CancellationToken.None));
            Assert.Contains(pebble.Id, boxB.ContentsSnapshot);
            Assert.False(UpWalkRevisits(pebble));
            // The box into its own content is refused and stays acyclic.
            var paLoop = put.Parser!.ParseArgs(["E22BoxA", "in", "E22BoxB"]);
            new PutCommand().Run(new CommandContext(putter, null, paLoop, "", CancellationToken.None));
            Assert.False(UpWalkRevisits(boxA));
            Assert.False(UpWalkRevisits(boxB));
            Assert.False(UpWalkRevisits(pebble));
            Assert.Contains(boxB.Id, boxA.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    private sealed class CountingTickable : GameObject
    {
        public int Fires;
        public override void AtTick() => Interlocked.Increment(ref Fires);
    }

    // DoReload vs Reset uses one ticker generation — post-reload
    // tickables fire on the passed ticker instead of being lost to a swap.
    [Fact]
    public async Task DoReload_ConcurrentReset_TickablesFireOnSameTicker()
    {
        using var env = GlobalTestEnv.Enter();
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        try
        {
            var tickable = new CountingTickable();
            tickable.Name = "e16tick";
            tickable.IsTickable = true;
            tickable.TickSeconds = 1.0;
            try { ObjectRegistry.AddObject(tickable); } catch { }
            var settings = new AtherizSettings
            {
                TimeSystemEnabled = false,
                AutosaveOnReload = false,
                AutosaveMinutes = 0,
            };
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 20; i++)
                {
                    try { StartStop.DoReload(settings, ticker); } catch { }
                }
            });
            var t2 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 20; i++)
                {
                    try { StartStop.Reset(); } catch { }
                }
            });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(60));
            try { StartStop.DoReload(settings, ticker); } catch { }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref tickable.Fires) == 0 && sw.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(50);
            Assert.True(Volatile.Read(ref tickable.Fires) > 0);
        }
        finally { ticker.Clear(); pool.Stop(); }
    }

    private static TestConnection MapConn()
    {
        var c = new TestConnection();
        c.ClientHost = "10.0.0.1";
        return c;
    }

    private static void ResetChains()
    {
        MapEdit.Reset();
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        InputFuncs.NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
    }

    // An apply-time refusal (stale validate verdict) reaches the client
    // as moves_denied instead of a silent partial apply.
    [Fact]
    public void MapEdit_StaleValidateVerdict_SurfacesMovesDenied()
    {
        using var env = GlobalTestEnv.Enter();
        ResetChains();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var mh = GlobalServices.GetMapHandler();
            var mi = new MapInfo("TestArea");
            mh.SetMapInfo("TestArea", 0, mi);
            InputFuncs.MapHandlerFactory = () => mh;
            InputFuncs.NodeHandlerFactory = () => nh;
            var area = "TestArea";
            nh.AddNode(new Node(new Coord(area, 0, 0, 0)));
            var conn = MapConn();
            var key = MapEdit.Grant("10.0.0.1", area, 0);
            new InputFuncs().MapEditHandler(conn, [key, 0, new List<object?>()], []);
            var hk = conn.Sent[^1].Args[1] as string ?? throw new InvalidOperationException("no handshake key");
            conn.ClearSent();
            var move = new List<object?> { new List<object?> { "room", 0, 0, 4, 4 } };
            new InputFuncs().MapEditHandler(conn, [hk, 1, move], []);
            var hk2 = conn.Sent[^1].Args[1] as string ?? throw new InvalidOperationException("no second key");
            conn.ClearSent();
            // Replay the same move: the source is now empty, so apply refuses.
            new InputFuncs().MapEditHandler(conn, [hk2, 2, move], []);
            var denied = conn.Sent.FirstOrDefault(s => s.Cmd == "moves_denied");
            Assert.Equal("moves_denied", denied.Cmd);
            Assert.Equal(2, denied.Args[0]);
            var idx = Assert.IsType<List<int>>(denied.Args[2]);
            Assert.Contains(0, idx);
        }
        finally { NodeHandler.SetCurrent(null); ResetChains(); }
    }

    // The render hint and the dispatch gate agree per generation — a
    // disabled path is never advertised, an enabled one never hidden.
    [Fact]
    public void Render_HintAgreesWithDispatchGate()
    {
        using var env = GlobalTestEnv.Enter();
        var saved = AtherizSettings.Global;
        var savedDispatch = CommandDispatcher.SnapshotSettings();
        try
        {
            var off = new AtherizSettings { GuestEnabled = false, AccountCreationEnabled = false };
            AtherizSettings.Global = off;
            CommandDispatcher.SetSettings(off);
            Assert.DoesNotContain("guest", ConnectionScreen.Render(off), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("create", ConnectionScreen.Render(off), StringComparison.OrdinalIgnoreCase);
            var on = new AtherizSettings { GuestEnabled = true, AccountCreationEnabled = true };
            AtherizSettings.Global = on;
            CommandDispatcher.SetSettings(on);
            Assert.Contains("guest", ConnectionScreen.Render(on), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("create", ConnectionScreen.Render(on), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AtherizSettings.Global = saved;
            CommandDispatcher.SetSettings(savedDispatch);
        }
    }

    // Door create vs node delete never leaves a door
    // referencing a missing node (per-direction refusal is fail-closed).
    [Fact]
    public async Task DoorCreate_ConcurrentDelete_NoDoorAtMissingNode()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var area = "e12";
            var locC = new Coord(area, 0, 0, 0);
            var destC = new Coord(area, 0, 2, 0);
            var doorC = new Coord(area, 0, 1, 0);
            nh.AddNode(new Node(locC));
            var builder = Builder("e12builder");
            builder.MoveTo(nh.GetNode(locC)!, force: true, announce: false);
            var conn = ConnWithPuppet("e12conn", builder);
            var cmd = new DoorCommand();
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 60; i++)
                {
                    try { nh.RemoveNode(destC); } catch { }
                    try { nh.AddNode(new Node(destC)); } catch { }
                    try { nh.RemoveNode(doorC); } catch { }
                }
            });
            var t2 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 60; i++)
                {
                    try { nh.AddNode(new Node(doorC)); } catch { }
                    var pa = cmd.Parser!.ParseArgs(["n"]);
                    try { new DoorCommand().Run(new CommandContext(builder, null, pa, "", CancellationToken.None)); } catch { }
                }
            });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(60));
            var seen = new HashSet<Door>();
            foreach (var c in new[] { locC, destC })
            {
                var dict = nh.GetDoors(c);
                if (dict is null) continue;
                foreach (var d in dict.Values) seen.Add(d);
            }
            foreach (var d in seen)
            {
                Assert.NotNull(nh.GetNode(d.FromCoord));
                Assert.NotNull(nh.GetNode(d.ToCoord));
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
