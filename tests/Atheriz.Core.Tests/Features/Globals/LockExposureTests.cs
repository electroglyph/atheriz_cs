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

// Lock-exposure direction: raw sync primitives must not stay public.
// Hiding them (to prevent external lock-order inversion, e.g. Channel.Msg
// vs delete-detach) is deferred work; this test stays red until it lands.
[Collection("Ported")]
public class LockExposureTests
{
    // --- Raw lock exposure ---

    [Fact]
    public void GameObject_SyncRoot_IsNotPublic()
    {
        // Raw ReaderWriterLockSlim exposure (SyncRoot / NodeLock / Lock plus
        // ReadScope/WriteScope and raw Enter*) lets external code invert the
        // lock order (Channel.Msg vs Delete-detach today), so SyncRoot must become non-public.
        var prop = typeof(GameObject).GetProperty("SyncRoot");
        Assert.NotNull(prop);
        Assert.False(prop!.GetMethod!.IsPublic);
    }
}
