using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: deleted-mover refusal, unsubscribe/subscribe
// coherence, concurrent time adds, deleted-receiver drop, character-cap
// reserve, excluded-assembly mutation.
// All exercise real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression2Tests
{
    // A deleted mover must not re-home into room contents.
    [Fact]
    public void MoveTo_DeletedMover_RefusedNoGhost()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("a5area", 0, 0, 0));
        var mover = GameObject.Create("a5mover");
        Assert.True(mover.MoveTo(room, force: true, announce: false));
        mover.IsDeleted = true;
        var room2 = new Node(new Coord("a5area", 1, 0, 0));
        Assert.False(mover.MoveTo(room2, force: true, announce: false));
        Assert.DoesNotContain(mover.Id, room2.ContentsSnapshot);
    }

    // Concurrent Subscribe/Unsubscribe never splits membership vs command.
    [Fact]
    public async Task Unsubscribe_Subscribe_Race_NeverSplitsCommand()
    {
        using var env = GlobalTestEnv.Enter();
        for (int i = 0; i < 1500; i++)
        {
            var ch = Channel.Create("a7chan" + i);
            var peer = GameObject.Create("a7peer" + i, isPc: true);
            try { ObjectRegistry.AddObject(ch); } catch { }
            try { ObjectRegistry.AddObject(peer); } catch { }
            peer.Subscribe(ch);
            using var start = new Barrier(2);
            var tA = Task.Run(() => { start.SignalAndWait(); peer.Unsubscribe(ch); });
            var tB = Task.Run(() => { start.SignalAndWait(); peer.Subscribe(ch); });
            await Task.WhenAll([tA, tB]).WaitAsync(TimeSpan.FromSeconds(30));
            bool subscribed = peer.ChannelsSnapshot.Contains(ch.Id);
            bool hasCmd = peer.InternalCmdSet?.GetAll().Any(c => c.Key == "a7chan" + i) == true;
            Assert.True(subscribed == hasCmd);
        }
    }

    // Concurrent playtime adds sum exactly.
    [Fact]
    public async Task AddSecondsPlayed_ConcurrentAdds_SumExactly()
    {
        using var env = GlobalTestEnv.Enter();
        var go = new GameObject { Name = "A15Acc" };
        go.RawSecondsPlayed = 0;
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 5000; i++) go.AddSecondsPlayed(1.0);
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(8 * 5000.0, go.RawSecondsPlayed);
    }

    // Delivery into a deleted object is dropped.
    [Fact]
    public void Msg_DeletedReceiver_Dropped()
    {
        using var env = GlobalTestEnv.Enter();
        var go = new GameObject { Name = "E3Recv" };
        go.Msg("hello");
        Assert.NotEmpty(go.PeekMessages());
        go.ClearMessages();
        go.IsDeleted = true;
        go.Msg("after death");
        ContentUtils.EmitToContents([go], go, "broadcast", null, null, null, null, false, nodeSemantics: false);
        Assert.Empty(go.PeekMessages());
    }

    // The character cap holds under concurrent reserves.
    [Fact]
    public async Task TryAddCharacter_ConcurrentReserves_CapHolds()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = new Account { Name = "D4Acc" };
        using var barrier = new Barrier(8);
        var results = new bool[8];
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            var c = GameObject.Create("d4char" + i, isPc: true);
            barrier.SignalAndWait();
            results[i] = acc.TryAddCharacter(c, 2);
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, results.Count(r => r));
        Assert.Equal(2, acc.Characters.Count);
    }

    // Exclusion scans survive concurrent set mutation.
    [Fact]
    public async Task IsExcludedAssembly_ConcurrentMutation_NeverThrows()
    {
        using var env = GlobalTestEnv.Enter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var hammer = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                PluginReloader.AddExcludedAssembly("ScratchEntry12345");
                PluginReloader.RemoveExcludedAssembly("ScratchEntry12345");
            }
        });
        var scanners = Enumerable.Range(0, 3).Select(idx => Task.Run(() =>
        {
            for (int i = 0; i < 2000; i++)
            {
                _ = PluginReloader.IsExcludedAssembly("System.Text.dll");
                _ = PluginReloader.IsExcludedAssembly("MyGame.dll");
            }
        })).ToArray();
        await Task.WhenAll(scanners).WaitAsync(TimeSpan.FromSeconds(30));
        cts.Cancel();
        await hammer.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
