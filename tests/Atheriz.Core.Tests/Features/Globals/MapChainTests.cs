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

namespace Atheriz.Core.Tests.Features.Globals;

// MapEdit chain copy-on-write snapshot behavior.
[Collection("Ported")]
public class MapChainTests
{
    // --- MapEdit live references ---

    [Fact]
    public void MapEdit_GetChain_ReturnsCopy_NotLiveReference()
    {
        // GetChain returns a copy rather than the stored mutable chain, so
        // in-place Consume mutation of Key/Seq/PreviousKey never tears
        // concurrent readers.
        MapEdit.ResetForTesting();
        try
        {
            string key = MapEdit.Grant("9.9.9.9", "limbo", 0, session: null);
            var c1 = MapEdit.GetChain(key);
            Assert.NotNull(c1);
            string orig = c1!.Key;
            c1.Key = "MUTATED";
            var c2 = MapEdit.GetChain(key);
            Assert.NotNull(c2);
            Assert.Equal(orig, c2!.Key);
        }
        finally { MapEdit.ResetForTesting(); }
    }
}
