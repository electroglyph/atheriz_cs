using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: add/remove-vs-move churn, same-session puppet
// churn, post-disconnect send drop, account-pair atomicity, deleted-puppet
// resurrection refusal, superseded cross-map skip, dispatch arg isolation.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression4Tests
{
    // Container add/remove churn vs MoveTo churn completes (no ABBA
    // deadlock) with the object single-homed and never in a deleted parent.
    [Fact]
    public async Task AddRemove_MoveTo_Churn_CompletesSingleHomed()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var holder = GameObject.Create("a1holder", isContainer: true);
            try { ObjectRegistry.AddObject(holder); } catch { }
            var r1 = new Node(new Coord("a1area", 0, 0, 0));
            var r2 = new Node(new Coord("a1area", 1, 0, 0));
            nh.AddNode(r1);
            nh.AddNode(r2);
            var item = GameObject.Create("a1item", isItem: true);
            Assert.True(item.MoveTo(r1, force: true, announce: false));
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 2000; i++)
                {
                    holder.AddObject(item);
                    holder.RemoveObject(item);
                }
            });
            var t2 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < 2000; i++)
                    item.MoveTo((i & 1) == 0 ? r1 : r2, force: true, announce: false);
            });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(item.MoveTo(r1, force: true, announce: false));
            var loc = item.ResolveLocationObject();
            Assert.NotNull(loc);
            int holders = (holder.ContentsSnapshot.Contains(item.Id) ? 1 : 0)
                + (r1.ContentsSnapshot.Contains(item.Id) ? 1 : 0)
                + (r2.ContentsSnapshot.Contains(item.Id) ? 1 : 0);
            Assert.Equal(1, holders);
            Assert.Equal(loc.Id, r1.Id);
            Assert.False(loc.IsDeleted);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Same-session puppet churn stays balanced and restores privilege.
    [Fact]
    public async Task Puppet_SameSession_Churn_BalancedRestores()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("a9churn");
        var sess = conn.Session;
        var hero = GameObject.Create("a9hero", isPc: true);
        hero.PrivilegeLevel = Privilege.Builder;
        var npc = GameObject.Create("a9npc", isNpc: true);
        npc.PrivilegeLevel = Privilege.Guest;
        npc.IsPc = false;
        hero.Session = sess;
        sess.Puppet = hero;
        // A stale second puppet on the live session is refused outright.
        var rival = GameObject.Create("a9rival", isPc: true);
        rival.PrivilegeLevel = Privilege.Builder;
        rival.Session = sess;
        Assert.True(hero.Puppet(sess, npc));
        Assert.False(rival.Puppet(sess, npc));
        Assert.True(hero.Unpuppet(sess));
        var churn = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
                if (hero.Puppet(sess, npc))
                    Assert.True(hero.Unpuppet(sess));
        })).ToArray();
        await Task.WhenAll(churn).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(sess.PuppetStack);
        Assert.False(npc.IsPc);
        Assert.Equal(Privilege.Guest, npc.PrivilegeLevel);
    }

    // Sends after disconnect are dropped.
    [Fact]
    public void Session_Msg_AfterDisconnect_Dropped()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("a12drop");
        var sess = conn.Session;
        sess.Msg("before");
        Assert.NotEmpty(conn.Sent);
        conn.ClearSent();
        sess.AtDisconnect();
        sess.Msg("late");
        Assert.Empty(conn.Sent);
    }

    // Account/id reads never tear across swaps.
    [Fact]
    public async Task Session_AccountPair_NeverTorn()
    {
        using var env = GlobalTestEnv.Enter();
        var sess = new Session();
        var accA = new Account { Name = "A12A" };
        var accB = new Account { Name = "A12B" };
        sess.Account = accA;
        using var barrier = new Barrier(2);
        var swap = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 5000; i++)
                sess.Account = (i & 1) == 0 ? accA : accB;
        });
        var read = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 5000; i++)
            {
                var (acc, id) = sess.GetAccountPair();
                Assert.True((acc is null && id is null) || (acc is not null && id == acc.Id));
            }
        });
        await Task.WhenAll([swap, read]).WaitAsync(TimeSpan.FromSeconds(30));
    }

    // A deleted puppet's login tail must not resurrect listeners.
    [Fact]
    public void AtPostPuppet_DeletedPuppet_NoResurrection()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("a11chan");
        var npc = GameObject.Create("a11npc", isNpc: true);
        try { ObjectRegistry.AddObject(ch); } catch { }
        try { ObjectRegistry.AddObject(npc); } catch { }
        npc.Subscribe(ch);
        Assert.Contains(ch.Id, npc.ChannelsSnapshot);
        // Simulate teardown landing after the login tail's snapshot: the
        // listener is gone but the membership entry is still queued.
        ch.RemoveListener(npc);
        npc.IsDeleted = true;
        npc.AtPostPuppet();
        Assert.DoesNotContain(npc.Id, ch.Listeners);
    }

    // A superseded cross-map move skips instead of dual-homing.
    [Fact]
    public async Task MapMove_SupersededCrossMap_Skips()
    {
        using var env = GlobalTestEnv.Enter();
        var mh = new MapHandler();
        var mover = GameObject.Create("b8mover", isPc: true);
        mover.IsMapable = true;
        var coordA = new Coord("b8a", 0, 0, 0);
        var coordB = new Coord("b8b", 1, 0, 0);
        var coordC = new Coord("b8c", 2, 0, 0);
        mover.Location = Atheriz.Core.Persistence.Dto.LocationRef.FromCoord(coordB);
        mh.MoveListenerAndMapable(mover, coordB, coordA);
        using var barrier = new Barrier(2);
        // T1 refreshes the live presence; T2 replays a stale A->C move.
        var t1 = Task.Run(() => { barrier.SignalAndWait(); mh.MoveListenerAndMapable(mover, coordB, coordA); });
        var t2 = Task.Run(() => { barrier.SignalAndWait(); mh.MoveListenerAndMapable(mover, coordC, coordA); });
        await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
        var inB = mh.GetMapInfo("b8b", 0);
        var inC = mh.GetMapInfo("b8c", 0);
        bool hasB = (inB?.Listeners.ContainsKey(mover.Id) == true) || (inB?.Objects.ContainsKey(mover.Id) == true);
        bool hasC = (inC?.Listeners.ContainsKey(mover.Id) == true) || (inC?.Objects.ContainsKey(mover.Id) == true);
        Assert.True(hasB);
        Assert.False(hasC);
    }

    // Caller mutation after Dispatch never reaches the handler.
    [Fact]
    public void Dispatch_CallerMutation_Isolated()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { StripInputEscapeSequences = false };
        var mgr = PortedHelpers.MakeManager(settings);
        try
        {
            var gate = new ManualResetEventSlim(false);
            var seen = new List<string?>();
            var lk = new object();
            mgr.RegisterHandler("text",
                (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c, a, k) =>
                {
                    gate.Wait(5000);
                    lock (lk) seen.Add(a.Count > 0 ? a[0]?.ToString() : null);
                }));
            var conn = new TestConnection("c8iso");
            var args = new List<object?> { "orig" };
            var kwargs = new Dictionary<string, object?> { ["k"] = "v" };
            mgr.Dispatch(conn, "text", args, kwargs);
            args[0] = "mutated";
            kwargs["k"] = "mutated";
            gate.Set();
            Assert.True(PortedHelpers.WaitFor(() => { lock (lk) return seen.Count > 0; }, 5000));
            lock (lk) Assert.Equal(["orig"], seen);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }
}
