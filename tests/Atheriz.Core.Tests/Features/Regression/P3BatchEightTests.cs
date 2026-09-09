using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for the P3 batch 8 (N-14..N-18).
[Collection("Ported")]
public class P3BatchEightTests
{
    private static bool IsColor(object? v)
    {
        var m = typeof(InputFuncs).GetMethod("IsColor", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        return (bool)m.Invoke(null, new object?[] { v })!;
    }

    // The list branch tests positional ints once: no repeated all-int scan,
    // no whole-value JsonElement check inside a proven-List branch.
    [Fact]
    public void IsColor_ListBranch_TestsPositionalIntsOnce()
    {
        Assert.True(IsColor(new List<object?> { 255, 0, 0 }));
        Assert.True(IsColor(new List<object?> { -1, -1, -1 }));
        Assert.False(IsColor(new List<object?> { 300, 0, 0 }));
        Assert.False(IsColor(new List<object?> { "red", 0, 0 }));
        Assert.False(IsColor("red"));
        using var doc = JsonDocument.Parse("[255,0,0]");
        Assert.True(IsColor(doc.RootElement));
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = SourceScan.Region(src, "private static bool IsColor(");
        Assert.DoesNotContain("lst.All", region);
        Assert.DoesNotContain("JsonElement case", region);
    }

    // The key is a string past the null guard: only the cells shape is
    // still conditional.
    [Fact]
    public void MapEditHandler_KeyGuard_ShapesOnlyCells()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = SourceScan.Region(src, "public void MapEditHandler(");
        Assert.DoesNotContain("key is not string", region);
        Assert.Contains("cellsObj is not List<object?>", region);
    }

    // Disconnect drops the empty always-true branch and calls the
    // self-locking setter directly instead of nesting its lock.
    [Fact]
    public void Disconnect_NoEmptyBranch_NoNestedLock()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
        var region = SourceScan.Region(src, "public virtual void Disconnect(");
        Assert.DoesNotContain("else if (connId == null", region);
        Assert.DoesNotContain("lock (connection.Lock) { connection.SetDisconnected(true); }", region);
        Assert.Contains("connection.SetDisconnected(true);", region);
    }

    // Both busy paths capture the queue size, so the busy log never reports
    // an empty queue while refusing a full one.
    [Fact]
    public void EnqueueInput_BusyLog_CountsBothPaths()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "BaseConnection.cs");
        var region = SourceScan.Region(src, "public void EnqueueInput(");
        Assert.Equal(2, SourceScan.Count(region, "pendingCount = _inputQueue.Count;"));
    }

    // One scope level in the WS setup: no doubled braces.
    [Fact]
    public void WebSocketSetup_SingleScopeLevel()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "WebSocketProtocol.cs");
        Assert.DoesNotContain("{\n            {", src);
    }

    // The send timeout is armed once at connect, not syscall'd per write.
    [Fact]
    public void TelnetWriter_SendTimeout_ArmedAtConnect()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Network", "TelnetProtocol.cs");
        Assert.Contains("Suppressed TelnetStreamWriter.Connect", src);
        Assert.DoesNotContain("Suppressed TelnetStreamWriter.Write", src);
        Assert.DoesNotContain("Suppressed TelnetStreamWriter.IacWithText", src);
    }

    // Id generation is a lock-free counter: unique under concurrency.
    [Fact]
    public void GenerateConnectionId_UniqueUnderConcurrency()
    {
        using var env = Atheriz.Core.Tests.GlobalTestEnv.Enter();
        var mgr = Atheriz.Core.Tests.Ported.PortedHelpers.MakeManager();
        try
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<string>();
            var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
            {
                for (int i = 0; i < 50; i++) bag.Add(mgr.GenerateConnectionId());
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join(10000));
            Assert.Equal(400, bag.Count);
            Assert.Equal(400, new HashSet<string>(bag).Count);
            var src = SourceScan.Read("src", "Atheriz.Core", "Network", "ConnectionManager.cs");
            Assert.Contains("Interlocked.Increment(ref _connectionCounter)", src);
        }
        finally { mgr.Atp.Stop(wait: false); }
    }
}
