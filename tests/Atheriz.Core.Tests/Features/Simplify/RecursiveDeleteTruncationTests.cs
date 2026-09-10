using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Recursive delete: the depth cap records capped children without visiting
// them (the membership test is load-bearing), so over-deep survivors are
// detached, not deleted.
[Collection("Ported")]
public class RecursiveDeleteTruncationTests
{
    [Fact]
    public void DeleteRecursive_DetachesOverDepthSurvivors()
    {
        ObjectRegistry.ClearAll();
        int prevDepth = GameObject.MaxSearchDepth;
        GameObject.MaxSearchDepth = 4;
        try
        {
            var chain = new List<GameObject>();
            for (int i = 0; i < 8; i++)
            {
                var o = GameObject.Create($"c{i}", isContainer: true);
                ObjectRegistry.AddObject(o);
                chain.Add(o);
                if (i > 0) chain[i - 1].AddObject(o);
            }
            var root = chain[0];

            var result = root.Delete(null, recursive: true);

            Assert.NotNull(result);
            Assert.True(root.IsDeleted);
            Assert.True(chain[3].IsDeleted);
            // Depth-capped: c4 and below survive, detached to nowhere.
            Assert.False(chain[4].IsDeleted);
            Assert.NotEmpty(ObjectRegistry.Get(chain[4].Id));
            Assert.IsType<LocationRef.NullLocation>(chain[4].Location);
            Assert.False(chain[7].IsDeleted);
        }
        finally
        {
            GameObject.MaxSearchDepth = prevDepth;
            ObjectRegistry.ClearAll();
        }
    }
}
