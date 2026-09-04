// Port of atheriz/tests/test_input_dispatch.py:1 — 8 defs faithful
using System.Diagnostics;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedInputDispatchTests
{
    private static ConnectionManager MakeManager() => PortedHelpers.MakeManager();

    private static bool Wait(Func<bool> cond, double timeoutSec = 2.0) => PortedHelpers.WaitFor(cond, (int)(timeoutSec*1000));

    [Fact]
    public void HandlerRunsOffCallerThreadOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        int callerTid = Environment.CurrentManagedThreadId;
        var seen = new List<int>();
        var lk = new object();
        void Handler(BaseConnection c, List<object?> a, Dictionary<string, object?> k)
        { lock (lk) seen.Add(Environment.CurrentManagedThreadId); }
        mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)Handler);
        var conn = new FakeConnection("t1");
        mgr.HandleCommand(conn, System.Text.Json.JsonSerializer.Serialize(new object[] { "text", new object[] { "look" }, new Dictionary<string, object?>() }));
        Assert.True(Wait(() => { lock(lk) return seen.Count>0; }));
        lock(lk) { Assert.Single(seen); Assert.NotEqual(callerTid, seen[0]); }
        mgr.Atp.Stop(wait:false);
    }

    [Fact]
    public void DispatchReturnsPromptlyWhileHandlerBlocked()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var release = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        void Handler(BaseConnection c, List<object?> a, Dictionary<string, object?> k) { started.Set(); release.Wait(2000); }
        mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)Handler);
        var c = new FakeConnection();
        var sw = Stopwatch.StartNew();
        mgr.Dispatch(c, "text", new List<object?>(), new Dictionary<string, object?>());
        sw.Stop();
        Assert.True(started.Wait(2000));
        Assert.True(sw.Elapsed.TotalSeconds < 1.0);
        release.Set();
        Thread.Sleep(80);
        mgr.Atp.Stop(wait:false);
    }

    [Fact]
    public void PerConnectionFifoOrdering()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var got = new List<int>();
        var lk = new object();
        void H(BaseConnection c, List<object?> a, Dictionary<string, object?> k) { lock(lk) got.Add(Convert.ToInt32(a[0])); }
        mgr.RegisterHandler("text", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)H);
        var conn = new FakeConnection("fifo");
        int n = 50; // faithful to original 50 (not 20)
        for (int i=0;i<n;i++) mgr.Dispatch(conn, "text", new List<object?>{i}, new Dictionary<string, object?>());
        Assert.True(Wait(() => { lock(lk) return got.Count==n; }, 5));
        lock(lk) Assert.Equal(Enumerable.Range(0,n).ToList(), got);
        mgr.Atp.Stop(wait:false);
    }

    [Fact]
    public void ConnectionsNotSerializedGlobally()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var release = new ManualResetEventSlim(false);
        var doneB = new ManualResetEventSlim(false);
        void Slow(BaseConnection c, List<object?> a, Dictionary<string, object?> k) => release.Wait(2000);
        void Fast(BaseConnection c, List<object?> a, Dictionary<string, object?> k) => doneB.Set();
        mgr.RegisterHandler("slow", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)Slow);
        mgr.RegisterHandler("fast", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)Fast);
        var a = new FakeConnection("a"); var b = new FakeConnection("b");
        mgr.Dispatch(a, "slow", new List<object?>(), new Dictionary<string, object?>());
        mgr.Dispatch(b, "fast", new List<object?>(), new Dictionary<string, object?>());
        Assert.True(doneB.Wait(2000));
        release.Set();
        mgr.Atp.Stop(wait:false);
    }

    [Fact]
    public void DisconnectDropsQueuedInput()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new AtherizSettings();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var mgr = new ConnectionManager(pool, s);
        var release = new ManualResetEventSlim(false);
        var ran = new List<string>();
        var ranLock = new object();
        void First(BaseConnection c, List<object?> a, Dictionary<string, object?> k) => release.Wait(2000);
        void Second(BaseConnection c, List<object?> a, Dictionary<string, object?> k) { lock(ranLock) ran.Add("second"); }
        mgr.RegisterHandler("first", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)First);
        mgr.RegisterHandler("second", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)Second);
        var c = new FakeConnection("c1");
        mgr.RegisterConnection("c1", c);
        mgr.Dispatch(c, "first", new List<object?>(), new Dictionary<string, object?>());
        Thread.Sleep(80);
        mgr.Dispatch(c, "second", new List<object?>(), new Dictionary<string, object?>());
        mgr.Disconnect(c);
        release.Set();
        Assert.True(Wait(() => !IsInputRunning(c)));
        Thread.Sleep(50);
        lock(ranLock) Assert.Empty(ran);
        // Verify queue cleared under connection.Lock (faithful to Python's with connection.lock: clear)
        var q = GetInputQueue(c);
        lock (c.Lock) Assert.Empty(q);
        mgr.Atp.Stop(wait:false);
    }

    [Fact]
    public void FloodCappedNewestDroppedBusyThrottled()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var release = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        var ran = new List<int>();
        var ranLock = new object();
        void First(BaseConnection c, List<object?> a, Dictionary<string, object?> k) { started.Set(); release.Wait(5000); }
        void Seq(BaseConnection c, List<object?> a, Dictionary<string, object?> k) { lock(ranLock) ran.Add(Convert.ToInt32(a[0])); }
        mgr.RegisterHandler("first", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)First);
        mgr.RegisterHandler("seq", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)Seq);
        var c = new FakeConnection("cap");
        mgr.Dispatch(c, "first", new List<object?>(), new Dictionary<string, object?>());
        Assert.True(started.Wait(2000));
        var cap = new AtherizSettings().ConnectionInputQueueLimit;
        for (int i=0;i<cap+50;i++) mgr.Dispatch(c, "seq", new List<object?>{i}, new Dictionary<string, object?>());
        var q = GetInputQueue(c);
        Assert.Equal(cap, q.Count);
        var busy = c.Sent.Count(s => s.Cmd == "text" && (s.Args.FirstOrDefault()?.ToString()?.ToLowerInvariant().Contains("busy") ?? false));
        Assert.Equal(1, busy);
        release.Set();
        Assert.True(Wait(() => { lock(ranLock) return ran.Count==cap; }, 5));
        lock(ranLock) Assert.Equal(Enumerable.Range(0,cap).ToList(), ran);
        mgr.Atp.Stop(wait:false);
    }

    [Fact]
    public void RecoversAfterDrain()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var release = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        var ran = new List<object>();
        var ranLock = new object();
        void First(BaseConnection c, List<object?> a, Dictionary<string, object?> k) { started.Set(); release.Wait(5000); }
        mgr.RegisterHandler("first", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)First);
        mgr.RegisterHandler("seq", (Action<BaseConnection, List<object?>, Dictionary<string, object?>>)((c,a,k)=>{ lock(ranLock) ran.Add(a[0]!); }));
        var c = new FakeConnection("cap2");
        mgr.Dispatch(c, "first", new List<object?>(), new Dictionary<string, object?>());
        Assert.True(started.Wait(2000));
        var cap = new AtherizSettings().ConnectionInputQueueLimit;
        for (int i=0;i<cap+10;i++) mgr.Dispatch(c, "seq", new List<object?>{i}, new Dictionary<string, object?>());
        release.Set();
        Assert.True(Wait(() => !IsInputRunning(c) && GetRanCount(ranLock, ran)==cap, 5));
        mgr.Dispatch(c, "seq", new List<object?>{"after"}, new Dictionary<string, object?>());
        Assert.True(Wait(() => { lock(ranLock) return ran.Count>0 && ran[ran.Count-1]?.ToString()=="after"; }));
        mgr.Atp.Stop(wait:false);
    }
    private static int GetRanCount(object lk, List<object> ran) { lock(lk) return ran.Count; }

    [Fact]
    public void RunMatchesPoolSemantics()
    {
        using var env = GlobalTestEnv.Enter();
        var atp = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        var got = new List<int>();
        atp.Run((Delegate)new Action<int>(got.Add), 1);
        Assert.Equal(new List<int>{1}, got);
        var ex = Record.Exception(() => atp.Run((Delegate)new Action(() => { throw new DivideByZeroException(); })));
        Assert.Null(ex);
        atp.Stop(wait:false);
    }

    private static bool IsInputRunning(BaseConnection c)
    {
        var f = typeof(BaseConnection).GetField("_inputRunning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f != null && (bool)(f.GetValue(c) ?? false);
    }
    private static System.Collections.ICollection GetInputQueue(BaseConnection c)
    {
        var f = typeof(BaseConnection).GetField("_inputQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (System.Collections.ICollection)f.GetValue(c)!;
    }
}