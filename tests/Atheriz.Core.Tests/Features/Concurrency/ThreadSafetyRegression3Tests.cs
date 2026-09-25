using System.Diagnostics;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: containment-cycle refusal, grid prior-owner
// detach, bulk-move single-churn, wrong-password creation parallelism,
// deleted-target look.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression3Tests
{
    private sealed class CountingItem : GameObject
    {
        public int Drops;
        public CountingItem() { IsItem = true; }
        public override bool AtPreDrop(GameObject giver) => true;
        public override void AtDrop(GameObject giver) => Interlocked.Increment(ref Drops);
    }

    // Opposite containment moves never both succeed, never cycle.
    [Fact]
    public async Task MoveTo_OppositeContainmentMoves_NoCycleCommitted()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("a3area", 0, 0, 0));
        for (int round = 0; round < 100; round++)
        {
            var c = GameObject.Create("a3c", isContainer: true);
            var d = GameObject.Create("a3d", isContainer: true);
            Assert.True(c.MoveTo(room, force: true, announce: false));
            Assert.True(d.MoveTo(room, force: true, announce: false));
            bool r1 = false, r2 = false;
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() => { barrier.SignalAndWait(); r1 = d.MoveTo(c, force: true, announce: false); });
            var t2 = Task.Run(() => { barrier.SignalAndWait(); r2 = c.MoveTo(d, force: true, announce: false); });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(r1 && r2);
            // The containment walk must terminate (no cycle committed).
            var seen = new HashSet<int>();
            var cur = (GameObject?)c;
            for (int i = 0; i < 1000 && cur is not null; i++)
            {
                Assert.True(seen.Add(cur.Id), "containment cycle committed");
                var next = cur.ResolveLocationObject();
                if (next is null || next.IsNode) break;
                cur = next;
            }
        }
    }

    // A grid added to a second area leaves the first (no dual-homing).
    [Fact]
    public async Task AddGrid_SecondArea_DetachesFromFirst()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var a = new NodeArea("b9a");
            var b = new NodeArea("b9b");
            nh.AddArea(a);
            nh.AddArea(b);
            var grid = new NodeGrid("b9a", 0);
            a.AddGrid(grid);
            b.AddGrid(grid);
            Assert.Null(a.GetGrid(0));
            Assert.Same(grid, b.GetGrid(0));
            Assert.Equal("b9b", grid.Area);
            // Concurrent churn converges to exactly one owner.
            using var barrier = new Barrier(2);
            for (int i = 0; i < 50; i++)
            {
                var t1 = Task.Run(() => { barrier.SignalAndWait(); a.AddGrid(grid); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); b.AddGrid(grid); });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                bool inA = ReferenceEquals(a.GetGrid(0), grid);
                bool inB = ReferenceEquals(b.GetGrid(0), grid);
                Assert.True(inA ^ inB);
                Assert.Equal(inA ? "b9a" : "b9b", grid.Area);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Concurrent same-dest moves churn exactly once.
    [Fact]
    public async Task MoveTo_ConcurrentSameDestMove_ChurnsOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            for (int round = 0; round < 100; round++)
            {
                var pile = new Node(new Coord("e21area", 0, round, 0));
                var dest = new Node(new Coord("e21area", 1, round, 0));
                nh.AddNode(pile);
                nh.AddNode(dest);
                var item = GameObject.Create("e21item", isItem: true);
                Assert.True(item.MoveTo(pile, force: true, announce: false));
                bool c1 = false, c2 = false;
                using var barrier = new Barrier(2);
                var t1 = Task.Run(() => { barrier.SignalAndWait(); item.MoveTo(dest, null, true, false, null, out c1); });
                var t2 = Task.Run(() => { barrier.SignalAndWait(); item.MoveTo(dest, null, true, false, null, out c2); });
                await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
                // Exactly one churn: the loser either aborts on the R6
                // re-verify or observes the no-op — never a second churn.
                Assert.True(c1 ^ c2);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // A repeated bulk drop fires hooks exactly once.
    [Fact]
    public void DropAll_RepeatedRun_HooksFireOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var room = new Node(new Coord("e21barea", 0, 0, 0));
            nh.AddNode(room);
            var go = GameObject.Create("e21packer", isPc: true);
            try { ObjectRegistry.AddObject(go); } catch { }
            Assert.True(go.MoveTo(room, force: true, announce: false));
            var item = new CountingItem { Name = "e21widget" };
            try { ObjectRegistry.AddObject(item); } catch { }
            Assert.True(item.MoveTo(go, force: true, announce: false));
            go.ClearMessages();
            var drop = new DropCommand();
            drop.Run(go, drop.Parser!.ParseArgs(["all"]));
            Assert.Equal(1, item.Drops);
            go.ClearMessages();
            drop.Run(go, drop.Parser!.ParseArgs(["all"]));
            Assert.Equal(1, item.Drops);
            Assert.DoesNotContain(go.PeekMessages(), m => m.Contains("dropped"));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // Concurrent wrong-password signups parallelize past PBKDF2.
    [Fact]
    public async Task AtCharCreate_ConcurrentWrongPassword_Parallelizes()
    {
        using var env = GlobalTestEnv.Enter();
        var home = new Node(AtherizSettings.Global.DefaultHome);
        try { ObjectRegistry.AddObject(home); } catch { }
        var setupOut = new StringWriter();
        ServerEvents.AtCharCreate("f6acc", "F6Owner", "password123", setupOut);
        var sw = new StringWriter();
        var sw2 = new Stopwatch();
        sw2.Start();
        ServerEvents.AtCharCreate("f6acc", "F6Solo", "wrongpass123", sw);
        sw2.Stop();
        double unit = Math.Max(sw2.Elapsed.TotalMilliseconds, 1);
        using var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            var name = $"F6User{(char)('A' + i)}";
            barrier.SignalAndWait();
            ServerEvents.AtCharCreate("f6acc", name, "wrongpass123", TextWriter.Null);
        })).ToArray();
        var wall = Stopwatch.StartNew();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(120));
        wall.Stop();
        // Pre-fix the 8 creates serialize at ~N x PBKDF2 (measured 8.1x);
        // post-fix the hashes run concurrently. 5x keeps margin under load.
        Assert.True(wall.Elapsed.TotalMilliseconds < 5 * unit,
            $"wall {wall.Elapsed.TotalMilliseconds:F0}ms vs unit {unit:F0}ms");
    }

    // A deleted object renders nothing.
    [Fact]
    public void AtLook_DeletedTarget_RendersNothing()
    {
        using var env = GlobalTestEnv.Enter();
        var looker = GameObject.Create("a14looker", isPc: true);
        var target = GameObject.Create("a14target");
        target.Desc = "shiny";
        Assert.Contains("shiny", looker.AtLook(target));
        target.IsDeleted = true;
        Assert.Equal("You see nothing here.", looker.AtLook(target));
        Assert.Equal("You see nothing here.", target.ReturnAppearance(looker));
    }
}
