using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// relief-path task loss, QueueCount truthfulness, shutdown join bound.
// Reflection is test-only white-box access (production never reflects).
[Collection("Ported")]
public class ThreadPoolReliefLossTests
{
    private static bool Wait(Func<bool> cond, int timeoutMs = 5000) => Ported.PortedHelpers.WaitFor(cond, timeoutMs);

    private static FieldInfo Priv(AsyncThreadPool pool, string name) =>
        typeof(AsyncThreadPool).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void EnqueueRaw(AsyncThreadPool pool, object? workItem)
    {
        var q = Priv(pool, "_queue").GetValue(pool)!;
        q.GetType().GetMethod("Enqueue")!.Invoke(q, new object?[] { workItem });
    }

    private static object NewWorkItem(Func<Task> runner, string name)
    {
        var t = typeof(AsyncThreadPool).GetNestedType("WorkItem", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(t, new object[] { runner, name })!;
    }

    private static void RunReliefLoop(AsyncThreadPool pool)
    {
        var m = typeof(AsyncThreadPool).GetMethod("WorkLoop", BindingFlags.NonPublic | BindingFlags.Instance)!;
        m.Invoke(pool, new object?[] { true });
    }

    [Fact]
    public void ReliefRequeueUnderFullQueue_PreservesUserTasks()
    {
        // A relief thread holding an exit sentinel while the queue sits at the
        // limit must grow the limit, never silently drop a queued user task.
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 3, reliefLimit: 0);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(10000); }));
        Assert.True(started.Wait(2000));

        // Front-load an exit sentinel (as Stop leaves it), then over-fill with
        // user items via raw enqueue (AddTask would refuse past the limit).
        EnqueueRaw(pool, null);
        var ran = new ConcurrentBag<int>();
        for (int i = 0; i < 3; i++)
        {
            int v = i;
            EnqueueRaw(pool, NewWorkItem(() => { ran.Add(v); return Task.CompletedTask; }, $"u{v}"));
        }
        Priv(pool, "_stopped").SetValue(pool, true);
        Priv(pool, "_reliefCount").SetValue(pool, 1);

        var t = new Thread(() => RunReliefLoop(pool)) { IsBackground = true };
        t.Start();
        Assert.True(t.Join(3000), "relief worker did not retire");
        Assert.Equal(0, pool.ReliefCount);
        // Old code dequeued (discarded) one user item to make room: 2, not 3.
        Assert.Equal(3, pool.QueueCount);

        gate.Set();
        Assert.True(Wait(() => ran.Count == 3, 3000), $"only {ran.Count}/3 preserved tasks ran");
    }

    [Fact]
    public void QueueCount_ExcludesStopSentinels()
    {
        // Stop's exit sentinels are control messages, not queued user work.
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10, reliefLimit: 0);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        var ran = new ConcurrentBag<int>();
        Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(10000); }));
        Assert.True(started.Wait(2000));
        Assert.True(pool.AddTask(() => ran.Add(1)));

        pool.Stop(wait: false, timeout: TimeSpan.FromSeconds(2));
        // Queue holds [user-task, sentinel]; only the user task counts.
        Assert.Equal(1, pool.QueueCount);

        gate.Set();
        Assert.True(Wait(() => ran.Count == 1, 3000));
    }

    [Fact]
    public void Stop_JoinsFixedWorkersAgainstSharedDeadline()
    {
        // Three stuck fixed workers with a 2s timeout must stall ~2s total,
        // not 3 x 2s of sequential Join calls.
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 100, reliefLimit: 0);
        var gate = new ManualResetEventSlim(false);
        var started = new CountdownEvent(3);
        for (int i = 0; i < 3; i++)
            pool.AddTask(() => { started.Signal(); gate.Wait(30000); });
        Assert.True(started.Wait(3000), "workers did not pick up blockers");

        var sw = Stopwatch.StartNew();
        pool.Stop(wait: true, timeout: TimeSpan.FromSeconds(2));
        sw.Stop();
        gate.Set();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"stop took {sw.Elapsed}");
    }
}
