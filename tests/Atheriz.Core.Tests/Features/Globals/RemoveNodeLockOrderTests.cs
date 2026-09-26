using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Globals;

// Finding 24: RemoveNode called RemoveObject (registry AllLock) while
// holding the handler write lock, inverting the collect-then-evict order
// ReplaceArea and Clear keep. It now resolves and ungrids under the hold
// and evicts after release. The discriminator is the handler lock itself:
// while the remover parks on AllLock, a handler read lock must still be
// acquirable (pre-fix the write hold is stuck underneath it).
// Setup holds AllLock directly: uniqueness predicates no longer run under
// it (finding 23 fix — snapshot-then-check outside the hold), so the old
// AddObjectUnique-predicate holder would never park the remover.
[Collection("Ported")]
public sealed class RemoveNodeLockOrderTests
{
    [Fact]
    public void RemoveNode_DoesNotHoldHandlerLockWhileWaitingOnRegistry()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var victim = new Node(new Coord("limbo", 3, 3, 0), "victim");
        nh.AddNode(victim);
        Assert.NotNull(ObjectRegistry.GetSingle(victim.Id));

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var registry = new Thread(() =>
        {
            lock (ObjectRegistry.AllLock)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        })
        { IsBackground = true };
        registry.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "registry side holds AllLock across its predicate");
            var removerDone = false;
            var remover = new Thread(() =>
            {
                nh.RemoveNode(victim.Coord);
                removerDone = true;
            })
            { IsBackground = true };
            remover.Start();
            // Settle: the remover parks on AllLock in both worlds (pre-fix
            // holding the handler write lock, post-fix after releasing it).
            // A slow thread start here would flake either verdict, and the
            // sensitivity run (old RemoveNode restored) proves the poll
            // below still fails without the fix.
            Thread.Sleep(500);
            Assert.False(Volatile.Read(ref removerDone), "remover finished while AllLock is still held — test setup broken");
            // While the remover is parked on AllLock, the handler lock must
            // be free. Poll (rather than sleep-then-probe once) so a slow
            // thread start cannot flake the verdict either way.
            var readWhileParked = false;
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline && !Volatile.Read(ref removerDone))
            {
                if (nh.Lock.TryEnterReadLock(50))
                {
                    try { readWhileParked = true; }
                    finally { nh.Lock.ExitReadLock(); }
                    break;
                }
            }
            Assert.True(readWhileParked, "handler read lock acquirable while RemoveNode waits on the registry lock");
        }
        finally
        {
            release.Set();
            registry.Join(TimeSpan.FromSeconds(5));
        }
        Assert.True(Ported.PortedHelpers.WaitFor(() => ObjectRegistry.GetSingle(victim.Id) is null, 5000),
            "node evicted from the registry after release");
    }
}
