using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Globals;

// The shared unassigned-id sentinel keeps counter semantics: Reset restores
// the sentinel, allocation starts at zero, and the watermark still applies.
[Collection("Ported")]
public class IdGeneratorSentinelTests
{
    [Fact]
    public void IdGenerator_Reset_RestoresInitialSentinel()
    {
        int orig = IdGenerator.GetId();
        try
        {
            IdGenerator.SetId(41);
            Assert.Equal(41, IdGenerator.GetId());
            IdGenerator.Reset();
            Assert.Equal(-1, IdGenerator.GetId());
            Assert.Equal(0, IdGenerator.GetUniqueId());
            IdGenerator.EnsureAtLeast(10);
            Assert.Equal(11, IdGenerator.GetUniqueId());
        }
        finally { IdGenerator.SetId(orig); }
    }
}
