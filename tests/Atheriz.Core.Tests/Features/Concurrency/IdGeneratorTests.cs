using System.Collections.Concurrent;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Parallel id generation never duplicates.
[Collection("Ported")]
public class IdGeneratorTests
{
    [Fact]
    public void ParallelGetUniqueId_YieldsDistinctIds()
    {
        // GetUniqueId (IdGenerator.cs:26-30) hands out a fresh id per call even
        // when 10k calls race on the lock.
        const int count = 10000;
        var ids = new ConcurrentBag<int>();
        Parallel.For(0, count, _ => ids.Add(IdGenerator.GetUniqueId()));
        Assert.Equal(count, ids.Count);
        Assert.Equal(count, ids.Distinct().Count());
    }
}
