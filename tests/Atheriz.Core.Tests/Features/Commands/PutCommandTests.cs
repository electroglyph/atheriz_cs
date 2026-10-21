// Put announce exclude list: one hoisted instance shared by both put loops
// still excludes the actor from every per-object announce.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class PutCommandTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (NodeHandler Nh, Node Node, GameObject Builder) EnterBuilderInNode(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        var builder = GameObject.Create("put_builder", isPc: true, privilege: Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        Assert.True(builder.MoveTo(node));
        builder.ClearMessages();
        return (nh, node, builder);
    }

    [Fact]
    public void PutCommand_PutAllInContainer_MovesAllItems()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node, holder) = EnterBuilderInNode("putallnode");
        try
        {
            var chest = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(chest);
            Assert.True(chest.MoveTo(node));
            var one = GameObject.Create("one", isItem: true);
            ObjectRegistry.AddObject(one);
            Assert.True(one.MoveTo(holder));
            var two = GameObject.Create("two", isItem: true);
            ObjectRegistry.AddObject(two);
            Assert.True(two.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "put all in chest", immediate: true));

            Assert.Contains(one.Id, chest.ContentsSnapshot);
            Assert.Contains(two.Id, chest.ContentsSnapshot);
            Assert.Empty(holder.ContentsSnapshot);
            var text = string.Join("\n", holder.PeekMessages());
            Assert.Contains("You put one in chest.", text);
            Assert.Contains("You put two in chest.", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_PutSingleInContainer_MovesItem()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node, holder) = EnterBuilderInNode("putsinglenode");
        try
        {
            var chest = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(chest);
            Assert.True(chest.MoveTo(node));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            holder.ClearMessages();

            var cmd = new PutCommand();
            cmd.Run(holder, cmd.Parser!.ParseArgs(["coin", "in", "chest"]));

            Assert.Contains(coin.Id, chest.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, holder.ContentsSnapshot);
            Assert.Contains($"You put {coin.Name} in {chest.Name}.", holder.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_TwoContainersInInventory_RefusesAmbiguousDestination()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, _, holder) = EnterBuilderInNode("putambig1");
        try
        {
            var first = GameObject.Create("chest", isContainer: true);
            var second = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(first);
            ObjectRegistry.AddObject(second);
            Assert.True(first.MoveTo(holder));
            Assert.True(second.MoveTo(holder));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "put coin in chest", immediate: true));

            Assert.Contains(coin.Id, holder.ContentsSnapshot);
            Assert.Empty(first.ContentsSnapshot);
            Assert.Empty(second.ContentsSnapshot);
            var text = string.Join("\n", holder.PeekMessages());
            Assert.Contains("Multiple matches found for 'chest'.", text);
            Assert.Contains($"#{first.Id} chest", text);
            Assert.Contains($"#{second.Id} chest", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_TwoContainersInRoom_RefusesAmbiguousDestination()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node, holder) = EnterBuilderInNode("putambig2");
        try
        {
            var first = GameObject.Create("chest", isContainer: true);
            var second = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(first);
            ObjectRegistry.AddObject(second);
            Assert.True(first.MoveTo(node));
            Assert.True(second.MoveTo(node));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "put coin in chest", immediate: true));

            Assert.Contains(coin.Id, holder.ContentsSnapshot);
            Assert.Empty(first.ContentsSnapshot);
            Assert.Empty(second.ContentsSnapshot);
            Assert.Contains("Multiple matches found for 'chest'.", string.Join("\n", holder.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_ExplicitIdPicksOneOfTwoContainers()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, _, holder) = EnterBuilderInNode("putambig3");
        try
        {
            var first = GameObject.Create("chest", isContainer: true);
            var second = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(first);
            ObjectRegistry.AddObject(second);
            Assert.True(first.MoveTo(holder));
            Assert.True(second.MoveTo(holder));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, $"put coin in #{second.Id}", immediate: true));

            Assert.Contains(coin.Id, second.ContentsSnapshot);
            Assert.Empty(first.ContentsSnapshot);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_AllPrefix_RefusesAmbiguousDestination()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, _, holder) = EnterBuilderInNode("putambig4");
        try
        {
            var first = GameObject.Create("chest", isContainer: true);
            var second = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(first);
            ObjectRegistry.AddObject(second);
            Assert.True(first.MoveTo(holder));
            Assert.True(second.MoveTo(holder));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "put coin in all chest", immediate: true));

            Assert.Contains(coin.Id, holder.ContentsSnapshot);
            Assert.Empty(first.ContentsSnapshot);
            Assert.Empty(second.ContentsSnapshot);
            Assert.Contains("Multiple matches found for 'all chest'.", string.Join("\n", holder.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
