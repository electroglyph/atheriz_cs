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

// Lock-exposure contract (wontfix, see AGENTS.md hard rules): the raw sync
// primitives stay public. External game code depends on them for atomic
// check-and-set across Session.Lock + character SyncRoot (puppet selection;
// a public-API rewrite would be check-then-set and race double-puppeting)
// and area-wide enumeration (area.Grids, grid.Nodes have no snapshot API).
// A previous internal-visibility attempt was reverted for this reason.
// This test pins the public surface: if SyncRoot ever stops being public,
// external game code breaks.
[Collection("Ported")]
public class LockExposureTests
{
    // --- Raw lock exposure ---

    [Fact]
    public void GameObject_SyncRoot_IsPublic()
    {
        var prop = typeof(GameObject).GetProperty("SyncRoot",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.NotNull(prop);
        Assert.True(prop!.GetMethod!.IsPublic);
    }
}
