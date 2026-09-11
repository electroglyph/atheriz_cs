// Unfollowed-leader notice: unfollow and exit share
// FollowHelper.NotifyUnfollowedLeader; the exit follower-side message stays
// per-site and the view gate stays in the helper.
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class UnfollowNoticeTests
{
    private static (Node Room, GameObject Leader, GameObject Follower) Setup(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(room);
        GameObject MakePc(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            Assert.True(o.MoveTo(room));
            o.ClearMessages();
            return o;
        }
        return (room, MakePc(area + "_leader"), MakePc(area + "_follower"));
    }

    private static void StartFollowing(GameObject follower, GameObject leader)
    {
        follower.ClearMessages();
        leader.ClearMessages();
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs([leader.Name]));
        Assert.Equal(leader.Id, follower.Following);
        follower.ClearMessages();
        leader.ClearMessages();
    }

    [Fact]
    public void UnfollowCommand_NotifiesLeader_WithByteIdenticalNotice()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("b20aunf");
        try
        {
            StartFollowing(follower, leader);
            new UnfollowCommand().Run(follower, null);
            var expected = $"{follower.GetDisplayName(leader)} is no longer following you.";
            Assert.Contains(expected, leader.PeekMessages());
            Assert.Contains("You stop following.", follower.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void ExitClearFollowing_NotifiesLeader_WithSameNotice()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("b20aexit");
        try
        {
            StartFollowing(follower, leader);
            LoggedInExitCommand.ClearFollowing(follower);
            var expected = $"{follower.GetDisplayName(leader)} is no longer following you.";
            Assert.Contains(expected, leader.PeekMessages());
            Assert.Contains($"You are no longer following {leader.GetDisplayName(follower)}.", follower.PeekMessages());
            Assert.Null(follower.Following);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void NotifyUnfollowedLeader_ViewDenied_SendsNothing()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("b20agate");
        try
        {
            StartFollowing(follower, leader);
            follower.AddLock("view", _ => false);
            leader.ClearMessages();
            FollowHelper.NotifyUnfollowedLeader(leader, follower);
            Assert.Empty(leader.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void NotifyUnfollowedLeader_ViewAllowed_SendsByteIdenticalNotice()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("b20adirect");
        try
        {
            leader.ClearMessages();
            FollowHelper.NotifyUnfollowedLeader(leader, follower);
            Assert.Contains($"{follower.GetDisplayName(leader)} is no longer following you.", leader.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
