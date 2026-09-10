using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The shared server-channel validator keeps the double-checked shape: a live
// channel resolves (and caches), while a deleted or renamed cached channel is
// dropped and rescanned instead of served stale.
[Collection("Ported")]
public class ServerChannelValidationTests
{
    private static Channel LiveServerChannel()
    {
        var chan = new Channel();
        chan.Name = "server";
        chan.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(chan);
        return chan;
    }

    [Fact]
    public void GetServerChannel_DeletedChannel_InvalidatesCache()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.Reset();
        ObjectRegistry.ClearAll();
        try
        {
            var chan = LiveServerChannel();
            Assert.Same(chan, GlobalServices.GetServerChannel());
            Assert.Same(chan, GlobalServices.GetServerChannel());
            chan.IsDeleted = true;
            Assert.Null(GlobalServices.GetServerChannel());
            Assert.Null(GlobalServices.GetServerChannel());
        }
        finally { GlobalServices.Reset(); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetServerChannel_RenamedChannel_InvalidatesCache()
    {
        using var env = GlobalTestEnv.Enter();
        GlobalServices.Reset();
        ObjectRegistry.ClearAll();
        try
        {
            var chan = LiveServerChannel();
            Assert.Same(chan, GlobalServices.GetServerChannel());
            chan.Name = "other";
            Assert.Null(GlobalServices.GetServerChannel());
        }
        finally { GlobalServices.Reset(); ObjectRegistry.ClearAll(); }
    }
}
