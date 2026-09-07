using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Follow-break notices are gated per direction (exit.py:100-103): the
// leader's notice needs the follower's view of the leader, and vice versa.
// Both exit paths (the hidden "exit" command and room exit objects) break
// following. NOTE: the room exit-object class is also named ExitCommand
// (Atheriz.Core.Objects.ExitCommand) — always qualify which one is meant.
// (C-7)
[Collection("Ported")]
public class ExitDirectionTests
{
    private static (NodeHandler nh, Node n1, Node n2, GameObject leader, GameObject follower) Setup()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var n1 = new Node(new Coord("exitdir", 0, 0, 0));
        var n2 = new Node(new Coord("exitdir", 1, 0, 0));
        nh.AddNode(n1);
        nh.AddNode(n2);
        var leader = GameObject.Create("Leader", isPc: true);
        ObjectRegistry.AddObject(leader);
        leader.IsConnected = true;
        Assert.True(leader.MoveTo(n1));
        var follower = GameObject.Create("Follower", isPc: true);
        ObjectRegistry.AddObject(follower);
        follower.IsConnected = true;
        Assert.True(follower.MoveTo(n1));
        // The leader cannot view the follower: deny-all view lock.
        follower.AddLock("view", _ => false);
        new FollowCommand().Run(follower, new FollowCommand().Parser!.ParseArgs(new[] { "Leader" }));
        Assert.Equal(leader.Id, follower.Following);
        leader.ClearMessages();
        follower.ClearMessages();
        return (nh, n1, n2, leader, follower);
    }

    [Fact]
    public void LoggedInExit_NoticesFollowViewDirection()
    {
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (nh, _, n2, leader, follower) = Setup();
            var exit = new LoggedInExitCommand { Destination = n2.Coord };
            exit.Run(follower, null);
            Assert.Null(follower.Following);
            Assert.DoesNotContain(leader.PeekMessages(), m => m.Contains("no longer following"));
            Assert.Contains(follower.PeekMessages(), m => m.Contains("no longer following"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RoomExitObject_BreaksFollowing()
    {
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (nh, _, n2, leader, follower) = Setup();
            var exitObj = new Atheriz.Core.Objects.ExitCommand { Destination = n2.Coord };
            exitObj.Run(follower, null);
            Assert.Null(follower.Following);
            Assert.DoesNotContain(leader.PeekMessages(), m => m.Contains("no longer following"));
            Assert.Contains(follower.PeekMessages(), m => m.Contains("no longer following"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
