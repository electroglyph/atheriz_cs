using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Objects.VerbConjugation;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// Regression pins for the audit4 object-layer fixes (O-1..O-6).
[Collection("Ported")]
public class Audit4ObjectFixTests
{
    private sealed class ThrowOnPostMove : GameObject
    {
        public override void AtPostMove(GameObject? destination, string? toExit = null)
            => throw new InvalidOperationException("boom in AtPostMove");
    }

    private static (Node n1, Node n2) MakeNodes(string area)
    {
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        ObjectRegistry.AddObject(n1);
        ObjectRegistry.AddObject(n2);
        return (n1, n2);
    }

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

    private static void InstallFollowScript(GameObject follower, GameObject leader)
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
    }

    [Fact]
    public void NodeDelete_WithThrowingMoveHook_CompletesWithoutThrowing()
    {
        // O-1: a content whose move hooks throw must not tear Node.Delete —
        // the rescue move fails closed and the content is deleted instead.
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeNodes("Audit4O1");
        var caller = GameObject.Create("Caller", isPc: true);
        ObjectRegistry.AddObject(caller);
        var content = new ThrowOnPostMove { Name = "Fragile" };
        ObjectRegistry.AddObject(content);
        content.Home = new LocationRef.CoordLocation(n2.Coord);
        n1.AddObject(content);

        var ex = Record.Exception(() => n1.Delete(caller, recursive: false));
        Assert.Null(ex);
        Assert.True(content.IsDeleted);
        Assert.True(n1.IsDeleted);
    }

    [Fact]
    public void Unpuppet_AfterTargetDelete_DoesNotRewire()
    {
        // O-2: deleting the puppet target unwinds the stack, so Unpuppet has
        // nothing to pop and must not rewire the session.
        using var env = GlobalTestEnv.Enter();
        var session = new Session();
        var pc = GameObject.Create("Pc", isPc: true);
        pc.PrivilegeLevel = Privilege.Builder;
        ObjectRegistry.AddObject(pc);
        var npc = GameObject.Create("Npc", isNpc: true);
        ObjectRegistry.AddObject(npc);
        session.Puppet = pc;
        pc.Session = session;
        pc.IsConnected = true;
        Assert.True(pc.Puppet(session, npc));

        npc.Delete(pc, true);
        Assert.True(npc.IsDeleted);
        Assert.Null(session.Puppet);
        Assert.False(pc.Unpuppet(session));
        Assert.Null(session.Puppet);
    }

    [Fact]
    public void Unpuppet_AfterPrevDelete_DoesNotRewireDeleted()
    {
        // O-2: when the stacked origin was deleted, Unpuppet must not point
        // the live session back at the deleted object.
        using var env = GlobalTestEnv.Enter();
        var session = new Session();
        var pc = GameObject.Create("Pc", isPc: true);
        pc.PrivilegeLevel = Privilege.Builder;
        ObjectRegistry.AddObject(pc);
        var npc = GameObject.Create("Npc", isNpc: true);
        ObjectRegistry.AddObject(npc);
        session.Puppet = pc;
        pc.Session = session;
        pc.IsConnected = true;
        Assert.True(pc.Puppet(session, npc));

        pc.Delete(npc, true);
        Assert.True(pc.IsDeleted);
        Assert.True(pc.Unpuppet(session));
        Assert.Null(session.Puppet);
        Assert.Null(pc.Session);
    }

    [Fact]
    public void AddLink_CaseVariant_DoesNotDuplicate()
    {
        // O-3: link lookup folds case, so AddLink must too — otherwise the
        // second link is installed but unreachable (shadowed).
        using var env = GlobalTestEnv.Enter();
        var (n1, _) = MakeNodes("Audit4O3");
        n1.AddLink(new NodeLink("North", new Coord("Audit4O3", 0, 1, 0)));
        n1.AddLink(new NodeLink("north", new Coord("Audit4O3", 0, 2, 0)));
        Assert.Single(n1.GetLinks());
        Assert.NotNull(n1.GetLinkByName("NORTH"));
    }

    [Fact]
    public void VetoedMove_DoesNotLeakFollowStack()
    {
        // O-4: a vetoed move pushed a pre-move entry with no post-move pop;
        // the pairing must be cancelled so later moves pair correctly.
        using var env = GlobalTestEnv.Enter();
        var (n1, _) = MakeNodes("Audit4O4a");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        InstallFollowScript(follower, leader);
        var script = leader.GetScriptsByType("FollowScript").First() as FollowScript;
        Assert.NotNull(script);

        Assert.False(leader.MoveTo(leader));
        Assert.Null(script!.OldLoc);
    }

    [Fact]
    public void ForceMove_DoesNotConsumeFollowStack()
    {
        // O-4: a force move skips pre-move (no push), so its post-move must
        // leave an empty stack alone instead of eating a partner entry.
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeNodes("Audit4O4b");
        var leader = MakePc("Leader", n1);
        var follower = MakePc("Follower", n1);
        InstallFollowScript(follower, leader);
        var script = leader.GetScriptsByType("FollowScript").First() as FollowScript;
        Assert.NotNull(script);

        Assert.True(leader.MoveTo(n2, force: true));
        Assert.Null(script!.OldLoc);
        Assert.Equal(leader.Id, follower.Following);
    }

    [Fact]
    public void Access_ThrowingPredicate_FailsClosed()
    {
        // O-5: a throwing lock predicate denies instead of propagating.
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Guarded");
        ObjectRegistry.AddObject(obj);
        var viewer = GameObject.Create("Viewer", isPc: true);
        ObjectRegistry.AddObject(viewer);
        obj.AddLock("view", _ => throw new InvalidOperationException("boom"));

        var ex = Record.Exception(() => obj.Access(viewer, "view"));
        Assert.Null(ex);
        Assert.False(obj.Access(viewer, "view"));
    }

    [Fact]
    public void DoorAccess_ThrowingPredicate_FailsClosed()
    {
        // O-5: same fail-closed rule on the door lock path.
        using var env = GlobalTestEnv.Enter();
        var viewer = GameObject.Create("Viewer", isPc: true);
        ObjectRegistry.AddObject(viewer);
        var door = new Door(new Coord("Audit4O5", 0, 0, 0), new Coord("Audit4O5", 0, 1, 0), "north", "south");
        door.AddLock("open", _ => throw new InvalidOperationException("boom"));

        var ex = Record.Exception(() => door.Access(viewer, "open"));
        Assert.Null(ex);
        Assert.False(door.Access(viewer, "open"));
    }

    [Fact]
    public void VerbTense_IsCaseInsensitive()
    {
        // O-6: the tense tables fold case, so tense queries must too.
        Assert.Equal("3rd singular present", Conjugate.VerbTense("IS"));
        Assert.True(Conjugate.VerbIsPresent("IS", "3"));
        Assert.True(Conjugate.VerbIsPast("WAS", "1"));
        Assert.True(Conjugate.VerbIsTense("RUNNING", "present participle"));
    }
}
