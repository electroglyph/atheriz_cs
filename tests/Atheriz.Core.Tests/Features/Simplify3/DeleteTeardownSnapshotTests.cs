// Pins for iterating teardown snapshots directly (GameObject.Delete.cs):
// deleting an object clears its followers, channel memberships, and session.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class DeleteTeardownSnapshotTests
{
    [Fact]
    public void TeardownDeleted_FollowerLink_ClearedBothWays()
    {
        using var env = GlobalTestEnv.Enter();
        var owner = GameObject.Create("owner", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(owner);
        var leader = GameObject.Create("leader", isPc: true);
        ObjectRegistry.AddObject(leader);
        var doomed = GameObject.Create("doomed", isPc: true);
        ObjectRegistry.AddObject(doomed);
        doomed.Following = leader.Id;
        leader.AddFollower(doomed.Id);
        Assert.Contains(doomed.Id, leader.FollowersSnapshot);

        Assert.NotNull(doomed.Delete(owner, recursive: false));

        Assert.Null(doomed.Following);
        Assert.DoesNotContain(doomed.Id, leader.FollowersSnapshot);
    }

    [Fact]
    public void TeardownDeleted_FollowersOfDeleted_Released()
    {
        using var env = GlobalTestEnv.Enter();
        var owner = GameObject.Create("owner", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(owner);
        var leader = GameObject.Create("leader", isPc: true);
        ObjectRegistry.AddObject(leader);
        var follower = GameObject.Create("follower", isPc: true);
        ObjectRegistry.AddObject(follower);
        follower.Following = leader.Id;
        leader.AddFollower(follower.Id);

        Assert.NotNull(leader.Delete(owner, recursive: false));

        Assert.Null(follower.Following);
    }

    [Fact]
    public void TeardownDeleted_ChannelMembership_Removed()
    {
        using var env = GlobalTestEnv.Enter();
        var owner = GameObject.Create("owner", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(owner);
        var doomed = GameObject.Create("doomed", isPc: true);
        ObjectRegistry.AddObject(doomed);
        var channel = Channel.Create("doomchan");
        doomed.Subscribe(channel);
        Assert.Contains(channel.Id, doomed.ChannelsSnapshot);

        Assert.NotNull(doomed.Delete(owner, recursive: false));

        Assert.DoesNotContain(channel.Id, doomed.ChannelsSnapshot);
    }

    [Fact]
    public void TeardownDeleted_SessionConnection_Closed()
    {
        using var env = GlobalTestEnv.Enter();
        var owner = GameObject.Create("owner", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(owner);
        var doomed = GameObject.Create("doomed", isPc: true);
        ObjectRegistry.AddObject(doomed);
        var conn = new TestConnection("doomed-conn");
        doomed.Session = new Session(conn);

        Assert.NotNull(doomed.Delete(owner, recursive: false));

        Assert.True(conn.Closed);
    }
}
