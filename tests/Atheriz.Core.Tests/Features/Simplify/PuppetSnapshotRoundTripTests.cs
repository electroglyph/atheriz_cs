using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Puppet snapshot restore: the typed record carries both fields, so restore
// applies exactly what the snapshot holds — no lookup arms, no ignored types.
[Collection("Ported")]
public class PuppetSnapshotRoundTripTests
{
    [Fact]
    public void RestorePuppetSnapshot_AppliesRecordFields()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("npc");
            obj.IsPc = false;
            obj.PrivilegeLevel = Privilege.Player;

            obj.RestorePuppetSnapshot(new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin));
            Assert.True(obj.IsPc);
            Assert.Equal(Privilege.Admin, obj.PrivilegeLevel);

            obj.RestorePuppetSnapshot(new GameObject.PuppetRestoreSnapshot(false, Privilege.Helper));
            Assert.False(obj.IsPc);
            Assert.Equal(Privilege.Helper, obj.PrivilegeLevel);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void PuppetRestoreSnapshot_ComparesByValue()
    {
        Assert.Equal(
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin),
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin));
        Assert.NotEqual(
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin),
            new GameObject.PuppetRestoreSnapshot(false, Privilege.Admin));
        Assert.NotEqual(
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Admin),
            new GameObject.PuppetRestoreSnapshot(true, Privilege.Guest));
    }
}
