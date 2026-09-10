using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Puppet snapshot restore: one lookup with ordered arms decides exactly what
// the double lookup did — boxed enum and int forms both restore, unknown
// types are ignored.
[Collection("Ported")]
public class PuppetSnapshotRoundTripTests
{
    [Fact]
    public void RestorePuppetSnapshot_AcceptsEnumAndIntForms()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("npc");
            obj.IsPc = false;
            obj.PrivilegeLevel = Privilege.Player;

            obj.RestorePuppetSnapshot(new Dictionary<string, object>
            {
                ["is_pc"] = true,
                ["privilege_level"] = Privilege.Admin,
            });
            Assert.True(obj.IsPc);
            Assert.Equal(Privilege.Admin, obj.PrivilegeLevel);

            obj.RestorePuppetSnapshot(new Dictionary<string, object>
            {
                ["is_pc"] = false,
                ["privilege_level"] = (int)Privilege.Helper,
            });
            Assert.False(obj.IsPc);
            Assert.Equal(Privilege.Helper, obj.PrivilegeLevel);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RestorePuppetSnapshot_IgnoresUnknownTypes()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var obj = GameObject.Create("npc");
            obj.PrivilegeLevel = Privilege.Player;

            obj.RestorePuppetSnapshot(new Dictionary<string, object>
            {
                ["privilege_level"] = "admin",
            });

            Assert.Equal(Privilege.Player, obj.PrivilegeLevel);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
