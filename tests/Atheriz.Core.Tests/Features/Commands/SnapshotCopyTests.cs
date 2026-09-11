// Direct snapshot pass-through: inventory, drop-all,
// get-all, give, and nofollow read the same contents with the direct
// snapshot pass-through as with the removed defensive copies.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class SnapshotCopyTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (NodeHandler Nh, Node Node) EnterRoom(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        return (nh, node);
    }

    [Fact]
    public void InventoryCommand_RunWithItems_ListsContents()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("b19inv");
        try
        {
            var holder = GameObject.Create("holder", isPc: true);
            ObjectRegistry.AddObject(holder);
            Assert.True(holder.MoveTo(node));
            var apple = GameObject.Create("Apple", isItem: true);
            ObjectRegistry.AddObject(apple);
            Assert.True(apple.MoveTo(holder));
            var coin = GameObject.Create("Coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "inventory", immediate: true));

            var text = string.Join("\n", holder.PeekMessages());
            Assert.Contains("Apple", text);
            Assert.Contains("Coin", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void DropCommand_DropAll_MovesContentsToLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("b19drop");
        try
        {
            var holder = GameObject.Create("holder", isPc: true);
            ObjectRegistry.AddObject(holder);
            Assert.True(holder.MoveTo(node));
            var rock = GameObject.Create("rock", isItem: true);
            ObjectRegistry.AddObject(rock);
            Assert.True(rock.MoveTo(holder));
            var gem = GameObject.Create("gem", isItem: true);
            ObjectRegistry.AddObject(gem);
            Assert.True(gem.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "drop all", immediate: true));

            Assert.Contains(rock.Id, node.ContentsSnapshot);
            Assert.Contains(gem.Id, node.ContentsSnapshot);
            Assert.DoesNotContain(rock.Id, holder.ContentsSnapshot);
            Assert.DoesNotContain(gem.Id, holder.ContentsSnapshot);
            var text = string.Join("\n", holder.PeekMessages());
            Assert.Contains("You dropped: rock", text);
            Assert.Contains("You dropped: gem", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GetCommand_GetAll_PicksUpLocationContents()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("b19get");
        try
        {
            var holder = GameObject.Create("holder", isPc: true);
            ObjectRegistry.AddObject(holder);
            Assert.True(holder.MoveTo(node));
            var sword = GameObject.Create("sword", isItem: true);
            ObjectRegistry.AddObject(sword);
            Assert.True(sword.MoveTo(node));
            var shield = GameObject.Create("shield", isItem: true);
            ObjectRegistry.AddObject(shield);
            Assert.True(shield.MoveTo(node));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "get all", immediate: true));

            Assert.Contains(sword.Id, holder.ContentsSnapshot);
            Assert.Contains(shield.Id, holder.ContentsSnapshot);
            var text = string.Join("\n", holder.PeekMessages());
            Assert.Contains("You picked up: sword", text);
            Assert.Contains("You picked up: shield", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GiveCommand_GiveSingleItem_MovesToTarget()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("b19give");
        try
        {
            var giver = GameObject.Create("giver", isPc: true);
            ObjectRegistry.AddObject(giver);
            Assert.True(giver.MoveTo(node));
            var taker = GameObject.Create("taker", isNpc: true);
            ObjectRegistry.AddObject(taker);
            Assert.True(taker.MoveTo(node));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(giver));
            giver.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(giver, "give coin to taker", immediate: true));

            Assert.Contains(coin.Id, taker.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, giver.ContentsSnapshot);
            Assert.Contains("You give coin to taker.", string.Join("\n", giver.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GiveCommand_GiveAll_MovesEntireInventory()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("b19giveall");
        try
        {
            var giver = GameObject.Create("giver", isPc: true);
            ObjectRegistry.AddObject(giver);
            Assert.True(giver.MoveTo(node));
            var taker = GameObject.Create("taker", isNpc: true);
            ObjectRegistry.AddObject(taker);
            Assert.True(taker.MoveTo(node));
            var one = GameObject.Create("one", isItem: true);
            ObjectRegistry.AddObject(one);
            Assert.True(one.MoveTo(giver));
            var two = GameObject.Create("two", isItem: true);
            ObjectRegistry.AddObject(two);
            Assert.True(two.MoveTo(giver));
            giver.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(giver, "give all to taker", immediate: true));

            Assert.Contains(one.Id, taker.ContentsSnapshot);
            Assert.Contains(two.Id, taker.ContentsSnapshot);
            Assert.Empty(giver.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void NofollowCommand_ToggleOn_ClearsFollowers()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node) = EnterRoom("b19nofollow");
        try
        {
            var leader = GameObject.Create("leader", isPc: true);
            ObjectRegistry.AddObject(leader);
            Assert.True(leader.MoveTo(node));
            var first = GameObject.Create("first", isPc: true);
            ObjectRegistry.AddObject(first);
            Assert.True(first.MoveTo(node));
            var second = GameObject.Create("second", isPc: true);
            ObjectRegistry.AddObject(second);
            Assert.True(second.MoveTo(node));
            first.Following = leader.Id;
            leader.AddFollower(first.Id);
            second.Following = leader.Id;
            leader.AddFollower(second.Id);
            Assert.Equal(2, leader.FollowersSnapshot.Count);
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "nofollow", immediate: true));

            Assert.True(leader.NoFollow);
            Assert.Empty(leader.FollowersSnapshot);
            Assert.Null(first.Following);
            Assert.Null(second.Following);
            Assert.Contains("You will no longer allow others to follow you.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
