using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// The follow pre-move push / post-move pop pairing must stay balanced across
// vetoed and forced moves, so later moves pair correctly.
[Collection("Ported")]
public class FollowStackVetoTests
{
    private static GameObject MakePc(string name, Node at)
    {
        var o = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(o);
        o.IsConnected = true;
        o.Location = new LocationRef.CoordLocation(at.Coord);
        at.AddObject(o);
        o.ClearMessages();
        return o;
    }

    private static FollowScript InstallFollowScript(GameObject follower, GameObject leader)
    {
        follower.Following = leader.Id;
        leader.AddFollower(follower.Id);
        if (!leader.GetScriptsByType("FollowScript").Any())
        {
            var s = new FollowScript
            {
                Id = IdGenerator.GetUniqueId(),
                Name = $"FollowScript_for_{follower.Id}",
                IsModified = true,
            };
            ObjectRegistry.AddObject(s);
            leader.AddScript(s);
        }
        return (FollowScript)leader.GetScriptsByType("FollowScript").First();
    }

    [Fact]
    public void VetoedMove_DoesNotLeakFollowStack()
    {
        // A vetoed move pushed a pre-move entry with no post-move pop; the
        // pairing must be cancelled so later moves pair correctly.
        using var env = GlobalTestEnv.Enter();
        var n1 = new Node(new Coord("FollowVeto", 0, 0, 0));
        ObjectRegistry.AddObject(n1);
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        var script = InstallFollowScript(follower, leader);

        Assert.False(leader.MoveTo(leader));
        Assert.Null(script.OldLoc);
    }

    [Fact]
    public void ForceMove_DoesNotConsumeFollowStack()
    {
        // A force move skips pre-move (no push), so its post-move must leave
        // an empty stack alone instead of eating a partner entry.
        using var env = GlobalTestEnv.Enter();
        var n1 = new Node(new Coord("FollowForce", 0, 0, 0));
        var n2 = new Node(new Coord("FollowForce", 0, 1, 0));
        ObjectRegistry.AddObject(n1);
        ObjectRegistry.AddObject(n2);
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        var script = InstallFollowScript(follower, leader);

        Assert.True(leader.MoveTo(n2, force: true));
        Assert.Null(script.OldLoc);
        Assert.Equal(leader.Id, follower.Following);
    }
}
