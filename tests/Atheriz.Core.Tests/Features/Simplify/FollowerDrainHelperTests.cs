// Pins for the shared follower-script drain (FollowHelper.RemoveScriptsIfDrained):
// unfollow and nofollow remove the drained FollowScript eagerly, keep it
// while followers remain, and take no locks of their own (ordering preserved).
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class FollowerDrainHelperTests
{
    private static (Node room, GameObject leader, GameObject follower) Setup(string area)
    {
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var room = new Node(new Coord(area, 0, 0, 0));
        grid.AddNode(room);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(room);
        GameObject MakePc(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            o.Location = new LocationRef.CoordLocation(room.Coord);
            room.AddObject(o);
            o.ClearMessages();
            return o;
        }
        return (room, MakePc("Leader"), MakePc("Follower"));
    }

    [Fact]
    public void Unfollow_DrainedLeader_LosesFollowScript()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("drain1");
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        Assert.Single(leader.GetScriptsByType("FollowScript"));
        new UnfollowCommand().Run(follower, null);
        Assert.Equal("You stop following.", follower.PeekMessages().Last());
        Assert.Empty(leader.GetScriptsByType("FollowScript"));
    }

    [Fact]
    public void Nofollow_DrainedSelf_LosesFollowScript()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, leader, follower) = Setup("drain2");
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        Assert.Single(leader.GetScriptsByType("FollowScript"));
        new NofollowCommand().Run(leader, null);
        Assert.Empty(leader.GetScriptsByType("FollowScript"));
        Assert.Null(follower.Following);
    }

    [Fact]
    public void Nofollow_WithRemainingBuilderFollower_KeepsFollowScript()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, leader, follower) = Setup("drain3");
        var builder = GameObject.Create("Arch", isPc: true, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        // Colocated + connected like the established nofollow fixtures:
        // search runs from the caller's location under the view lock.
        builder.IsConnected = true;
        builder.Location = new LocationRef.CoordLocation(room.Coord);
        room.AddObject(builder);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        new FollowCommand().Run(builder, new FollowCommand().Parser!.ParseArgs(["Leader"]));
        Assert.Equal(2, leader.FollowersSnapshot.Count);
        new NofollowCommand().Run(leader, null);
        // The builder follower is kept, so the set never drains and the script stays.
        Assert.Contains(builder.Id, leader.FollowersSnapshot);
        Assert.Single(leader.GetScriptsByType("FollowScript"));
    }
}
