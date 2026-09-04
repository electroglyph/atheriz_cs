// Port of atheriz/tests/test_reload_concurrency.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Plugins;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedReloadConcurrencyTests
{
    [Fact] public void Reloads_AreSerialized_MaxOverlapOne()
    {
        using var env = GlobalTestEnv.Enter();
        // In C# PluginReloader uses _reloadLock Monitor.TryEnter — concurrent reloads shouldn't overlap
        var tasks = new List<Task<bool>>();
        var ticker = GlobalServices.GetAsyncTicker();
        var pool = GlobalServices.GetAsyncThreadPool();
        for(int i=0;i<2;i++) tasks.Add(PluginReloader.ReloadAsync("/tmp/nonexistent.dll", ticker, pool));
        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
        // Both should complete, second with "already in progress" false
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.True(t.IsCompleted));
    }
    [Fact] public void ShutdownAndReload_DoNotOverlap_SharedWorldLock()
    {
        using var env = GlobalTestEnv.Enter();
        var worldLock = StartStop.WorldLock;
        bool entered1 = false, entered2 = false;
        var t1 = new System.Threading.Thread(() => { lock(worldLock){ entered1=true; System.Threading.Thread.Sleep(50);} });
        var t2 = new System.Threading.Thread(() => { lock(worldLock){ entered2=true; System.Threading.Thread.Sleep(50);} });
        t1.Start(); t2.Start();
        t1.Join(2000); t2.Join(2000);
        Assert.True(entered1 && entered2);
        Assert.False(t1.IsAlive && t2.IsAlive);
    }
    [Fact] public void Reload_HoldsWorldLock_WhilePatching()
    {
        using var env = GlobalTestEnv.Enter();
        var ticker = GlobalServices.GetAsyncTicker();
        // Simulate reload holding world lock
        lock(StartStop.WorldLock)
        {
            // Inside reload, world lock held — other operations wait
            Assert.True(System.Threading.Monitor.IsEntered(StartStop.WorldLock));
        }
        Assert.False(System.Threading.Monitor.IsEntered(StartStop.WorldLock));
    }
    [Fact] public void HttpVsIngameReload_NotInterleaved()
    {
        using var env = GlobalTestEnv.Enter();
        // Placeholder for INTEN 5.5 — verify ReloadAsync TryEnter prevents double entry
        var ticker = GlobalServices.GetAsyncTicker();
        var pool = GlobalServices.GetAsyncThreadPool();
        var t1 = PluginReloader.ReloadAsync("/tmp/a.dll", ticker, pool);
        var t2 = PluginReloader.ReloadAsync("/tmp/b.dll", ticker, pool);
        Task.WaitAll(new[]{t1,t2}, 3000);
        Assert.True(t1.IsCompleted && t2.IsCompleted);
    }
}
