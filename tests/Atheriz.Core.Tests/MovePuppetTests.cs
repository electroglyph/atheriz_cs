using Atheriz.Core;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests;

[Collection("Ported")]
public class MovePuppetTests
{
    [Fact]
    public void MoveTo_SelfCycle_Guard()
    {
        Globals.ObjectRegistry.ClearAll();
        var room = GameObject.Create("room");
        room.IsContainer = true;
        Globals.ObjectRegistry.AddObject(room);

        var bag = GameObject.Create("bag");
        bag.IsContainer = true;
        room.AddObject(bag);
        Globals.ObjectRegistry.AddObject(bag);

        var coin = GameObject.Create("coin");
        bag.AddObject(coin);
        Globals.ObjectRegistry.AddObject(coin);

        // bag contains coin — moving bag into coin should be blocked (descendant cycle)
        Assert.False(bag.MoveTo(coin));

        // self move also blocked
        Assert.False(bag.MoveTo(bag));

        // valid move into room (already there but should succeed or at least not cycle)
        // Move coin from bag to room
        Assert.True(coin.MoveTo(room));
        Assert.IsType<Atheriz.Core.Persistence.Dto.LocationRef.ObjectLocation>(coin.Location);
        var loc = (Atheriz.Core.Persistence.Dto.LocationRef.ObjectLocation)coin.Location;
        Assert.Equal(room.Id, loc.ObjectId);
        Assert.Contains(coin.Id, room.ContentsSnapshot);
        Assert.DoesNotContain(coin.Id, bag.ContentsSnapshot);

        Globals.ObjectRegistry.ClearAll();
    }

    [Fact]
    public void Puppet_Snapshot_OnlyIsPcAndPrivilegeLevel_Wontfix()
    {
        Globals.ObjectRegistry.ClearAll();
        var session = new Session(connection: null);
        var builder = GameObject.Create("Builder", isPc: true);
        builder.PrivilegeLevel = Privilege.Builder;
        builder.Quelled = false;
        builder.CanHear = true;
        builder.IsMapable = true;
        Globals.ObjectRegistry.AddObject(builder);

        var npc = GameObject.Create("Npc", isNpc: true);
        npc.IsPc = false;
        npc.PrivilegeLevel = Privilege.Guest;
        npc.Quelled = true; // should NOT be saved/restored per wontfix
        npc.CanHear = true;
        npc.IsMapable = false;
        Globals.ObjectRegistry.AddObject(npc);

        // Wire session puppet to builder initially (simulate login)
        session.Puppet = builder;
        builder.Session = session;

        bool ok = builder.Puppet(session, npc);
        Assert.True(ok);
        // After puppet, npc should be Pc and have builder privilege, but quelled unchanged (wontfix)
        Assert.True(npc.IsPc);
        Assert.Equal(Privilege.Builder, npc.PrivilegeLevel);
        // Wontfix: quelled not restored — remains true (original npc quelled)
        Assert.True(npc.Quelled);
        // CanHear unchanged (still true), IsMapable unchanged? Actually IsMapable stays false? In Python puppet would not change IsMapable, only IsPc/priv. Our code only sets IsPc, so IsMapable remains false per wontfix.
        Assert.False(npc.IsMapable);

        // Session stack should have one entry
        Assert.Single(session.PuppetStack);
        Assert.Equal(npc, session.Puppet);

        // Unpuppet should restore IsPc/privilege only
        bool ok2 = builder.Unpuppet(session);
        Assert.True(ok2);
        Assert.False(npc.IsPc); // restored
        Assert.Equal(Privilege.Guest, npc.PrivilegeLevel);
        Assert.True(npc.Quelled); // still true, not cleared
        Assert.Equal(builder, session.Puppet);
        Assert.Empty(session.PuppetStack);

        Globals.ObjectRegistry.ClearAll();
    }

    private sealed class RepuppetNpc : GameObject
    {
        public Session? S2;
        public GameObject? P2;
        public bool HookRan;
        public override void AtUnpuppet(GameObject caller)
        {
            HookRan = true;
            // Simulate the race: a second session puppets the target while the
            // first Unpuppet is inside AtUnpuppet (target.Session is null here).
            if (S2 != null && P2 != null) P2.Puppet(S2, this);
        }
    }

    [Fact]
    public void Unpuppet_MidHookRepuppet_KeepsNewPuppet()
    {
        // Unpuppet must not apply its stale restore over a puppet installed
        // during AtUnpuppet (owner decision 2026-09-08): the new owner keeps
        // IsPc/session/snapshot.
        Globals.ObjectRegistry.ClearAll();
        var s1 = new Session(connection: null);
        var p1 = GameObject.Create("P1", isPc: true);
        p1.PrivilegeLevel = Privilege.Builder;
        Globals.ObjectRegistry.AddObject(p1);
        var s2 = new Session(connection: null);
        var p2 = GameObject.Create("P2", isPc: true);
        p2.PrivilegeLevel = Privilege.Builder;
        Globals.ObjectRegistry.AddObject(p2);
        var npc = new RepuppetNpc();
        npc.Id = Globals.IdGenerator.GetUniqueId();
        npc.Name = "Npc";
        npc.IsNpc = true;
        npc.IsPc = false;
        npc.PrivilegeLevel = Privilege.Guest;
        Globals.ObjectRegistry.AddObject(npc);
        npc.S2 = s2;
        npc.P2 = p2;

        s1.Puppet = p1;
        p1.Session = s1;
        Assert.True(p1.Puppet(s1, npc));
        Assert.True(npc.IsPc);
        s2.Puppet = p2;
        p2.Session = s2;

        Assert.True(p1.Unpuppet(s1));
        Assert.True(npc.HookRan);
        // New puppet owns the target: stale restore skipped.
        Assert.Same(npc, s2.Puppet);
        Assert.Same(s2, npc.Session);
        Assert.True(npc.IsPc);

        Globals.ObjectRegistry.ClearAll();
    }
}
