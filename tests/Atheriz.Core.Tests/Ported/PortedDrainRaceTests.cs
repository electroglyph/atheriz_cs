// Port of atheriz/tests/test_drain_race.py — 5 defs faithful
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDrainRaceTests
{
    private class DrainRecorder
    {
        public object Lock = new();
        public int Active;
        public int MaxActive;
        public List<object> Ran = new();
        public ManualResetEventSlim Started = new(false);
        public Action<BaseConnection, List<object?>, Dictionary<string, object?>> MakeHandler(object tag, int delayMs = 0, ManualResetEventSlim? blocker = null)
        {
            return (conn, args, kwargs) =>
            {
                lock (Lock) { Active++; MaxActive = Math.Max(MaxActive, Active); Ran.Add(tag); Started.Set(); }
                if (delayMs > 0) Thread.Sleep(delayMs);
                blocker?.Wait(5000);
                lock (Lock) Active--;
            };
        }
    }
    private static bool Wait(Func<bool> cond, int timeoutMs = 5000) => PortedHelpers.WaitFor(cond, timeoutMs);
    private sealed class FlakyPool : AsyncThreadPool
    {
        private readonly Func<Func<Task>, string, bool> _impl;
        public FlakyPool(Func<Func<Task>, string, bool> impl, int maxThreads=4, int queueLimit=1000) : base(maxThreads, queueLimit) { _impl = impl; }
        private bool AddInternalWrapper(Func<Task> runner, string name) => _impl(runner, name);
        public override bool AddTask(Action action) => _impl(() => { action(); return Task.CompletedTask; }, action.Method.Name);
        public override bool AddTask(Func<Task> asyncFunc) => _impl(asyncFunc, asyncFunc.Method.Name);
        public override bool AddTask(Delegate del, params object?[] args) => _impl(() => { del.DynamicInvoke(args); return Task.CompletedTask; }, del.Method.Name);
    }

    [Fact]
    public void SingleWorkerInvariantUnderRejection()
    {
        using var env = GlobalTestEnv.Enter();
        var realPool = new AsyncThreadPool(maxThreads:4, queueLimit:1000);
        int failures = 3;
        bool Flaky(Func<Task> runner, string name)
        {
            if (failures > 0) { failures--; return false; }
            return realPool.AddTask(runner, name);
        }
        var flakyPool = new FlakyPool(Flaky, maxThreads:4, queueLimit:1000);
        var mgr = new ConnectionManager(pool: flakyPool);
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var conn = new FakeConnection();
            var rec = new DrainRecorder();
            int total = 20;
            for (int i=0;i<total;i++) conn.EnqueueInput(rec.MakeHandler(i), new List<object?>(), new Dictionary<string, object?>());
            Assert.True(Wait(() => rec.Ran.Count == total, 5000), $"ran {rec.Ran.Count}/{total}");
            lock (rec.Lock) { Assert.True(rec.MaxActive <= 1, $"maxActive {rec.MaxActive}"); Assert.Equal(Enumerable.Range(0,total).Cast<object>().ToList().OrderBy(x=>x).ToList(), rec.Ran.OrderBy(x=>x).ToList()); }
            // Also check FIFO order via sorted == list(range)
            lock (rec.Lock) Assert.Equal(Enumerable.Range(0,total).Cast<object>().ToList(), rec.Ran.OrderBy(x=> (int)x).ToList());
        }
        finally { ConnectionManager.GlobalInstance = prev; flakyPool.Stop(wait:false); realPool.Stop(wait:false); mgr.Atp.Stop(wait:false); }
    }

    [Fact]
    public void NoSilentLossOnRejection()
    {
        using var env = GlobalTestEnv.Enter();
        var realPool = new AsyncThreadPool(maxThreads:4, queueLimit:1000);
        bool rejectNext = true;
        bool Flaky(Func<Task> runner, string name)
        {
            if (rejectNext) { rejectNext=false; return false; }
            return realPool.AddTask(runner, name);
        }
        var flakyPool = new FlakyPool(Flaky);
        var mgr = new ConnectionManager(pool: flakyPool);
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var conn = new FakeConnection();
            var rec = new DrainRecorder();
            conn.EnqueueInput(rec.MakeHandler("A"), new List<object?>(), new Dictionary<string, object?>());
            // queue must be kept intact on rejection
            var q = (System.Collections.ICollection)typeof(BaseConnection).GetField("_inputQueue", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.True(q.Count > 0, "queue must be kept intact on rejection");
            var running = (bool)typeof(BaseConnection).GetField("_inputRunning", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.False(running);
            Assert.Empty(rec.Ran);
            conn.EnqueueInput(rec.MakeHandler("B"), new List<object?>(), new Dictionary<string, object?>());
            Assert.True(Wait(() => rec.Ran.Count==2 && rec.Ran[0].Equals("A") && rec.Ran[1].Equals("B"), 5000));
            var q2 = (System.Collections.ICollection)typeof(BaseConnection).GetField("_inputQueue", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.Empty(q2);
        }
        finally { ConnectionManager.GlobalInstance = prev; flakyPool.Stop(wait:false); realPool.Stop(wait:false); mgr.Atp.Stop(wait:false); }
    }

    [Fact]
    public void NoDoubleStartWhileWorkerMidRun()
    {
        using var env = GlobalTestEnv.Enter();
        var realPool = new AsyncThreadPool(maxThreads:4, queueLimit:1000);
        int calls = 0;
        object callsLock = new();
        bool Counting(Func<Task> runner, string name)
        {
            lock(callsLock) calls++;
            return realPool.AddTask(runner, name);
        }
        var countingPool = new FlakyPool(Counting);
        var mgr = new ConnectionManager(pool: countingPool);
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var conn = new FakeConnection();
            var blocker = new ManualResetEventSlim(false);
            var rec = new DrainRecorder();
            conn.EnqueueInput(rec.MakeHandler("first", blocker: blocker), new List<object?>(), new Dictionary<string, object?>());
            Assert.True(rec.Started.Wait(2000), "first not started");
            for (int i=0;i<5;i++) conn.EnqueueInput(rec.MakeHandler($"later-{i}"), new List<object?>(), new Dictionary<string, object?>());
            lock(callsLock) Assert.Equal(1, calls);
            blocker.Set();
            Assert.True(Wait(() => rec.Ran.Count==6 && !(bool)typeof(BaseConnection).GetField("_inputRunning", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!, 5000));
            lock(callsLock) Assert.Equal(1, calls);
            lock(rec.Lock) { Assert.True(rec.MaxActive <=1); Assert.Equal("first", rec.Ran[0]); Assert.Equal(6, rec.Ran.Count); }
        }
        finally { ConnectionManager.GlobalInstance = prev; countingPool.Stop(wait:false); realPool.Stop(wait:false); mgr.Atp.Stop(wait:false); }
    }

    [Fact]
    public void RejectionBusyReplyThrottled()
    {
        using var env = GlobalTestEnv.Enter();
        var flakyPool = new FlakyPool((runner,name)=> false);
        var mgr = new ConnectionManager(pool: flakyPool);
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var conn = new FakeConnection();
            var rec = new DrainRecorder();
            for(int i=0;i<5;i++) conn.EnqueueInput(rec.MakeHandler(i), new List<object?>(), new Dictionary<string, object?>());
            int busy = conn.Sent.Count(s => s.Cmd=="text" && s.Args.FirstOrDefault()?.ToString()?.ToLowerInvariant().Contains("busy")==true);
            Assert.Equal(1, busy);
            var q = (System.Collections.ICollection)typeof(BaseConnection).GetField("_inputQueue", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.Equal(5, q.Count);
            // After restoring pool, next enqueue should drain all 6
            var realPool = new AsyncThreadPool(maxThreads:4);
            var realMgr = new ConnectionManager(pool: realPool);
            ConnectionManager.GlobalInstance = realMgr;
            conn.EnqueueInput(rec.MakeHandler(99), new List<object?>(), new Dictionary<string, object?>());
            Assert.True(Wait(()=> rec.Ran.Count==6, 5000));
            realPool.Stop(wait:false); realMgr.Atp.Stop(wait:false);
        }
        finally { ConnectionManager.GlobalInstance = prev; flakyPool.Stop(wait:false); mgr.Atp.Stop(wait:false); }
    }

    [Fact]
    public void InputQueueRetriesAfterThreadpoolRejectWithoutNewEnqueue()
    {
        using var env = GlobalTestEnv.Enter();
        var realPool = new AsyncThreadPool(maxThreads:4, queueLimit:1000);
        bool firstRejected = true;
        bool FailOnce(Func<Task> runner, string name)
        {
            if (firstRejected) { firstRejected=false; return false; }
            return realPool.AddTask(runner, name);
        }
        var flakyPool = new FlakyPool(FailOnce);
        var mgr = new ConnectionManager(pool: flakyPool);
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var conn = new FakeConnection();
            var rec = new DrainRecorder();
            conn.EnqueueInput(rec.MakeHandler("only"), new List<object?>(), new Dictionary<string, object?>());
            var q = (System.Collections.ICollection)typeof(BaseConnection).GetField("_inputQueue", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.Equal(1, q.Count);
            var running = (bool)typeof(BaseConnection).GetField("_inputRunning", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.False(running);
            // Now make pool succeed and wait for automatic retry via Timer(0.05) without new enqueue
            ConnectionManager.GlobalInstance = new ConnectionManager(pool: realPool);
            Assert.True(Wait(()=> rec.Ran.Count==1, 3000), "input queue starved after threadpool reject: handler never retried without new input");
            var q2 = (System.Collections.ICollection)typeof(BaseConnection).GetField("_inputQueue", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.Empty(q2);
            var running2 = (bool)typeof(BaseConnection).GetField("_inputRunning", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(conn)!;
            Assert.False(running2);
        }
        finally { ConnectionManager.GlobalInstance = prev; flakyPool.Stop(wait:false); realPool.Stop(wait:false); mgr.Atp.Stop(wait:false); }
    }
}