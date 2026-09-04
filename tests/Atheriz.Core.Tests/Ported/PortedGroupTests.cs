// Port of atheriz/tests/test_group.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedGroupTests
{
    private class MockArgs
    {
        public List<string> Args { get; }
        public MockArgs(params string[] a) => Args = a.ToList();
    }

    private static (GameObject leader, GameObject follower, GameObject target, Node node) MakeTestObjects()
    {
        var nh = NodeHandler.GetCurrent() ?? new NodeHandler();
        var node = new Node(new Coord("group_test", 0, 0, 0));
        nh.AddNode(node);
        var leader = GameObject.Create("Leader", isNpc: true); ObjectRegistry.AddObject(leader);
        leader.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(leader);
        var follower = GameObject.Create("Follower", isNpc: true); ObjectRegistry.AddObject(follower);
        follower.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(follower);
        var target = GameObject.Create("Target", isNpc: true); ObjectRegistry.AddObject(target);
        target.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(target);
        leader.ClearMessages(); follower.ClearMessages(); target.ClearMessages();
        var fField = typeof(GameObject).GetField("_followers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var set = fField.GetValue(leader) as HashSet<int>;
        set!.Add(follower.Id); set.Add(target.Id);
        leader.IsModified = true;
        return (leader, follower, target, node);
    }

    // Helper to run GroupCommand with args list
    private static void RunGroup(GameObject caller, params string[] args)
    {
        var cmd = new GroupCommand();
        // Build ParsedArgs via parser: expects single "args" REMAINDER list
        var pa = cmd.Parser!.ParseArgs(args);
        cmd.Run(caller, pa);
    }

    [Fact]
    public void GroupAdd()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, _, _) = MakeTestObjects();
        RunGroup(leader, "add", "Follower");
        // Find group channel via reflection
        int? gc = GetGroupChannel(leader);
        Assert.NotNull(gc);
        var ch = ObjectRegistry.Get(gc!.Value).FirstOrDefault() as Channel;
        Assert.NotNull(ch);
        Assert.Contains(follower.Id, ch!.Listeners);
        Assert.Contains(leader.Id, ch.Listeners);
        Assert.Equal(leader.Id, ch.CreatedBy);
        Assert.Equal(gc.Value, GetGroupChannel(follower));
    }

    [Fact]
    public void GroupAddNotFollowing()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, target, _) = MakeTestObjects();
        // Remove target from followers
        var f = typeof(GameObject).GetField("_followers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        (f.GetValue(leader) as HashSet<int>)!.Remove(target.Id);
        RunGroup(leader, "add", "Target");
        Assert.Null(GetGroupChannel(leader));
        Assert.Contains(leader.PeekMessages(), m => m.ToLowerInvariant().Contains("not following"));
    }

    [Fact]
    public void GroupKick()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, _, _) = MakeTestObjects();
        RunGroup(leader, "add", "Follower");
        var gc = GetGroupChannel(leader)!;
        var ch = ObjectRegistry.Get(gc.Value).FirstOrDefault() as Channel;
        Assert.Contains(follower.Id, ch!.Listeners);
        RunGroup(leader, "kick", "Follower");
        Assert.DoesNotContain(follower.Id, ch.Listeners);
        Assert.Null(GetGroupChannel(follower));
    }

    [Fact]
    public void GroupKickNotLeader()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, _, _) = MakeTestObjects();
        RunGroup(leader, "add", "Follower");
        RunGroup(follower, "kick", "Leader");
        Assert.Contains(follower.PeekMessages(), m => m.ToLowerInvariant().Contains("not the leader"));
    }

    [Fact]
    public void GroupLeave()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, _, _) = MakeTestObjects();
        RunGroup(leader, "add", "Follower");
        var gc = GetGroupChannel(leader)!;
        RunGroup(follower, "leave");
        var ch = ObjectRegistry.Get(gc.Value).FirstOrDefault() as Channel;
        Assert.DoesNotContain(follower.Id, ch!.Listeners);
        Assert.Null(GetGroupChannel(follower));
    }

    [Fact]
    public void GroupLeaderLeavePromotesRemainingMember()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, _, _) = MakeTestObjects();
        RunGroup(leader, "add", "Follower");
        var gc = GetGroupChannel(leader)!.Value;
        var ch = ObjectRegistry.Get(gc).FirstOrDefault() as Channel;
        RunGroup(leader, "leave");
        Assert.Null(GetGroupChannel(leader));
        Assert.Equal(gc, GetGroupChannel(follower));
        Assert.Contains(follower.Id, ch!.Listeners);
        Assert.Equal(follower.Id, ch.CreatedBy);
    }

    [Fact]
    public void GroupList()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, target, _) = MakeTestObjects();
        RunGroup(leader, "add", "Follower");
        RunGroup(leader, "add", "Target");
        leader.ClearMessages();
        RunGroup(leader, "list");
        var msg = string.Join(" ", leader.PeekMessages());
        Assert.Contains("Leader", msg);
        Assert.Contains("Follower", msg);
        Assert.Contains("Target", msg);
    }

    [Fact]
    public void GroupMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, follower, _, _) = MakeTestObjects();
        RunGroup(leader, "add", "Follower");
        leader.ClearMessages(); follower.ClearMessages();
        RunGroup(leader, "Hello", "team!");
        var lm = string.Join(" ", leader.PeekMessages());
        var fm = string.Join(" ", follower.PeekMessages());
        Assert.Contains("Hello team!", lm);
        Assert.Contains("Hello team!", fm);
    }

    [Fact]
    public void GroupAddSelfFails()
    {
        using var env = GlobalTestEnv.Enter();
        var (leader, _, _, _) = MakeTestObjects();
        RunGroup(leader, "add", "Leader");
        Assert.Contains(leader.PeekMessages(), m => m.ToLowerInvariant().Contains("yourself") || m.ToLowerInvariant().Contains("not following"));
    }

    private static int? GetGroupChannel(GameObject go)
    {
        try { return (int?)((dynamic)go).GroupChannel; } catch { }
        var f = go.GetType().GetField("_groupChannel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) return f.GetValue(go) as int?;
        var dtoField = typeof(GameObject).GetField("_extra", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return null;
    }
}
