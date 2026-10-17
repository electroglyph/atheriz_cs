using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// Batch D of the commands.md rollout: teleport, tag, scripts, emit, force
// (builder verbs) plus boot (superuser).
[Collection("Ported")]
public class BatchDCommandsTests
{
    private static GameObject MakeBuilder()
        => PortedHelpers.MakeCaller("BatchDBuilder", Privilege.Builder);

    private static GameObject MakeSuper()
        => PortedHelpers.MakeCaller("BatchDSuper", Privilege.Admin);

    private static GameObject MakeRoom(string name)
    {
        var room = GameObject.Create(name);
        ObjectRegistry.AddObject(room);
        return room;
    }

    private static void Run(GameObject caller, string text)
    {
        var job = CommandDispatcher.DispatchLoggedIn(caller, text, immediate: true);
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void Teleport_SelfToNamedRoom()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var lounge = MakeRoom("lounge");
        var attic = MakeRoom("attic");
        hero.Location = new LocationRef.ObjectLocation(attic.Id);
        Run(hero, "teleport = lounge");
        Assert.Same(lounge, hero.ResolveLocationObject());
    }

    [Fact]
    public void Teleport_OtherJoinsMe()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var lounge = MakeRoom("lounge");
        var attic = MakeRoom("attic");
        hero.Location = new LocationRef.ObjectLocation(lounge.Id);
        var bob = GameObject.Create("Bob");
        ObjectRegistry.AddObject(bob);
        bob.Location = new LocationRef.ObjectLocation(attic.Id);
        Run(hero, "teleport Bob");
        Assert.Same(lounge, bob.ResolveLocationObject());
    }

    [Fact]
    public void Teleport_ToNone_Detaches()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var lounge = MakeRoom("lounge");
        var bob = GameObject.Create("Bob");
        ObjectRegistry.AddObject(bob);
        bob.Location = new LocationRef.ObjectLocation(lounge.Id);
        Run(hero, "teleport --tonone Bob");
        Assert.Null(bob.ResolveLocationObject());
    }

    [Fact]
    public void Teleport_Quiet_MovesSilently()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var lounge = MakeRoom("lounge");
        var attic = MakeRoom("attic");
        hero.Location = new LocationRef.ObjectLocation(attic.Id);
        Run(hero, "teleport --quiet = lounge");
        Assert.Same(lounge, hero.ResolveLocationObject());
        Assert.Empty(hero.PeekMessages());
    }

    [Fact]
    public void Teleport_IntoExit_FollowsLink()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var nodeA = new Node(new Coord("teleroom", 0, 0, 0));
        var nodeB = new Node(new Coord("teleroom", 0, 1, 0));
        ObjectRegistry.AddObject(nodeA);
        ObjectRegistry.AddObject(nodeB);
        nodeA.AddLink(new NodeLink("north", new Coord("teleroom", 0, 1, 0)));
        hero.Location = new LocationRef.ObjectLocation(nodeA.Id);
        var bob = GameObject.Create("Bob");
        ObjectRegistry.AddObject(bob);
        bob.Location = new LocationRef.ObjectLocation(nodeA.Id);
        Run(hero, "teleport --intoexit Bob = north");
        Assert.Same(nodeB, bob.ResolveLocationObject());
    }

    [Fact]
    public void Tag_Add_SetsTags()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeRoom("box");
        Run(MakeBuilder(), "tag box = red;round");
        Assert.True(box.HasTag("red"));
        Assert.True(box.HasTag("round"));
    }

    [Fact]
    public void Tag_Bare_ListsTags()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeRoom("listbox");
        Run(hero, "tag listbox = t1;t2");
        hero.ClearMessages();
        Run(hero, "tag listbox");
        var text = string.Join("\n", hero.PeekMessages());
        Assert.Contains("t1", text);
        Assert.Contains("t2", text);
    }

    [Fact]
    public void Tag_Comma_SeparatesLikeSemicolon()
    {
        using var env = GlobalTestEnv.Enter();
        var box = MakeRoom("commabox");
        Run(MakeBuilder(), "tag commabox = a,b;c");
        Assert.True(box.HasTag("a"));
        Assert.True(box.HasTag("b"));
        Assert.True(box.HasTag("c"));
    }

    [Fact]
    public void Tag_Remove_Clears()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeRoom("box");
        Run(hero, "tag box = red;round");
        Run(hero, "tag --remove box = red");
        Assert.False(box.HasTag("red"));
        Assert.True(box.HasTag("round"));
    }

    [Fact]
    public void Tag_Search_ListsObjects()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeRoom("box");
        Run(hero, "tag box = red");
        hero.ClearMessages();
        Run(hero, "tag --search red");
        Assert.Contains(hero.PeekMessages(), m => m.Contains("box"));
    }

    [Fact]
    public void Tag_Alias_ResolvesToTagCommand()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.IsType<TagCommand>(CommandRegistry.LoggedIn.Get("tags"));
    }

    [Fact]
    public void Scripts_ListsTickables()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var tick = GameObject.Create("cron", isTickable: true);
        ObjectRegistry.AddObject(tick);
        Run(hero, "scripts");
        Assert.Contains(hero.PeekMessages(), m => m.Contains("cron"));
    }

    [Fact]
    public void Scripts_None_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        Run(hero, "scripts");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Emit_Bare_RoomBroadcast()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var room = MakeRoom("hall");
        var ears = PortedHelpers.MakeCaller("Ears");
        Assert.True(hero.MoveTo(room));
        Assert.True(ears.MoveTo(room));
        ears.ClearMessages();
        Run(hero, "emit Boo");
        Assert.Contains(ears.PeekMessages(), m => m.Contains("Boo"));
    }

    [Fact]
    public void Emit_Targeted_ObjectHears()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var box = MakeRoom("box");
        Run(hero, "emit box = Zzz");
        Assert.Contains(box.PeekMessages(), m => m.Contains("Zzz"));
    }

    [Fact]
    public void Emit_Channel_ReachesListeners()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var ears = PortedHelpers.MakeCaller("Ears");
        var chan = Channel.Create("batchd-chan");
        chan.AddListener(ears);
        Run(hero, "emit batchd-chan = Hi");
        Assert.Contains(ears.PeekMessages(), m => m.Contains("Hi"));
    }

    [Fact]
    public void Emit_OfflineAccount_Reported()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var ghost = Account.Create("batchd-ghost", "password1");
        Run(hero, $"emit {ghost.Name} = Hi");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Force_RunsCommandAsTarget()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        var room = MakeRoom("stage");
        var bob = GameObject.Create("Bob", isNpc: true);
        var witness = PortedHelpers.MakeCaller("Witness");
        ObjectRegistry.AddObject(bob);
        Assert.True(hero.MoveTo(room));
        Assert.True(bob.MoveTo(room));
        Assert.True(witness.MoveTo(room));
        witness.ClearMessages();
        Run(hero, "force Bob = say forced hi");
        Assert.Contains(witness.PeekMessages(), m => m.Contains("forced hi"));
    }

    [Fact]
    public void Force_UnknownTarget_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = MakeBuilder();
        Run(hero, "force zzz = say hi");
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Boot_ByAccountName_KicksSession()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = ConnectionManager.GlobalInstance;
        var mgr = new ConnectionManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var acc = Account.Create("batchd-vic", "password1");
            var conn = new TestConnection("sess-vic");
            Assert.True(mgr.RegisterConnection("sess-vic", conn));
            conn.Session.Account = acc;
            var hero = MakeSuper();
            Run(hero, "boot batchd-vic");
            Assert.True(conn.Closed);
            Assert.Single(hero.PeekMessages());
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Boot_Unknown_Answered()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = new ConnectionManager();
        try
        {
            var hero = MakeSuper();
            Run(hero, "boot zzz");
            Assert.Single(hero.PeekMessages());
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }

    [Fact]
    public void Boot_Reason_Delivered()
    {
        using var env = GlobalTestEnv.Enter();
        var prev = ConnectionManager.GlobalInstance;
        var mgr = new ConnectionManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var acc = Account.Create("batchd-vic2", "password1");
            var conn = new TestConnection("sess-vic2");
            Assert.True(mgr.RegisterConnection("sess-vic2", conn));
            conn.Session.Account = acc;
            Run(MakeSuper(), "boot batchd-vic2 = go away");
            var text = string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));
            Assert.Contains("go away", text);
            Assert.True(conn.Closed);
        }
        finally { ConnectionManager.GlobalInstance = prev; }
    }
}
