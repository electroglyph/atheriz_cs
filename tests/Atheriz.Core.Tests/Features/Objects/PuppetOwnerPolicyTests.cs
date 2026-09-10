using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// The Create-time puppet lock is the PuppetOwner policy (single spelling):
// NPCs are open, superusers bypass, and owned characters admit their owner's
// session while strangers are refused.
[Collection("Ported")]
public class PuppetOwnerPolicyTests
{
    [Fact]
    public void OwnerSession_CanPuppet_StrangerCannot()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var target = GameObject.Create("victim");
            var avatar = GameObject.Create("avatar");
            var acc = new Account();
            acc.AddCharacter(target);
            avatar.Session = new Session(connection: null, account: acc);
            Assert.True(target.Access(avatar, "puppet"));
            var stranger = GameObject.Create("stranger");
            Assert.False(target.Access(stranger, "puppet"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void NpcOpen_SuperuserBypass()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var npc = GameObject.Create("mob", isNpc: true);
            var stranger = GameObject.Create("stranger");
            Assert.True(npc.Access(stranger, "puppet"));
            var target = GameObject.Create("victim");
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            Assert.True(target.Access(admin, "puppet"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
