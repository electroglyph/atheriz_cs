using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Globals;

// The lazy FIFO deletes preserve eviction order exactly: removes retire one
// queue slot (even across re-adds), absent removes are no-ops, and
// RemoveIfEqual stays atomic — so overflow always evicts the oldest LIVE key.
[Collection("Ported")]
public class BoundedDictionaryTombstoneTests
{
    private const int Limit = 4000;

    [Fact]
    public void BoundedDictionary_Overflow_EvictsOldest()
    {
        var d = new ObjectRegistry.BoundedDictionary<string, int>();
        for (int i = 0; i < Limit + 1; i++) d.Set($"k{i}", i);
        Assert.Equal(Limit, d.Count);
        Assert.False(d.Contains("k0"));
        Assert.True(d.Contains($"k{Limit}"));
    }

    [Fact]
    public void BoundedDictionary_RemoveThenOverflow_EvictsOldestLive()
    {
        var d = new ObjectRegistry.BoundedDictionary<string, int>();
        for (int i = 0; i < Limit; i++) d.Set($"k{i}", i);
        d.Remove("k0");
        d.Set("fresh", -1);
        Assert.Equal(Limit, d.Count);
        Assert.False(d.Contains("k0"));
        d.Set("overflow", -2);
        Assert.Equal(Limit, d.Count);
        Assert.False(d.Contains("k1"));
        Assert.True(d.Contains("k2"));
        Assert.True(d.Contains("fresh"));
        Assert.True(d.Contains("overflow"));
    }

    [Fact]
    public void BoundedDictionary_ReaddedKey_EvictsByNewestPosition()
    {
        var d = new ObjectRegistry.BoundedDictionary<string, int>();
        for (int i = 0; i < Limit; i++) d.Set($"k{i}", i);
        d.Remove("k0");
        d.Set("k0", 99);
        d.Set("extra", -1);
        // The re-added k0 is newest; the oldest live key k1 goes instead.
        Assert.False(d.Contains("k1"));
        Assert.True(d.TryGetValue("k0", out var v) && v == 99);
        Assert.True(d.Contains("extra"));
    }

    [Fact]
    public void BoundedDictionary_RemoveAbsent_IsNoop()
    {
        var d = new ObjectRegistry.BoundedDictionary<string, int>();
        d.Set("a", 1);
        d.Remove("missing");
        Assert.Equal(1, d.Count);
        d.Set("b", 2);
        // No phantom retirement: both live keys survive the next insert.
        d.Set("c", 3);
        Assert.True(d.Contains("a"));
        Assert.True(d.Contains("b"));
        Assert.True(d.Contains("c"));
    }

    [Fact]
    public void BoundedDictionary_RemoveIfEqual_IsAtomic()
    {
        var d = new ObjectRegistry.BoundedDictionary<string, int>();
        d.Set("k", 1);
        // A stale expiry value must not delete a concurrently refreshed entry.
        Assert.False(d.RemoveIfEqual("k", 2));
        Assert.True(d.Contains("k"));
        Assert.True(d.RemoveIfEqual("k", 1));
        Assert.False(d.Contains("k"));
        Assert.False(d.RemoveIfEqual("k", 1));
    }

    [Fact]
    public void BoundedDictionary_ConcurrentRemoves_StayConsistent()
    {
        var d = new ObjectRegistry.BoundedDictionary<string, int>();
        for (int i = 0; i < 1000; i++) d.Set($"c{i}", i);
        System.Threading.Tasks.Parallel.For(0, 500, i => d.Remove($"c{i}"));
        Assert.Equal(500, d.Count);
        for (int i = 500; i < 1000; i++) Assert.True(d.Contains($"c{i}"));
        // Churn under the limit leaves later inserts and counts exact.
        for (int i = 0; i < 500; i++) d.Set($"n{i}", i);
        Assert.Equal(1000, d.Count);
    }
}
