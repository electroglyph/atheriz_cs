using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Deleting a puppet target or a stacked puppet origin must not leave the
// session rewired onto a deleted object.
[Collection("Ported")]
public class UnpuppetAfterDeleteTests
{
    private static (Session session, GameObject pc, GameObject npc) MakePuppetPair()
    {
        var session = new Session();
        var pc = GameObject.Create("Pc", isPc: true);
        pc.PrivilegeLevel = Privilege.Builder;
        ObjectRegistry.AddObject(pc);
        var npc = GameObject.Create("Npc", isNpc: true);
        ObjectRegistry.AddObject(npc);
        session.Puppet = pc;
        pc.Session = session;
        pc.IsConnected = true;
        Assert.True(pc.Puppet(session, npc));
        return (session, pc, npc);
    }

    [Fact]
    public void Unpuppet_AfterTargetDelete_DoesNotRewire()
    {
        // Deleting the puppet target unwinds the stack, so Unpuppet has
        // nothing to pop and must not rewire the session.
        using var env = GlobalTestEnv.Enter();
        var (session, pc, npc) = MakePuppetPair();

        npc.Delete(pc, true);
        Assert.True(npc.IsDeleted);
        Assert.Null(session.Puppet);
        Assert.False(pc.Unpuppet(session));
        Assert.Null(session.Puppet);
    }

    [Fact]
    public void Unpuppet_AfterPrevDelete_DoesNotRewireDeleted()
    {
        // When the stacked origin was deleted, Unpuppet must not point the
        // live session back at the deleted object.
        using var env = GlobalTestEnv.Enter();
        var (session, pc, npc) = MakePuppetPair();

        pc.Delete(npc, true);
        Assert.True(pc.IsDeleted);
        Assert.True(pc.Unpuppet(session));
        Assert.Null(session.Puppet);
        Assert.Null(pc.Session);
    }
}
