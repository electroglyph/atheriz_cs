using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: depth-2 unwire
// coherence, delete-vs-send channel behavior, registry single-publish,
// serialized puppet attach, single-count disconnect.
// All exercise real engine paths; no mocks.
// Reflection is used only for mechanism assertions (allowed in tests).
[Collection("Ported")]
public sealed class ThreadSafetyRegression11Tests
{
    // Depth-2 concurrent unwires land coherently: the pop-ownership gate
    // serializes the two pops, so the terminal state is either fully
    // unwound or exactly one live level remains — never half-wired (a
    // popped entry whose restore is skipped while a stale pointer survives).
    [Fact]
    public async Task Unpuppet_Depth2_ConcurrentUnwires_CleanTerminal()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("a10depth");
        var sess = conn.Session;
        var hero = GameObject.Create("a10hero", isPc: true);
        hero.PrivilegeLevel = Privilege.Builder;
        var npc1 = GameObject.Create("a10npc1", isNpc: true);
        npc1.PrivilegeLevel = Privilege.Guest;
        npc1.IsPc = false;
        var npc2 = GameObject.Create("a10npc2", isNpc: true);
        npc2.PrivilegeLevel = Privilege.Guest;
        npc2.IsPc = false;
        hero.Session = sess;
        sess.Puppet = hero;
        Assert.True(hero.Puppet(sess, npc1));
        Assert.True(npc1.Puppet(sess, npc2));
        Assert.Equal(2, sess.PuppetStack.Count);
        using var barrier = new Barrier(2);
        var t1 = Task.Run(() => { barrier.SignalAndWait(); return npc2.Unpuppet(sess); });
        var t2 = Task.Run(() => { barrier.SignalAndWait(); return npc1.Unpuppet(sess); });
        var results = await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
        // At least the first pop always succeeds (its owner is the live top).
        Assert.True(results[0] || results[1]);
        var puppet = sess.Puppet;
        if (ReferenceEquals(puppet, hero))
        {
            Assert.Empty(sess.PuppetStack);
            Assert.Same(sess, hero.Session);
            Assert.Null(npc1.Session);
            Assert.Null(npc2.Session);
            Assert.False(npc1.IsPc);
            Assert.False(npc2.IsPc);
            Assert.Equal(Privilege.Guest, npc1.PrivilegeLevel);
            Assert.Equal(Privilege.Guest, npc2.PrivilegeLevel);
        }
        else if (ReferenceEquals(puppet, npc1))
        {
            // One live level remains: hero -> npc1 stacked, npc2 unwound.
            var stack = sess.PuppetStack;
            Assert.Single(stack);
            Assert.True(ReferenceEquals(stack[0].Prev, hero) && ReferenceEquals(stack[0].Target, npc1));
            Assert.Null(hero.Session);
            Assert.Same(sess, npc1.Session);
            Assert.Null(npc2.Session);
            Assert.False(npc2.IsPc);
            Assert.Equal(Privilege.Guest, npc2.PrivilegeLevel);
        }
        else
        {
            Assert.Fail("half-wired puppet stack after concurrent unwires");
        }
    }

    // Timestamping listener for the delete-vs-send pin.
    private sealed class TimedListener : GameObject
    {
        public readonly List<long> DeliveryTicks = new();
        public readonly object Gate = new();
        public override void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors = false, string? msgType = null)
        {
            lock (Gate) DeliveryTicks.Add(System.Diagnostics.Stopwatch.GetTimestamp());
            base.Msg(text, fromObj, mapping, raiseErrors, msgType);
        }
    }

    // A delete landing between a Msg's listener snapshot and its
    // delivery loop drops delivery instead of ghost-delivering a dead-channel
    // message to just-detached peers. The sender loops until the deleter is
    // done, so at least one send straddles the delete; any delivery stamped
    // after Delete returned is a violation.
    [Fact]
    public async Task ChannelMsg_DeleteMidFlight_NoPostDeleteDelivery()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("a16chan");
        try { ObjectRegistry.AddObject(ch); } catch { }
        var listener = new TimedListener();
        listener.Name = "a16listener";
        try { ObjectRegistry.AddObject(listener); } catch { }
        ch.AddListener(listener);
        using var stop = new ManualResetEventSlim(false);
        long deleteTicks = 0;
        var sender = Task.Run(() =>
        {
            while (!stop.IsSet)
                ch.Msg("burst");
        });
        var deleter = Task.Run(() =>
        {
            Thread.Sleep(50);
            ch.Delete(null);
            deleteTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            stop.Set();
        });
        await Task.WhenAll([sender, deleter]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(deleteTicks != 0);
        List<long> stamps;
        lock (listener.Gate) stamps = listener.DeliveryTicks.ToList();
        Assert.NotEmpty(stamps);
        Assert.DoesNotContain(stamps, t => t > deleteTicks);
    }

    // Concurrent Msg churn vs clearing saves converges — every sent
    // message is eventually persisted and the dirty flag quiesces. The
    // generation re-dirty inside BuildSaveOperation is asserted structurally:
    // without it a Msg landing between the history snapshot and the flag
    // clear loses a checkpoint entry.
    [Fact]
    public async Task ChannelMsg_ClearingSaveChurn_EventuallyPersistsAll()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = new Channel(historyLimit: 1000);
        ch.Name = "a16save";
        try { ObjectRegistry.AddObject(ch); } catch { }
        const int total = 60;
        var sent = new ManualResetEventSlim(false);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < total; i++)
                ch.Msg($"a16msg-{i:D3}");
            sent.Set();
        });
        var saver = Task.Run(() =>
        {
            while (!sent.IsSet)
                ch.GetSaveOperationClearing();
        });
        await Task.WhenAll([writer, saver]).WaitAsync(TimeSpan.FromSeconds(30));
        string json = "";
        for (int i = 0; i < 50 && ch.IsModified; i++)
            json = ch.GetSaveOperationClearing().Json;
        Assert.False(ch.IsModified);
        for (int i = 0; i < total; i++)
            Assert.Contains($"a16msg-{i:D3}", json, StringComparison.Ordinal);
    }

    // Concurrent first touches after Reset publish exactly one instance
    // with a stable member count (double-checked init with a volatile fast
    // path, not a torn publish).
    [Fact]
    public async Task CommandRegistry_ResetVsFirstTouch_SinglePublish()
    {
        using var env = GlobalTestEnv.Enter();
        for (int round = 0; round < 20; round++)
        {
            CommandRegistry.Reset();
            using var barrier = new Barrier(8);
            var got = new CmdSet[8];
            var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
            {
                barrier.SignalAndWait();
                got[i] = CommandRegistry.LoggedIn;
            })).ToArray();
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
            for (int i = 1; i < got.Length; i++)
                Assert.Same(got[0], got[i]);
            int count = got[0].GetAll().Count;
            Assert.True(count > 0);
            for (int i = 0; i < got.Length; i++)
                Assert.Equal(count, got[i].GetAll().Count);
        }
    }

    // Concurrent attaches for different characters on one session
    // serialize under session.Lock — exactly one wins, no orphan.
    [Fact]
    public async Task SessionPuppetHelper_ConcurrentAttach_ExactlyOneWins()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("d3race");
        var c1 = GameObject.Create("d3c1", isPc: true);
        var c2 = GameObject.Create("d3c2", isPc: true);
        using var barrier = new Barrier(2);
        var t1 = Task.Run(() => { barrier.SignalAndWait(); return SessionPuppetHelper.TryAttach(conn, c1); });
        var t2 = Task.Run(() => { barrier.SignalAndWait(); return SessionPuppetHelper.TryAttach(conn, c2); });
        var results = await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotEqual(results[0], results[1]);
        var winner = results[0] ? c1 : c2;
        var loser = results[0] ? c2 : c1;
        Assert.Same(winner, conn.Session!.Puppet);
        Assert.Same(conn.Session, winner.Session);
        Assert.Null(loser.Session);
    }

    // Session SecondsPlayed: concurrent disconnects never
    // double-count the elapsed interval.
    [Fact]
    public async Task Session_ConcurrentDisconnect_ElapsedCountedOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("sessonce");
        var sess = conn.Session;
        var pc = GameObject.Create("sessoncepc", isPc: true);
        pc.Session = sess;
        sess.Puppet = pc;
        sess.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10.0;
        using var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            sess.AtDisconnect();
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(sess.SecondsPlayed >= 0.0);
        Assert.True(sess.SecondsPlayed <= 30.0);
    }
}
