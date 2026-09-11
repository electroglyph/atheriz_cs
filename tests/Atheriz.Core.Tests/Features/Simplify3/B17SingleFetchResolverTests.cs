// Pins for simplify3 §4 batch B17: single-id fetch + resolver-lambda sweep to
// ObjectRegistry.GetSingle (CommandHelpers, BaseChannelCommand, ExamFormatter,
// GroupCommand, ContainmentCommands).
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B17SingleFetchResolverTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void SearchWithFallback_HashIdFound_ReturnsSingleObject()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        var target = GameObject.Create("target");
        ObjectRegistry.AddObject(target);

        var found = CommandHelpers.SearchWithFallback(caller, $"#{target.Id}");
        Assert.Single(found);
        Assert.Same(target, found[0]);
    }

    [Fact]
    public void SearchWithFallback_HashIdMissing_ReturnsEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);

        Assert.Empty(CommandHelpers.SearchWithFallback(caller, "#299999"));
    }

    [Fact]
    public void SearchWithFallback_HashIdMalformed_ReturnsEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);

        Assert.Empty(CommandHelpers.SearchWithFallback(caller, "#x"));
    }

    [Fact]
    public void LocalVerbSets_LocationContentsWithSet_YieldsSet()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("b17loc", 0, 0, 0));
            nh.AddNode(node);
            var prop = GameObject.Create("prop");
            ObjectRegistry.AddObject(prop);
            var propSet = new CmdSet();
            prop.ExternalCmdSet = propSet;
            Assert.True(prop.MoveTo(node));
            var go = GameObject.Create("visitor");
            ObjectRegistry.AddObject(go);
            Assert.True(go.MoveTo(node));

            Assert.Contains(propSet, CommandHelpers.LocalVerbSets(go));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void LocalVerbSets_InventoryContentsWithSet_YieldsSet()
    {
        using var env = GlobalTestEnv.Enter();
        var go = GameObject.Create("holder");
        ObjectRegistry.AddObject(go);
        var tool = GameObject.Create("tool");
        ObjectRegistry.AddObject(tool);
        var toolSet = new CmdSet();
        tool.ExternalCmdSet = toolSet;
        Assert.True(tool.MoveTo(go));

        Assert.Contains(toolSet, CommandHelpers.LocalVerbSets(go));
    }

    [Fact]
    public void LocalVerbSets_StaleId_SkipsWithoutThrow()
    {
        using var env = GlobalTestEnv.Enter();
        var go = GameObject.Create("holder");
        ObjectRegistry.AddObject(go);
        var ghost = GameObject.Create("ghost");
        ObjectRegistry.AddObject(ghost);
        var ghostSet = new CmdSet();
        ghost.ExternalCmdSet = ghostSet;
        Assert.True(ghost.MoveTo(go));
        ObjectRegistry.RemoveObject(ghost);

        var ex = Record.Exception(() => CommandHelpers.LocalVerbSets(go).ToList());
        Assert.Null(ex);
        Assert.DoesNotContain(ghostSet, CommandHelpers.LocalVerbSets(go));
    }

    [Fact]
    public void SearchIn_ContainerQuery_FoundAndMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var bag = GameObject.Create("bag", isContainer: true);
        ObjectRegistry.AddObject(bag);
        var sword = GameObject.Create("sword", isItem: true);
        ObjectRegistry.AddObject(sword);
        Assert.True(sword.MoveTo(bag));

        var found = CommandHelpers.SearchIn(bag, "sword");
        Assert.Single(found);
        Assert.Same(sword, found[0]);
        Assert.Empty(CommandHelpers.SearchIn(bag, "nope"));
    }

    [Fact]
    public void SearchWithFallback_LocationFallback_FindsContainerItem()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("b17fb", 0, 0, 0));
            nh.AddNode(node);
            var caller = GameObject.Create("seeker");
            ObjectRegistry.AddObject(caller);
            Assert.True(caller.MoveTo(node));
            var chest = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(chest);
            Assert.True(chest.MoveTo(node));
            var gem = GameObject.Create("gem", isItem: true);
            ObjectRegistry.AddObject(gem);
            Assert.True(gem.MoveTo(chest));

            var found = CommandHelpers.SearchWithFallback(caller, "gem");
            Assert.Contains(gem, found);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void TryGetChannel_LiveId_ResolvesAndCaches()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("b17chan");
        var cmd = new BaseChannelCommand { id = chan.Id };

        Assert.True(cmd.TryGetChannel(out var got));
        Assert.Same(chan, got);
    }

    [Fact]
    public void TryGetChannel_MissingDeletedWrongType_ReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        var missing = new BaseChannelCommand { id = 299998 };
        Assert.False(missing.TryGetChannel(out _));

        var notChan = GameObject.Create("notachan");
        ObjectRegistry.AddObject(notChan);
        var wrongType = new BaseChannelCommand { id = notChan.Id };
        Assert.False(wrongType.TryGetChannel(out _));

        var chan = Channel.Create("b17gone");
        var gone = new BaseChannelCommand { id = chan.Id };
        chan.IsDeleted = true;
        Assert.False(gone.TryGetChannel(out _));
    }

    [Fact]
    public void ExamFormatter_FollowersHint_FoundRendersNamedId()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        ObjectRegistry.AddObject(alice);

        var rendered = ExamFormatter.FormatValue(new List<int> { alice.Id }, "followers") as string;
        Assert.Equal("{#" + alice.Id + " (" + alice.Name + ")}", rendered);
    }

    [Fact]
    public void ExamFormatter_FollowersHint_MissingRendersBareId()
    {
        using var env = GlobalTestEnv.Enter();
        var rendered = ExamFormatter.FormatValue(new List<int> { 299997 }, "followers") as string;
        Assert.Equal("{#299997}", rendered);
    }

    [Fact]
    public void ExamFormatter_CreatedByFallback_FoundAndMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        ObjectRegistry.AddObject(alice);

        var found = ExamFormatter.FormatValue(alice.Id, "created_by") as string;
        Assert.Equal($"{alice.Id} ({alice.Name})", found);
        var missing = ExamFormatter.FormatValue(299996, "created_by") as string;
        Assert.Equal("299996", missing);
    }

    [Fact]
    public void GroupCommand_AddAndList_ResolvesThroughSingleFetch()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("b17group", 0, 0, 0));
            nh.AddNode(node);
            var leader = GameObject.Create("Leader", isNpc: true);
            ObjectRegistry.AddObject(leader);
            Assert.True(leader.MoveTo(node));
            var follower = GameObject.Create("Follower", isNpc: true);
            ObjectRegistry.AddObject(follower);
            Assert.True(follower.MoveTo(node));
            leader.AddFollower(follower.Id);
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group add Follower", immediate: true));
            Assert.NotNull(leader.GroupChannel);
            var channel = ObjectRegistry.GetSingle(leader.GroupChannel!.Value) as Channel;
            Assert.NotNull(channel);
            Assert.Contains(follower.Id, channel!.Listeners);

            leader.ClearMessages();
            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group list", immediate: true));
            var text = string.Join("\n", leader.PeekMessages());
            Assert.Contains("Group members:", text);
            Assert.Contains("Follower", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_KickUnknownTarget_ReportsCouldNotFind()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("b17gkick", 0, 0, 0));
            nh.AddNode(node);
            var leader = GameObject.Create("Leader", isNpc: true);
            ObjectRegistry.AddObject(leader);
            Assert.True(leader.MoveTo(node));
            var follower = GameObject.Create("Follower", isNpc: true);
            ObjectRegistry.AddObject(follower);
            Assert.True(follower.MoveTo(node));
            leader.AddFollower(follower.Id);

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group add Follower", immediate: true));
            leader.ClearMessages();
            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group kick Ghost", immediate: true));
            Assert.Contains("Could not find 'Ghost'.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_PutAll_SkipsStaleIds()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("b17put", 0, 0, 0));
            nh.AddNode(node);
            var holder = GameObject.Create("holder");
            ObjectRegistry.AddObject(holder);
            Assert.True(holder.MoveTo(node));
            var chest = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(chest);
            Assert.True(chest.MoveTo(node));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            var stale = GameObject.Create("stale", isItem: true);
            ObjectRegistry.AddObject(stale);
            Assert.True(stale.MoveTo(holder));
            ObjectRegistry.RemoveObject(stale);
            holder.ClearMessages();

            var ex = Record.Exception(() => RunJob(CommandDispatcher.DispatchLoggedIn(holder, "put all in chest", immediate: true)));
            Assert.Null(ex);
            Assert.Contains(coin.Id, chest.ContentsSnapshot);
            Assert.Contains($"You put {coin.Name} in {chest.Name}.", holder.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Resolver_MethodGroup_MatchesSingleFetch()
    {
        using var env = GlobalTestEnv.Enter();
        var target = GameObject.Create("target");
        ObjectRegistry.AddObject(target);

        Func<int, GameObject?> resolver = ObjectRegistry.GetSingle;
        Assert.Same(target, resolver(target.Id));
        Assert.Null(resolver(299995));
        Assert.Same(target, ObjectRegistry.GetSingle(target.Id));
    }
}
