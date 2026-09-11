// Pins for simplify3 §4 group-channel fetch helper: every subcommand
// reports the byte-identical missing-channel error, and leave clears the
// stale pointer before reporting.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B19GroupChannelTests
{
    private const int MissingChannelId = 299991;

    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (Node Node, GameObject Leader) EnterLeader(string area, string leaderName = "leader")
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        var leader = GameObject.Create(leaderName, isNpc: true);
        ObjectRegistry.AddObject(leader);
        Assert.True(leader.MoveTo(node));
        return (node, leader);
    }

    [Fact]
    public void GroupCommand_ListWithMissingChannel_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader) = EnterLeader("b19glist");
        try
        {
            leader.GroupChannel = MissingChannelId;
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group list", immediate: true));

            Assert.Contains("Error: Group channel not found.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_KickWithMissingChannel_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader) = EnterLeader("b19gkick");
        try
        {
            leader.GroupChannel = MissingChannelId;
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group kick ghost", immediate: true));

            Assert.Contains("Error: Group channel not found.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_LeaveWithMissingChannel_ClearsStateAndReports()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader) = EnterLeader("b19gleave");
        try
        {
            leader.GroupChannel = MissingChannelId;
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group leave", immediate: true));

            Assert.Contains("Error: Group channel not found.", string.Join("\n", leader.PeekMessages()));
            Assert.Null(leader.GroupChannel);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_AddWithMissingChannel_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var (node, leader) = EnterLeader("b19gadd");
        try
        {
            var follower = GameObject.Create("follower", isNpc: true);
            ObjectRegistry.AddObject(follower);
            Assert.True(follower.MoveTo(node));
            leader.AddFollower(follower.Id);
            leader.GroupChannel = MissingChannelId;
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group add follower", immediate: true));

            Assert.Contains("Error: Group channel not found.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_MessageWithMissingChannel_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader) = EnterLeader("b19gmsg");
        try
        {
            leader.GroupChannel = MissingChannelId;
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group hello world", immediate: true));

            Assert.Contains("Error: Group channel not found.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_ListWithWrongTypeChannel_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader) = EnterLeader("b19gwrong");
        try
        {
            var notChannel = GameObject.Create("notachannel");
            ObjectRegistry.AddObject(notChannel);
            leader.GroupChannel = notChannel.Id;
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group list", immediate: true));

            Assert.Contains("Error: Group channel not found.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupCommand_ListWithNoGroup_ReportsNotInGroup()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader) = EnterLeader("b19gnone");
        try
        {
            leader.GroupChannel = null;
            leader.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(leader, "group list", immediate: true));

            Assert.Contains("You are not in a group.", string.Join("\n", leader.PeekMessages()));
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
