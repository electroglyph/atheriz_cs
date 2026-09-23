using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// A corrupt group-channel reference must be reported, never thrown (GroupCommand.cs:27,
// contrast the safe lookup at :37).
[Collection("Ported")]
public class GroupCommandTests
{
    [Fact]
    public void Group_List_CorruptChannelRef_DoesNotThrow()
    {
        // When the stored id points at a non-channel, list must answer gracefully.
        ObjectRegistry.ClearAll();
        try
        {
            var hero = GameObject.Create("hero");
            var fake = GameObject.Create("pub");
            ObjectRegistry.AddObject(hero);
            ObjectRegistry.AddObject(fake);
            hero.GroupChannel = fake.Id;
            hero.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(hero, "group list", immediate: true);
            Assert.NotNull(job);
            var ex = Record.Exception(() => job!.Func(job.Caller, job.Args));
            Assert.Null(ex);
            Assert.Contains("Group channel not found", string.Join("\n", hero.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GroupAdd_MultiWordName_JoinsRemainder()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var room = new Node(new Coord("f1room", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var hero = GameObject.Create("hero", isPc: true);
            var bob = GameObject.Create("Big Bob", isNpc: true);
            ObjectRegistry.AddObject(hero);
            ObjectRegistry.AddObject(bob);
            Assert.True(hero.MoveTo(room));
            Assert.True(bob.MoveTo(room));
            hero.AddFollower(bob.Id);
            hero.ClearMessages();

            var job = CommandDispatcher.DispatchLoggedIn(hero, "group add Big Bob", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);

            var text = string.Join("\n", hero.PeekMessages());
            Assert.Contains("added Big Bob", text);
            Assert.DoesNotContain("Could not find", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupKick_MultiWordName_JoinsRemainder()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var room = new Node(new Coord("f1kroom", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var hero = GameObject.Create("hero", isPc: true);
            var bob = GameObject.Create("Big Bob", isNpc: true);
            ObjectRegistry.AddObject(hero);
            ObjectRegistry.AddObject(bob);
            Assert.True(hero.MoveTo(room));
            Assert.True(bob.MoveTo(room));
            hero.AddFollower(bob.Id);
            var add = CommandDispatcher.DispatchLoggedIn(hero, "group add Big Bob", immediate: true);
            add!.Func(add.Caller, add.Args);
            hero.ClearMessages();

            var kick = CommandDispatcher.DispatchLoggedIn(hero, "group kick Big Bob", immediate: true);
            Assert.NotNull(kick);
            kick!.Func(kick.Caller, kick.Args);

            var text = string.Join("\n", hero.PeekMessages());
            Assert.Contains("kicked Big Bob", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupAdd_TargetAlreadyGrouped_RefusesWithoutDualMembership()
    {
        // Single-membership model: adding a target that already holds a
        // group channel refuses instead of silently subscribing them to a
        // second group's traffic (leave/list would only see the new group).
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var room = new Node(new Coord("o5room", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var alice = GameObject.Create("alice", isPc: true);
            var carol = GameObject.Create("carol", isPc: true);
            var bob = GameObject.Create("bob", isPc: true);
            ObjectRegistry.AddObject(alice);
            ObjectRegistry.AddObject(carol);
            ObjectRegistry.AddObject(bob);
            // Group targets are online PCs: default PcView hides
            // disconnected characters from room search.
            bob.IsConnected = true;
            Assert.True(alice.MoveTo(room));
            Assert.True(carol.MoveTo(room));
            Assert.True(bob.MoveTo(room));
            alice.AddFollower(bob.Id);
            carol.AddFollower(bob.Id);

            var add = CommandDispatcher.DispatchLoggedIn(alice, "group add bob", immediate: true);
            Assert.NotNull(add);
            add!.Func(add.Caller, add.Args);
            Assert.NotNull(alice.GroupChannel);
            int aliceCh = alice.GroupChannel!.Value;

            carol.ClearMessages();
            var steal = CommandDispatcher.DispatchLoggedIn(carol, "group add bob", immediate: true);
            Assert.NotNull(steal);
            steal!.Func(steal.Caller, steal.Args);

            Assert.Contains("already in a group", string.Join("\n", carol.PeekMessages()));
            Assert.Equal(aliceCh, bob.GroupChannel);
            Assert.Null(carol.GroupChannel);
            var ch = ObjectRegistry.GetSingle(aliceCh) as Channel;
            Assert.NotNull(ch);
            Assert.Contains(bob.Id, ch!.Listeners);
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
