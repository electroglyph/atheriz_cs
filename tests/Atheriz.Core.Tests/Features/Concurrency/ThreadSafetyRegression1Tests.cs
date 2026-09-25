using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: overlapping alarm ticks, link remove-vs-add,
// deleted-node implant refusal, limiter duplicate-task balance, puppet
// double-attach refusal, map-toggle convergence.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression1Tests
{
    private sealed class CountingAlarm : GameObject
    {
        public int Fires;
        public CountingAlarm() { Name = "alarm-counter"; }
        public override void AtAlarm(GameTime.GameTimeInfo when, Dictionary<string, System.Text.Json.JsonElement>? data)
        {
            Interlocked.Increment(ref Fires);
        }
    }

    // Overlapping OnTicks must fire a one-shot alarm exactly once.
    [Fact]
    public async Task GameTime_OverlappingOnTicks_SingleAlarmFiresOnce()
    {
        using var env = GlobalTestEnv.Enter();
        using var pool = new AsyncThreadPool(maxThreads: 4);
        var gt = new GameTime(settings: null, ticker: null, pool, autoLoad: false);
        for (int i = 0; i < 50; i++)
        {
            var counter = new CountingAlarm();
            try { ObjectRegistry.AddObject(counter); } catch { }
            gt.AddAlarm("?", "?", counter.Id, repeat: false);
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() => { barrier.SignalAndWait(); gt.OnTick(); });
            var t2 = Task.Run(() => { barrier.SignalAndWait(); gt.OnTick(); });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (counter.Fires == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(1, counter.Fires);
        }
    }

    // Removing one link must not delete a fresh edge to the same dest.
    [Fact]
    public async Task RemoveLink_ConcurrentAddToSameDest_FreshEdgeSurvives()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            for (int round = 0; round < 30; round++)
            {
                var n1 = new Node(new Coord("b6a", 0, 0, 0));
                var n2 = new Node(new Coord("b6a", 1, 0, 0));
                var dest = new Coord("b6b", 5, 5, 0);
                nh.AddNode(n1);
                nh.AddNode(n2);
                n1.AddLink(new NodeLink("east", dest));
                using var barrier = new Barrier(2);
                var tRemove = Task.Run(() => { barrier.SignalAndWait(); n1.RemoveLink("east"); });
                var tAdd = Task.Run(() => { barrier.SignalAndWait(); n2.AddLink(new NodeLink("west", dest)); });
                await Task.WhenAll([tRemove, tAdd]).WaitAsync(TimeSpan.FromSeconds(30));
                var kept = n2.GetLinks().Any(l => l.Coord.Equals(dest));
                Assert.True(kept);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Plural AddObjects refuses a deleted node like singular AddObject.
    [Fact]
    public void AddObjects_DeletedNode_RefusesImplant()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("b7a", 0, 0, 0));
        var fresh = GameObject.Create("b7fresh");
        node.IsDeleted = true;
        node.AddObjects([fresh]);
        Assert.DoesNotContain(fresh.Id, node.ContentsSnapshot);
    }

    // A duplicate Task reserve is refused; one release balances.
    [Fact]
    public void PendingLimiter_DuplicateTaskReserve_RefusedAndBalanced()
    {
        var limiter = new PendingLimiter(maxBytes: 1024);
        var task = new Task(() => { });
        Assert.True(limiter.TryReserve(task, 5));
        Assert.False(limiter.TryReserve(task, 5));
        limiter.Release(task);
        Assert.Equal((0, 0, 0), limiter.Snapshot());
    }

    // A second attach on the same session is refused; no orphan.
    [Fact]
    public void SessionPuppetHelper_SecondAttach_RefusedNoOrphan()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("d3-double");
        var c1 = new GameObject { Name = "D3Char1" };
        var c2 = new GameObject { Name = "D3Char2" };
        Assert.True(SessionPuppetHelper.TryAttach(conn, c1));
        Assert.False(SessionPuppetHelper.TryAttach(conn, c2));
        Assert.Same(c1, conn.Session.Puppet);
        Assert.Same(conn.Session, c1.Session);
        Assert.Null(c2.Session);
    }

    // An even number of concurrent toggles restores the initial value.
    [Fact]
    public async Task MapToggle_ConcurrentEvenToggles_RestoresInitial()
    {
        using var env = GlobalTestEnv.Enter();
        var go = new GameObject { Name = "E23Toggle" };
        go.MapEnabled = false;
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 250; i++) go.ToggleMapEnabled();
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(go.MapEnabled);
    }
}
