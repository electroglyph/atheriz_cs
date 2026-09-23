using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Regression pins for the race.md audit fixes (v0.3.0.0). All exercise real
// engine paths with tight stress loops; none use mocks.
[Collection("Ported")]
public sealed class RaceFixRegressionTests
{
    // R6: two threads moving the same object to different rooms must never
    // leave it listed in both rooms' contents. Pre-fix the loser proceeded
    // with a stale source (multiHome ~6/25 rounds in the /tmp probe).
    [Fact]
    public void MoveTo_ConcurrentSameObjectMoves_NeverDualHomed()
    {
        using var env = GlobalTestEnv.Enter();
        var r1 = new Node(new Coord("racearea", 0, 0, 0));
        var r2 = new Node(new Coord("racearea", 1, 0, 0));
        var r3 = new Node(new Coord("racearea", 2, 0, 0));
        try { ObjectRegistry.AddObject(r1); } catch { }
        try { ObjectRegistry.AddObject(r2); } catch { }
        try { ObjectRegistry.AddObject(r3); } catch { }
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            nh.AddNode(r1);
            nh.AddNode(r2);
            nh.AddNode(r3);
            var mover = PortedHelpers.MakeCaller("racemover");
            try { ObjectRegistry.AddObject(mover); } catch { }
            var rooms = new[] { r1, r2, r3 };
            using var barrier = new Barrier(2);
            for (int i = 0; i < 100; i++)
            {
                foreach (var r in rooms) { try { r.RemoveObject(mover); } catch { } }
                Assert.True(mover.MoveTo(r1, force: true, announce: false));
                var tA = Task.Run(() => { barrier.SignalAndWait(); mover.MoveTo(r2, force: true, announce: false); });
                var tB = Task.Run(() => { barrier.SignalAndWait(); mover.MoveTo(r3, force: true, announce: false); });
                Assert.True(Task.WaitAll([tA, tB], TimeSpan.FromSeconds(30)));
                var holders = rooms.Where(r => r.ContentsSnapshot.Contains(mover.Id)).ToList();
                var loc = mover.ResolveLocationObject();
                Assert.True(holders.Count == 1 && loc is not null && holders[0].Id == loc.Id);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // R3: a Delete landing between peer-add and command install must not
    // orphan a live channel command. Statistical pin (window is narrow;
    // the /tmp probe fires ~2/5000): fails pre-fix with high probability.
    [Fact]
    public void Subscribe_Delete_Race_NeverOrphansCommand()
    {
        using var env = GlobalTestEnv.Enter();
        for (int i = 0; i < 3000; i++)
        {
            var ch = Channel.Create("racechan" + i);
            var peer = GameObject.Create("racepeer" + i, isPc: true);
            try { ObjectRegistry.AddObject(ch); } catch { }
            try { ObjectRegistry.AddObject(peer); } catch { }
            // Start barrier: the orphan window only opens under genuine
            // overlap. A pooled wait may inline the delegates sequentially,
            // which would pass all 3000 iterations without racing.
            using var start = new Barrier(2);
            var tA = Task.Run(() => { start.SignalAndWait(); peer.Subscribe(ch); });
            var tB = Task.Run(() => { start.SignalAndWait(); ch.Delete(); });
            Assert.True(Task.WaitAll([tA, tB], TimeSpan.FromSeconds(30)));
            bool subscribed = peer.ChannelsSnapshot.Contains(ch.Id);
            bool hasCmd = peer.InternalCmdSet?.GetAll().Any(c => c.Key == "racechan" + i) == true;
            Assert.True(subscribed == hasCmd && (!subscribed || !ch.IsDeleted));
        }
    }

    private sealed class SlowSetupCommand : Command
    {
        public override string Key => "raceslow";
        protected override void SetupParser(GameArgumentParser parser)
        {
            Thread.Sleep(100);
            parser.AddArgument("target", help: "t");
        }
        public override void Run(IMessageTarget caller, object? args) { }
    }

    // R1: racing first Parser access must publish one complete instance.
    // Characterization pin (x86/ARM informal — the fix makes it formal).
    [Fact]
    public void Parser_ConcurrentFirstAccess_PublishesCompleteInstance()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new SlowSetupCommand();
        using var barrier = new Barrier(16);
        var seen = new GameArgumentParser?[16];
        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            seen[i] = cmd.Parser;
        })).ToArray();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)));
        foreach (var p in seen) Assert.Same(seen[0], p);
        Assert.Contains("target", seen[0]!.FormatHelp());
    }

    // R2: concurrent ParseArgs on a shared parser stays clean.
    // Characterization pin (production serializes via Command._parserLock;
    // the cache lock makes direct sharing safe too).
    [Fact]
    public void ParseArgs_ConcurrentSharedParser_NoFailures()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new SlowSetupCommand();
        var parser = cmd.Parser!;
        int ok = 0;
        // Start barrier: all 8 must genuinely overlap for the sharing to
        // mean anything. Sequential inlining would pass without contention.
        using var start = new Barrier(8);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < 5000; i++) parser.ParseArgs(["x"]);
            Interlocked.Increment(ref ok);
        })).ToArray();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(60)));
        Assert.Equal(8, ok);
    }

    // R5: Msg on a deleted channel must not append history.
    [Fact]
    public void Msg_DeletedChannel_DoesNotAppendHistory()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = new Channel();
        var sender = GameObject.Create("Bob");
        try { ObjectRegistry.AddObject(sender); } catch { }
        ch.Msg("before", sender);
        Assert.Contains("before", ch.History);
        ch.Delete();
        ch.Msg("after", sender);
        Assert.DoesNotContain("after", ch.History);
        Assert.Contains("before", ch.History);
    }
}
