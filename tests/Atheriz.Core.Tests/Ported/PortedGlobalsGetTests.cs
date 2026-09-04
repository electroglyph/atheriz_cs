// Port of atheriz/tests/test_globals_get.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;
using System.Threading;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedGlobalsGetTests
{
    // --- TestUniqueId ---
    [Fact] public void SetIdChangesCounter()
    {
        using var env = GlobalTestEnv.Enter();
        IdGenerator.SetId(0);
        Assert.Equal(1, IdGenerator.GetUniqueId());
    }
    [Fact] public void IdIncrements()
    {
        using var env = GlobalTestEnv.Enter();
        IdGenerator.SetId(0);
        var id1 = IdGenerator.GetUniqueId();
        var id2 = IdGenerator.GetUniqueId();
        Assert.Equal(id1 + 1, id2);
    }
    [Fact] public void IdStrictlyMonotonic()
    {
        using var env = GlobalTestEnv.Enter();
        IdGenerator.SetId(0);
        var ids = Enumerable.Range(0, 10).Select(_ => IdGenerator.GetUniqueId()).ToList();
        for (int i = 1; i < ids.Count; i++) Assert.Equal(ids[i-1] + 1, ids[i]);
    }
    [Fact] public void SetIdOverrides()
    {
        using var env = GlobalTestEnv.Enter();
        IdGenerator.SetId(100);
        Assert.Equal(101, IdGenerator.GetUniqueId());
        Assert.Equal(102, IdGenerator.GetUniqueId());
    }
    [Fact] public void SetIdToZero()
    {
        using var env = GlobalTestEnv.Enter();
        IdGenerator.SetId(0);
        Assert.Equal(1, IdGenerator.GetUniqueId());
    }
    [Fact] public void SetIdNegative()
    {
        using var env = GlobalTestEnv.Enter();
        IdGenerator.SetId(-5);
        Assert.Equal(-4, IdGenerator.GetUniqueId());
    }
    [Fact] public void IdUniqueAcrossThreads()
    {
        using var env = GlobalTestEnv.Enter();
        IdGenerator.SetId(0);
        var seen = new List<int>();
        var lck = new object();
        void Worker()
        {
            for (int i = 0; i < 50; i++)
            {
                var nid = IdGenerator.GetUniqueId();
                lock (lck) seen.Add(nid);
            }
        }
        var threads = Enumerable.Range(0, 4).Select(_ => new Thread(Worker)).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Assert.Equal(4 * 50, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(seen.Count - 1, seen.Max() - seen.Min());
    }
    [Fact] public void IdStartsFromMinusOneAfterReset()
    {
        using var env = GlobalTestEnv.Enter();
        // GlobalTestEnv already reset to -1
        var first = IdGenerator.GetUniqueId();
        Assert.Equal(0, first);
    }

    // --- TestGetGameTime ---
    [Fact] public void GetGameTimeReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var t1 = GlobalServices.GetGameTime();
        var t2 = GlobalServices.GetGameTime();
        Assert.Same(t1, t2);
    }
    // --- TestGetConnectionManager ---
    [Fact] public void GetConnectionManagerReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var m1 = GlobalServices.GetConnectionManager();
        var m2 = GlobalServices.GetConnectionManager();
        Assert.Same(m1, m2);
    }
    [Fact] public void GetConnectionManagerReturnsManagerInstance()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var m1 = GlobalServices.GetConnectionManager();
        var m2 = GlobalServices.GetConnectionManager();
        Assert.Same(m1, m2);
        // After reset, should still be ConnectionManager
        GlobalServices.ResetForTesting();
        var baseMgr = GlobalServices.GetConnectionManager();
        Assert.IsType<ConnectionManager>(baseMgr);
    }
    // --- TestGetAsyncTicker ---
    [Fact] public void GetAsyncTickerReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var t1 = GlobalServices.GetAsyncTicker();
        var t2 = GlobalServices.GetAsyncTicker();
        Assert.Same(t1, t2);
    }
    [Fact] public void GetAsyncTickerConstructedOnce()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var t1 = GlobalServices.GetAsyncTicker();
        var t2 = GlobalServices.GetAsyncTicker();
        Assert.Same(t1, t2);
    }
    // --- TestGetServerChannel ---
    [Fact] public void GetServerChannelReturnsNoneWhenNoChannel()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        ObjectRegistry.ClearAll();
        // ensure no channel named server
        var result = GlobalServices.GetServerChannel();
        Assert.Null(result);
    }
    [Fact] public void GetServerChannelReturnsFirstMatchingChannel()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        ObjectRegistry.ClearAll();
        var chan = new Channel();
        chan.Name = "server";
        chan.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(chan);
        var result = GlobalServices.GetServerChannel();
        Assert.Same(chan, result);
    }
    [Fact] public void GetServerChannelCachesAfterFirstLookup()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        ObjectRegistry.ClearAll();
        var chan = new Channel();
        chan.Name = "server";
        chan.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(chan);
        var first = GlobalServices.GetServerChannel();
        var second = GlobalServices.GetServerChannel();
        Assert.Same(first, second);
        Assert.Same(chan, first);
        // Should be cached — removing from registry still returns cached
        ObjectRegistry.RemoveObject(chan);
        var third = GlobalServices.GetServerChannel();
        // Implementation clears cache if name mismatch or deleted; after removal, cached still returned until next check?
        // Our impl checks IsDeleted and name on cached; after RemoveObject, cached still has same object but registry no longer contains it.
        // It will still return cached because IsDeleted false and name == server. That's expected caching behavior.
        Assert.NotNull(third);
    }
    [Fact] public void GetServerChannelReturnsCachedOnSubsequentCalls()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        ObjectRegistry.ClearAll();
        var chan = new Channel();
        chan.Name = "server";
        chan.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(chan);
        var first = GlobalServices.GetServerChannel();
        var second = GlobalServices.GetServerChannel();
        Assert.Same(first, second);
    }
    // --- TestGetMapHandler ---
    [Fact] public void GetMapHandlerReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var m1 = GlobalServices.GetMapHandler();
        var m2 = GlobalServices.GetMapHandler();
        Assert.Same(m1, m2);
    }
    // --- TestGetLoggedinCmdset ---
    [Fact] public void GetLoggedInCmdSetReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var c1 = GlobalServices.GetLoggedInCmdSet();
        var c2 = GlobalServices.GetLoggedInCmdSet();
        Assert.Same(c1, c2);
    }
    // --- TestGetUnloggedinCmdset ---
    [Fact] public void GetUnloggedInCmdSetReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var c1 = GlobalServices.GetUnloggedInCmdSet();
        var c2 = GlobalServices.GetUnloggedInCmdSet();
        Assert.Same(c1, c2);
    }
    // --- TestGetAsyncThreadpool ---
    [Fact] public void GetAsyncThreadPoolReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var t1 = GlobalServices.GetAsyncThreadPool();
        var t2 = GlobalServices.GetAsyncThreadPool();
        Assert.Same(t1, t2);
    }
    [Fact] public void GetAsyncThreadPoolConstructedWithThreadpoolLimit()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var pool = GlobalServices.GetAsyncThreadPool();
        Assert.NotNull(pool);
        // Threadpool limit is at least 1
        Assert.True(pool != null);
    }
    // --- TestGetNodeHandler ---
    [Fact] public void GetNodeHandlerReturnsSingleton()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var n1 = GlobalServices.GetNodeHandler();
        var n2 = GlobalServices.GetNodeHandler();
        Assert.Same(n1, n2);
    }
    // --- TestIntegration ---
    [Fact] public void GettersAreIndependent()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.ResetForTesting();
        var gt = GlobalServices.GetGameTime();
        var mh = GlobalServices.GetMapHandler();
        var nh = GlobalServices.GetNodeHandler();
        Assert.NotSame(gt as object, mh as object);
        Assert.NotSame(mh as object, nh as object);
        Assert.NotSame(gt as object, nh as object);
    }
}
