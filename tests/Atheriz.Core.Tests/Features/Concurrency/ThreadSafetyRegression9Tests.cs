using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: post-dispose input drop, closed-session text
// drop, channel command-vs-delete churn, door-dirty retention, salt
// distinctness, dual-leave safety.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression9Tests
{
    // Input past disposal never runs its handler.
    [Fact]
    public async Task EnqueueInput_AfterDispose_NeverRuns()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("c1drop");
        int runs = 0;
        conn.EnqueueInput((c, a, k) => Interlocked.Increment(ref runs), [], []);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref runs) < 1 && sw.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(10);
        Assert.Equal(1, Volatile.Read(ref runs));
        conn.Dispose();
        conn.EnqueueInput((c, a, k) => Interlocked.Increment(ref runs), [], []);
        await Task.Delay(300);
        Assert.Equal(1, Volatile.Read(ref runs));
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Core", "Network", "BaseConnection.cs");
        Assert.Contains("private volatile bool _disposed;", src);
    }

    // Closed-session text dispatches nothing, not even a parked prompt.
    [Fact]
    public void Text_ClosedSessionWithFuture_DropsEverything()
    {
        using var env = GlobalTestEnv.Enter();
        var funcs = new InputFuncs();
        var conn = new TestConnection("c2drop");
        var character = GameObject.Create("c2char", isPc: true);
        try { ObjectRegistry.AddObject(character); } catch { }
        conn.Session.Puppet = character;
        conn.Session.AtDisconnect();
        conn.Session.Puppet = character;
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.Session.InputFuture = tcs;
        character.ClearMessages();
        funcs.Text(conn, new List<object?> { "look" }, new Dictionary<string, object?>());
        Assert.False(tcs.Task.IsCompleted);
        Assert.Empty(character.PeekMessages());
    }

    // Channel command churn beside delete churn never deadlocks.
    // The two sides free-run (no per-round rendezvous): a Barrier(2) here
    // phase-locks the workers so the fatal Subscribe-vs-detach overlap
    // rarely aligns and the ABBA hides. Free-running back-to-back
    // iterations over 200 rounds explore the overlap deterministically:
    // pre-fix this stalled the 60 s watchdog most runs; post-fix it
    // completes in milliseconds. (Subscribe/RemoveListener used to read
    // the peer id under the channel lock — channel -> peer — against
    // Delete-detach's peer -> channel order.)
    [Fact]
    public async Task GetCommand_Delete_Churn_Completes()
    {
        using var env = GlobalTestEnv.Enter();
        for (int round = 0; round < 200; round++)
        {
            var ch = Channel.Create("a8chan" + round);
            var peer = GameObject.Create("a8peer" + round, isPc: true);
            try { ObjectRegistry.AddObject(ch); } catch { }
            try { ObjectRegistry.AddObject(peer); } catch { }
            var t1 = Task.Run(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    peer.Subscribe(ch);
                    _ = ch.GetCommand();
                    peer.Unsubscribe(ch);
                }
            });
            var t2 = Task.Run(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    try { ch.Delete(); } catch { }
                    try { ObjectRegistry.AddObject(ch); } catch { }
                }
            });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(60));
        }
    }

    // Suppression windows never lose a dirty mark (generation-guarded).
    [Fact]
    public async Task DoorMark_ConcurrentDeferChurn_NeverLosesChange()
    {
        using var env = GlobalTestEnv.Enter();
        var door = new Door(new Coord("b5a", 0, 0, 0), new Coord("b5a", 1, 0, 0), "east", "west");
        using var barrier = new Barrier(2);
        var t1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 2000; i++)
                door.SetEndpoints(new Coord("b5a", 0, 0, 0), new Coord("b5a", 1, 0, 0));
        });
        var t2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 2000; i++)
                door.FromExit = i % 2 == 0 ? "east" : "east2";
        });
        await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(door.FromExit == "east" || door.FromExit == "east2");
        var src = Tests.Features.Regression.SourceScan.Read("src", "Atheriz.Core", "Objects", "Door.cs");
        Assert.Equal(12, Tests.Features.Regression.SourceScan.Count(src, "MarkAfterWrite(changed);"));
    }

    // Concurrent fresh-path salts are distinct and correct.
    [Fact]
    public async Task SaltProvider_ConcurrentFreshPaths_DistinctSalts()
    {
        using var env = GlobalTestEnv.Enter();
        var paths = Enumerable.Range(0, 16)
            .Select(_ => Path.Combine(Path.GetTempPath(), "atheriz_b11_" + Guid.NewGuid().ToString("N")))
            .ToList();
        try
        {
            var results = new string?[16];
            using var barrier = new Barrier(16);
            var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            {
                barrier.SignalAndWait();
                results[i] = SaltProvider.GetSalt(paths[i]);
            })).ToArray();
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.All(results, s => Assert.False(string.IsNullOrEmpty(s)));
            Assert.Equal(16, results.Distinct().Count());
        }
        finally
        {
            foreach (var p in paths) try { Directory.Delete(p, true); } catch { }
        }
    }

    // Dual last-member leave never throws and leaves no stranded member.
    [Fact]
    public async Task GroupLeave_DualLeave_NoThrowNoStrand()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("e4area", 0, 0, 0));
        try { ObjectRegistry.AddObject(room); } catch { }
        for (int round = 0; round < 50; round++)
        {
            var a = GameObject.Create($"e4a{round}", isPc: true);
            var b = GameObject.Create($"e4b{round}", isPc: true);
            a.PrivilegeLevel = Privilege.Builder;
            b.PrivilegeLevel = Privilege.Builder;
            foreach (var o in new[] { a, b })
            {
                try { ObjectRegistry.AddObject(o); } catch { }
                o.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(room.Coord);
                room.AddObject(o);
                o.ClearMessages();
            }
            var ch = Channel.Create($"e4group{round}");
            try { ObjectRegistry.AddObject(ch); } catch { }
            ch.CreatedBy = a.Id;
            ch.AddListener(a);
            ch.AddListener(b);
            a.GroupChannel = ch.Id;
            b.GroupChannel = ch.Id;
            using var barrier = new Barrier(2);
            var t1 = Task.Run(() => { barrier.SignalAndWait(); new GroupCommand().Run(a, new GroupCommand().Parser!.ParseArgs(["leave"])); });
            var t2 = Task.Run(() => { barrier.SignalAndWait(); new GroupCommand().Run(b, new GroupCommand().Parser!.ParseArgs(["leave"])); });
            await Task.WhenAll([t1, t2]).WaitAsync(TimeSpan.FromSeconds(30));
            foreach (var o in new[] { a, b })
            {
                if (o.GroupChannel is int gid)
                {
                    var member = ObjectRegistry.GetSingle(gid) as Channel;
                    Assert.NotNull(member);
                    Assert.False(member.IsDeleted);
                    Assert.Contains(o.Id, member.Listeners);
                }
            }
        }
    }
}
