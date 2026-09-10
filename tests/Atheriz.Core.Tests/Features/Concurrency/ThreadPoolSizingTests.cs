using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Thread-pool sizing matches Python's async-plus-workers layout.
[Collection("Ported")]
public class ThreadPoolSizingTests
{
    // --- AsyncThreadPool worker count ---

    [Fact]
    public void ThreadPool_FixedThreads_MatchPythonLayout()
    {
        // maxThreads counts the async slot plus (maxThreads-1) fixed workers,
        // mirroring Python's threads[0] async + threads[1:] workers layout.
        // Existing saturation tests pin this contract; this pins it explicitly.
        var pool = new AsyncThreadPool(maxThreads: 3);
        try
        {
            Assert.Equal(3, pool.MaxThreads);
            Assert.Equal(2, pool.FixedThreads.Count);
        }
        finally { pool.Stop(wait: false); }
    }
}
