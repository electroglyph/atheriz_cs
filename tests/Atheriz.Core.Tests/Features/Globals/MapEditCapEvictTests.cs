using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Globals;

// The bulk cap eviction preserves the oldest-first order: overflowing the
// chain cap drops the earliest grants and keeps the newest, and a single
// grant past a full cap keeps the count pinned.
[Collection("Ported")]
public class MapEditCapEvictTests
{
    [Fact]
    public void Grant_BeyondCap_EvictsOldestFirst()
    {
        MapEdit.Reset();
        try
        {
            var keys = new List<string>();
            for (int i = 0; i < MapEdit.MaxChains + 44; i++)
                keys.Add(MapEdit.Grant("10.0.0.1", "evictarea", 0));
            Assert.Equal(MapEdit.MaxChains, MapEdit.ChainsSnapshot.Count);
            for (int i = 0; i < 44; i++) Assert.Null(MapEdit.GetChain(keys[i]));
            Assert.NotNull(MapEdit.GetChain(keys[44]));
            Assert.NotNull(MapEdit.GetChain(keys[^1]));
        }
        finally { MapEdit.Reset(); }
    }

    [Fact]
    public void Grant_AtFullCap_KeepsCountPinned()
    {
        MapEdit.Reset();
        try
        {
            for (int i = 0; i < MapEdit.MaxChains; i++)
                MapEdit.Grant("10.0.0.2", "evictfull", 0);
            Assert.Equal(MapEdit.MaxChains, MapEdit.ChainsSnapshot.Count);
            MapEdit.Grant("10.0.0.2", "evictfull", 0);
            Assert.Equal(MapEdit.MaxChains, MapEdit.ChainsSnapshot.Count);
        }
        finally { MapEdit.Reset(); }
    }
}
