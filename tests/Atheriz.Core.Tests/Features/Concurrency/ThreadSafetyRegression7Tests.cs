using Atheriz.Core.Commands;
using System.Reflection;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: follow routing, area equals churn, registry
// single publish, logger generation atomicity, clock single read, serializer
// hook flips, password-rotation detection, pool-rejection reporting.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression7Tests
{
    private sealed class SwapClock : ITimeProvider
    {
        public double MonotonicSeconds() => 1.0;
        public long MonotonicMilliseconds() => 1000;
        public double Now() => 1.0;
    }

    // A veto for another destination never steals this move's entry.
    [Fact]
    public void FollowScript_VetoOtherDest_KeepsEntry()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var n1 = new Node(new Coord("a6area", 0, 0, 0));
            var n2 = new Node(new Coord("a6area", 1, 0, 0));
            var n3 = new Node(new Coord("a6area", 2, 0, 0));
            nh.AddNode(n1);
            nh.AddNode(n2);
            nh.AddNode(n3);
            var leader = GameObject.Create("a6leader");
            try { ObjectRegistry.AddObject(leader); } catch { }
            Assert.True(leader.MoveTo(n1, force: true, announce: false));
            var follower = GameObject.Create("a6follower", isPc: true);
            try { ObjectRegistry.AddObject(follower); } catch { }
            Assert.True(follower.MoveTo(n1, force: true, announce: false));
            follower.Following = leader.Id;
            leader.AddFollower(follower.Id);
            var script = new FollowScript { Name = "FollowScript_for_a6" };
            try { ObjectRegistry.AddObject(script); } catch { }
            leader.AddScript(script);
            script.at_pre_move(n2);
            Assert.Same(n1, script.OldLoc);
            // A veto aimed at a different destination removes nothing.
            FollowScript.CancelPendingPush(leader, n3);
            Assert.Same(n1, script.OldLoc);
            script.at_post_move(n2);
            Assert.Null(script.OldLoc);
            Assert.Equal(n2.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Sequential chained moves route the follower along every hop.
    [Fact]
    public void FollowScript_ChainedMoves_RoutesEveryHop()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var r1 = new Node(new Coord("a6c", 0, 0, 0));
            var r2 = new Node(new Coord("a6c", 1, 0, 0));
            var r3 = new Node(new Coord("a6c", 2, 0, 0));
            nh.AddNode(r1);
            nh.AddNode(r2);
            nh.AddNode(r3);
            var leader = GameObject.Create("a6cleader");
            var follower = GameObject.Create("a6cfollower", isPc: true);
            try { ObjectRegistry.AddObject(leader); } catch { }
            try { ObjectRegistry.AddObject(follower); } catch { }
            Assert.True(leader.MoveTo(r1, force: true, announce: false));
            Assert.True(follower.MoveTo(r1, force: true, announce: false));
            follower.Following = leader.Id;
            leader.AddFollower(follower.Id);
            var script = new FollowScript { Name = "FollowScript_for_a6c" };
            try { ObjectRegistry.AddObject(script); } catch { }
            leader.AddScript(script);
            Assert.True(leader.MoveTo(r2, force: true, announce: false));
            Assert.True(leader.HasHook("at_pre_move"));
            Assert.True(leader.HasHook("at_post_move"));
            Assert.Null(script.OldLoc);
            // NOTE: force moves skip AtPreMove by design (no capture, so no
            // follow). Routing runs on non-force moves below.
            Assert.True(leader.MoveTo(r1, announce: false));
            Assert.Equal(r1.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
            Assert.True(leader.MoveTo(r2, announce: false));
            Assert.Equal(r2.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
            Assert.True(leader.MoveTo(r3, announce: false));
            Assert.Equal(r3.Coord, ((LocationRef.CoordLocation)follower.Location).Coord);
            Assert.Null(script.OldLoc);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Concurrent opposite leader moves never throw and drain the stack.
    [Fact]
    public async Task FollowScript_ConcurrentLeaderMoves_DrainsStack()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var valid = new HashSet<int>();
            for (int round = 0; round < 30; round++)
            {
                var r1 = new Node(new Coord("a6b", 0, round, 0));
                var r2 = new Node(new Coord("a6b", 1, round, 0));
                var r3 = new Node(new Coord("a6b", 2, round, 0));
                nh.AddNode(r1);
                nh.AddNode(r2);
                nh.AddNode(r3);
                valid.Add(r1.Id);
                valid.Add(r2.Id);
                valid.Add(r3.Id);
                var leader = GameObject.Create("a6bleader");
                var follower = GameObject.Create("a6bfollower", isPc: true);
                try { ObjectRegistry.AddObject(leader); } catch { }
                try { ObjectRegistry.AddObject(follower); } catch { }
                Assert.True(leader.MoveTo(r1, force: true, announce: false));
                Assert.True(follower.MoveTo(r1, force: true, announce: false));
                follower.Following = leader.Id;
                leader.AddFollower(follower.Id);
                var script = new FollowScript { Name = "FollowScript_for_a6b" };
                try { ObjectRegistry.AddObject(script); } catch { }
                leader.AddScript(script);
                using var barrier = new Barrier(2);
                // Non-force: force moves skip AtPreMove by design (no capture).
                var t1 = Task.Run(() => { barrier.SignalAndWait(); leader.MoveTo(r2, announce: false); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); leader.MoveTo(r3, announce: false); });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Null(script.OldLoc);
                var leaderLoc = leader.ResolveLocationObject();
                var followerLoc = follower.ResolveLocationObject();
                Assert.NotNull(leaderLoc);
                Assert.NotNull(followerLoc);
                // No wrong-room routing: both ends sit in rooms of this round.
                Assert.Contains(leaderLoc.Id, valid);
                Assert.Contains(followerLoc.Id, valid);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Link churn beside Equals never throws.
    [Fact]
    public async Task NodeArea_EqualsVsLinkChurn_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var a = new NodeArea("b4a");
        var b = new NodeArea("b4b");
        using var barrier = new Barrier(4);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var mut1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    a.AddLinkedArea("x" + (i % 50));
                    a.RemoveLinkedArea("x" + (i % 50));
                    i++;
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        });
        var mut2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    b.AddLinkedArea("y" + (i % 50));
                    b.RemoveLinkedArea("y" + (i % 50));
                    i++;
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        });
        var eq1 = Task.Run(() => { barrier.SignalAndWait(); while (!cts.Token.IsCancellationRequested) { try { _ = a.Equals(b); } catch (Exception ex) { errors.Enqueue(ex); } } });
        var eq2 = Task.Run(() => { barrier.SignalAndWait(); while (!cts.Token.IsCancellationRequested) { try { _ = a.GetHashCode() ^ b.GetHashCode(); } catch (Exception ex) { errors.Enqueue(ex); } } });
        await Task.WhenAll([mut1, mut2, eq1, eq2]).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(errors.IsEmpty);
    }

    // Concurrent first touches publish one stable instance.
    [Fact]
    public void CommandRegistry_ParallelFirstTouch_SingleInstance()
    {
        using var env = GlobalTestEnv.Enter();
        for (int round = 0; round < 20; round++)
        {
            CommandRegistry.Reset();
            object? first = null;
            bool mismatch = false;
            int count = -1;
            Parallel.For(0, 8, _ =>
            {
                var cs = CommandRegistry.LoggedIn;
                if (Interlocked.CompareExchange(ref first, cs, null) is not null && !ReferenceEquals(first, cs))
                    mismatch = true;
                Volatile.Write(ref count, cs.GetAll().Count);
            });
            Assert.False(mismatch);
            Assert.True(count > 0);
        }
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Core", "Commands", "CommandRegistry.cs");
        Assert.Contains("Volatile.Read(ref field)", src);
    }

    // Concurrent applies never publish a mixed path/level generation.
    [Fact]
    public async Task Logger_ConcurrentApplySettings_NeverMixed()
    {
        using var env = GlobalTestEnv.Enter();
        var a = new Atheriz.Core.Settings.AtherizSettings { SavePath = "saveA", LogLevel = "debug" };
        var b = new Atheriz.Core.Settings.AtherizSettings { SavePath = "saveB", LogLevel = "error" };
        using var barrier = new Barrier(2);
        var t1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 300; i++)
            {
                AtherizLogger.ApplySettings(a);
                var (path, level) = AtherizLogger.SnapshotSettings();
                Assert.True((path == "saveA" && level == Microsoft.Extensions.Logging.LogLevel.Debug) || (path == "saveB" && level == Microsoft.Extensions.Logging.LogLevel.Error));
            }
        });
        var t2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 300; i++) AtherizLogger.ApplySettings(i % 2 == 0 ? a : b);
        });
        await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
        AtherizLogger.ApplySettings();
    }

    // Swapping the clock mid-read never mixes generations.
    [Fact]
    public async Task GameClock_ConcurrentSwap_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        var orig = GameClock.Default;
        try
        {
            using var barrier = new Barrier(9);
            var readers = Enumerable.Range(0, 8).Select(idx => Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 20000; i++) _ = GameClock.MonotonicSeconds();
            })).ToArray();
            var swapper = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 100; i++)
                    GameClock.Default = i % 2 == 0 ? (ITimeProvider)new SwapClock() : orig;
            });
            await Task.WhenAll([.. readers, swapper]).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { GameClock.Default = orig; }
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Core", "Utils", "GameClock.cs");
        Assert.Contains("var clock = Default", src);
    }

    // Hook flips never tear a call.
    [Fact]
    public async Task DtoSerializer_ConcurrentHookFlip_NeverTears()
    {
        using var env = GlobalTestEnv.Enter();
        var dto = new GameObjectDto();
        int custom = 0, plain = 0;
        using var barrier = new Barrier(2);
        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 5000; i++)
            {
                var s = GameObjectDtoSerializer.ToJson(dto);
                if (s == "CUSTOM") Interlocked.Increment(ref custom);
                else Interlocked.Increment(ref plain);
            }
        });
        var flip = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 500; i++)
            {
                GameObjectDtoSerializer.ToJsonHook = _ => "CUSTOM";
                GameObjectDtoSerializer.ToJsonHook = null;
            }
        });
        await Task.WhenAll([writer, flip]).WaitAsync(TimeSpan.FromSeconds(30));
        GameObjectDtoSerializer.ToJsonHook = null;
        Assert.Equal(5000, custom + plain);
    }

    // An interleaved rotation is detected, never overwritten.
    [Fact]
    public void ChangePassword_RotationMidway_RefusesInsteadOfOverwrite()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("f21acc", "oldpw12345");
        Assert.False(acc.ChangePassword("wrongpw", "new1"));
        Assert.True(acc.CheckPassword("oldpw12345"));
        // Interleaved rotation between another thread's check and set.
        acc.SetPassword("new2rot");
        Assert.False(acc.ChangePassword("oldpw12345", "new1"));
        Assert.True(acc.CheckPassword("new2rot"));
        Assert.True(acc.ChangePassword("new2rot", "new3"));
        Assert.True(acc.CheckPassword("new3"));
    }

    // Pool rejection reports false; acceptance reports true.
    [Fact]
    public async Task EmitSound_FullPool_ReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        using var pool = new AsyncThreadPool(maxThreads: 1, queueLimit: 1, reliefLimit: 0);
        GlobalServices.SetAsyncThreadPool(pool);
        var park = new ManualResetEventSlim(false);
        Assert.True(pool.AddTask(() => park.Wait(TimeSpan.FromSeconds(15)), "park"));
        // Wait until the worker actually picked up the parked task: adding
        // the filler while it is still queued would reject it (queueLimit 1)
        // for scheduling reasons instead of the intended full-pool shape.
        var pickup = System.Diagnostics.Stopwatch.StartNew();
        while (pool.QueueCount > 0 && pickup.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(10);
        Assert.Equal(0, pool.QueueCount);
        // Fill the single queue slot so the next submit rejects.
        Assert.True(pool.AddTask(() => { }, "filler"));
        var room = new Node(new Coord("a13area", 0, 0, 0));
        var speaker = GameObject.Create("a13speaker");
        var hearer = GameObject.Create("a13hearer");
        hearer.CanHear = true;
        try { ObjectRegistry.AddObject(speaker); } catch { }
        try { ObjectRegistry.AddObject(hearer); } catch { }
        Assert.True(speaker.MoveTo(room, force: true, announce: false));
        Assert.True(hearer.MoveTo(room, force: true, announce: false));
        hearer.ClearMessages();
        Assert.False(speaker.EmitSound("bang", "BANG", 1.0));
        park.Set();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool accepted = false;
        while (!accepted && sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            accepted = speaker.EmitSound("bang", "BANG", 1.0);
            if (!accepted) await Task.Delay(20);
        }
        Assert.True(accepted);
    }
}
