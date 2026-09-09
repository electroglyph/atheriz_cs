using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for the P3 batch 7 (N-9..N-13).
[Collection("Ported")]
public class P3BatchSevenTests
{
    private static async Task<List<string?>> Collect(IEnumerable<string> chunks, int maxLine)
    {
        var reader = new System.IO.StringReader(string.Concat(chunks));
        var list = new List<string?>();
        await foreach (var line in TelnetProtocol.ReadCappedLines(reader, maxLine))
            list.Add(line);
        return list;
    }

    // An overlong tail closed by EOF yields the same drop marker as a
    // mid-stream overlong line instead of vanishing silently.
    [Fact]
    public async Task OverlongEofTail_YieldsDropMarker()
    {
        var res = await Collect(new[] { new string('x', 100) }, maxLine: 10);
        Assert.Single(res);
        Assert.Null(res[0]);
    }

    [Fact]
    public async Task OverlongMidStreamLine_YieldsDropMarker()
    {
        var res = await Collect(new[] { new string('x', 100) + "\r\n", "ok\r\n" }, maxLine: 10);
        Assert.Equal(2, res.Count);
        Assert.Null(res[0]);
        Assert.Equal("ok", res[1]);
    }

    // A zero reserve takes no count slot: reserve and release are both
    // no-ops for zero, so neither leaks nor double-returns.
    [Fact]
    public void PendingLimiter_ZeroReserve_TakesNoSlot()
    {
        var lim = new PendingLimiter(maxBytes: 1024, maxCount: 4);
        Assert.True(lim.TryReserve(0));
        Assert.Equal(0, lim.PendingCount);
        lim.ReleaseSync(0);
        Assert.Equal(0, lim.PendingCount);
        Assert.True(lim.TryReserve(10));
        Assert.Equal(1, lim.PendingCount);
        lim.ReleaseSync(10);
        Assert.Equal(0, lim.PendingCount);
    }

    // Tick-delegate removal builds the id set once per sweep and matches by
    // id (same instance shares the id; a pre-patch delegate shares the
    // replacement's id but never the reference).
    [Fact]
    public void PluginReloader_TickSweep_BuildsIdSetOnce()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Plugins", "PluginReloader.cs");
        var region = SourceScan.Region(src, "private static void RemoveTickDelegatesFor(");
        // Built once before the per-delegate walk, not once per delegate.
        Assert.True(region.IndexOf("new HashSet<int>()", StringComparison.Ordinal)
            < region.IndexOf("foreach (var d in coros.ToList())", StringComparison.Ordinal));
        Assert.Contains("TargetsTickable(d.Target, tickableIds)", region);
        Assert.Contains("if (target is GameObject self && tickableIds.Contains(self.Id)) return true;",
            SourceScan.Region(src, "private static bool TargetsTickable("));
    }

    // Timer loop faults are logged where they happen instead of dying in an
    // unobserved task, and a stillborn (canceled) timer never reports
    // running.
    [Fact]
    public void AsyncTicker_LoopFaults_Logged_StillbornNotRunning()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncTicker.cs");
        var timer = SourceScan.Region(src, "private async Task TimerAsync(");
        Assert.Contains("catch (Exception ex)", timer);
        var start = SourceScan.Region(src, "public void Start()");
        Assert.Contains("IsCanceled: true", start);
    }

    // Relief spawn starts before list-add under one lock: a Start throw
    // restores the count and leaves no phantom entry.
    [Fact]
    public void ReliefSpawn_StartBeforeAdd_CountRestoredOnFailure()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Concurrency", "AsyncThreadPool.cs");
        var region = SourceScan.Region(src, "AtherizRelief-{seq}");
        Assert.Contains("t.Start(true);", region);
        Assert.Contains("_reliefCount--;", region);
        Assert.Contains("_reliefThreads.Add(t);", region);
        Assert.True(region.IndexOf("t.Start(true);", StringComparison.Ordinal)
            < region.IndexOf("_reliefThreads.Add(t);", StringComparison.Ordinal));
    }
}
