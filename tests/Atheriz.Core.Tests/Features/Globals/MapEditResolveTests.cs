using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Globals;

// The shared chain resolver preserves every lookup branch: direct hits,
// previous-key hits (retry vs replay), gaps, ip mismatches, unknown keys,
// and the advisory validator — with the documented reason strings.
[Collection("Ported")]
public class MapEditResolveTests
{
    [Fact]
    public void Consume_RetryReplayGapIpUnknown_PreserveReasons()
    {
        MapEdit.Reset();
        try
        {
            var k1 = MapEdit.Grant("5.5.5.5", "resolvearea", 0);
            Assert.True(MapEdit.ValidateChain(k1, "5.5.5.5", 0));
            Assert.False(MapEdit.ValidateChain(k1, "5.5.5.5", 5));
            Assert.False(MapEdit.ValidateChain(k1, "9.9.9.9", 0));
            var r = MapEdit.Consume(k1, "5.5.5.5", 0);
            Assert.Equal(MapEditStatus.Processed, r.Status);
            var k2 = r.NewKey!;
            // Previous-key hit with the rotated seq retries with the new key.
            var retry = MapEdit.Consume(k1, "5.5.5.5", 0);
            Assert.Equal(MapEditStatus.Retry, retry.Status);
            Assert.Equal(k2, retry.NewKey);
            // Previous-key hit with any other seq is a replay.
            var replay = MapEdit.Consume(k1, "5.5.5.5", 7);
            Assert.Equal(MapEditStatus.Reject, replay.Status);
            Assert.Equal("replay", replay.Reason);
            // Direct hit jumping ahead is a gap; wrong ip is rejected as ip.
            var gap = MapEdit.Consume(k2, "5.5.5.5", 9);
            Assert.Equal(MapEditStatus.Reject, gap.Status);
            Assert.Equal("gap", gap.Reason);
            var ip = MapEdit.Consume(k2, "8.8.8.8", 1);
            Assert.Equal(MapEditStatus.Reject, ip.Status);
            Assert.Equal("ip", ip.Reason);
            var unk = MapEdit.Consume("no-such-key", "5.5.5.5", 0);
            Assert.Equal(MapEditStatus.Reject, unk.Status);
            Assert.Equal("unknown_key", unk.Reason);
        }
        finally { MapEdit.Reset(); }
    }

    [Fact]
    public void GetChain_PreviousHit_ResolvesWithoutDroppingMapping()
    {
        MapEdit.Reset();
        try
        {
            var k1 = MapEdit.Grant("5.5.5.6", "resolvekeep", 0);
            var r = MapEdit.Consume(k1, "5.5.5.6", 0);
            Assert.Equal(MapEditStatus.Processed, r.Status);
            // Read-path resolution leaves the previous-key mapping in place.
            Assert.NotNull(MapEdit.GetChain(k1));
            Assert.NotNull(MapEdit.GetChain(k1));
            Assert.True(MapEdit.ValidateChain(k1, "5.5.5.6", 0));
            Assert.False(MapEdit.ValidateChain(k1, "5.5.5.6", 3));
            Assert.True(MapEdit.ValidateChain(r.NewKey!, "5.5.5.6", 1));
        }
        finally { MapEdit.Reset(); }
    }
}
