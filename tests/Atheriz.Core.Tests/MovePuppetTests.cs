using Atheriz.Core;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests;

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
}
