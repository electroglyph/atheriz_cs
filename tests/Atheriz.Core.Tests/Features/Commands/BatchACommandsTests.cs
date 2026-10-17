using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// Batch A of the commands.md rollout: home, nick, whisper, access, about,
// plus the pose alias on the existing emote verb.
[Collection("Ported")]
public class BatchACommandsTests
{
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

    private static GameObject MakeNickCaller(string accountName)
    {
        var acc = Account.Create(accountName, "password1");
        var caller = PortedHelpers.MakeCaller("NickUser");
        caller.Session = new Session(account: acc);
        return caller;
    }

    private static (GameObject Hero, GameObject Bob, GameObject Carol) MakeWhisperRoom()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord("whisper-room", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        var hero = GameObject.Create("hero", isPc: true);
        var bob = GameObject.Create("bob", isNpc: true);
        var carol = GameObject.Create("carol", isNpc: true);
        ObjectRegistry.AddObject(hero);
        ObjectRegistry.AddObject(bob);
        ObjectRegistry.AddObject(carol);
        Assert.True(hero.MoveTo(room));
        Assert.True(bob.MoveTo(room));
        Assert.True(carol.MoveTo(room));
        hero.ClearMessages();
        bob.ClearMessages();
        carol.ClearMessages();
        return (hero, bob, carol);
    }

    [Fact]
    public void Home_MovedElsewhere_TeleportsBack()
    {
        using var env = GlobalTestEnv.Enter();
        var home = MakeRoom("homestead");
        var away = MakeRoom("faraway");
        var hero = PortedHelpers.MakeCaller("Homer");
        hero.Location = new LocationRef.ObjectLocation(away.Id);
        hero.Home = new LocationRef.ObjectLocation(home.Id);
        Run(hero, "home");
        Assert.Same(home, hero.ResolveLocationObject());
    }

    [Fact]
    public void Home_AlreadyHome_StaysPutAndAnswers()
    {
        using var env = GlobalTestEnv.Enter();
        var home = MakeRoom("homestead");
        var hero = PortedHelpers.MakeCaller("Homer");
        hero.Location = new LocationRef.ObjectLocation(home.Id);
        hero.Home = new LocationRef.ObjectLocation(home.Id);
        Run(hero, "home");
        Assert.Same(home, hero.ResolveLocationObject());
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void Home_NoHome_StaysNowhereAndAnswers()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Homer");
        Run(hero, "home");
        Assert.Null(hero.ResolveLocationObject());
        Assert.Single(hero.PeekMessages());
    }

    [Fact]
    public void About_AnswersOnceNamingTheServer()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Curious");
        Run(hero, "about");
        var messages = hero.PeekMessages();
        Assert.Single(messages);
        Assert.Contains(AtherizSettings.Global.ServerName, messages[0]);
    }

    [Fact]
    public void About_VersionAlias_ReachesSameVerb()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.IsType<AboutCommand>(CommandRegistry.LoggedIn.Get("version"));
    }

    [Fact]
    public void Access_Bare_ShowsOwnRank()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Ranky", Privilege.Player);
        Run(hero, "access");
        var messages = hero.PeekMessages();
        Assert.Single(messages);
        Assert.Contains(Privilege.Player.ToString(), messages[0]);
    }

    [Fact]
    public void Access_ObjectBranch_Player_DeniedAndLocksUntouched()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Sneaky", Privilege.Player);
        var box = MakeRoom("box");
        Run(hero, "access box");
        Assert.Single(hero.PeekMessages());
        Assert.True(box.Access(hero, "view"));
    }

    [Fact]
    public void Access_ObjectBranch_Builder_ViewsLocks()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Bossy", Privilege.Builder);
        var box = MakeRoom("box");
        Run(hero, "access box");
        var messages = hero.PeekMessages();
        Assert.Single(messages);
        Assert.Contains(box.Name, messages[0]);
    }

    [Fact]
    public void Access_SetLock_ChangesEnforcement()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Bossy", Privilege.Builder);
        var box = MakeRoom("box");
        Assert.True(box.Access(hero, "view"));
        Run(hero, "access box = view:denied");
        Assert.False(box.Access(hero, "view"));
    }

    [Fact]
    public void Access_BadPolicy_LeavesLocksUntouched()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Bossy", Privilege.Builder);
        var box = MakeRoom("box");
        Run(hero, "access box = view:nope");
        Assert.Single(hero.PeekMessages());
        Assert.True(box.Access(hero, "view"));
    }

    [Fact]
    public void Access_EmptyPolicy_ClearsLock()
    {
        using var env = GlobalTestEnv.Enter();
        var hero = PortedHelpers.MakeCaller("Bossy", Privilege.Builder);
        var box = MakeRoom("box");
        Run(hero, "access box = view:denied");
        Assert.False(box.Access(hero, "view"));
        Run(hero, "access box = view:");
        Assert.True(box.Access(hero, "view"));
    }

    [Fact]
    public void Nick_Set_PersistsInAccountExtras()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeNickCaller("batcha-nick-1");
        Run(caller, "nick greet = say hello");
        Assert.Equal("say hello", NickCommand.ReadNicks(caller.Session!.Account!).GetValueOrDefault("inputline:greet"));
    }

    [Fact]
    public void Nick_List_ShowsStoredKey()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeNickCaller("batcha-nick-2");
        Run(caller, "nick greet = say hello");
        caller.ClearMessages();
        Run(caller, "nick");
        Assert.Contains(caller.PeekMessages(), m => m.Contains("greet"));
    }

    [Fact]
    public void Nick_Expand_RewritesUnknownFirstWord()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeNickCaller("batcha-nick-3");
        Run(caller, "nick greet = say hello");
        Assert.Equal("say hello", NickCommand.ExpandNicks(caller, "greet"));
    }

    [Fact]
    public void Nick_Expand_LeavesRealCommandsAlone()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeNickCaller("batcha-nick-4");
        Run(caller, "nick look = say hello");
        Assert.Equal("look", NickCommand.ExpandNicks(caller, "look"));
    }

    [Fact]
    public void Nick_Expand_SkipsSingleCharKeys()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeNickCaller("batcha-nick-5");
        Assert.Equal("n", NickCommand.ExpandNicks(caller, "n"));
    }

    [Fact]
    public void Nick_Dispatch_ExpandsThroughSay()
    {
        using var env = GlobalTestEnv.Enter();
        var room = MakeRoom("lobby");
        var caller = MakeNickCaller("batcha-nick-6");
        ObjectRegistry.AddObject(caller);
        var ears = PortedHelpers.MakeCaller("Ears");
        Assert.True(caller.MoveTo(room));
        Assert.True(ears.MoveTo(room));
        Run(caller, "nick loud = say");
        ears.ClearMessages();
        Run(caller, "loud hello room");
        Assert.Contains(ears.PeekMessages(), m => m.Contains("hello room"));
    }

    [Fact]
    public void Whisper_EqualsForm_ReachesOnlyTarget()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var (hero, bob, carol) = MakeWhisperRoom();
            Run(hero, "whisper bob = psst secret");
            Assert.Contains(bob.PeekMessages(), m => m.Contains("psst secret"));
            Assert.Empty(carol.PeekMessages());
            Assert.Single(hero.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Whisper_FirstWordForm_WorksForSingleWordTarget()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var (hero, bob, carol) = MakeWhisperRoom();
            Run(hero, "whisper bob psst2");
            Assert.Contains(bob.PeekMessages(), m => m.Contains("psst2"));
            Assert.Empty(carol.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Whisper_UnknownTarget_NobodyHearsAnything()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var (hero, bob, carol) = MakeWhisperRoom();
            Run(hero, "whisper zzz = hi");
            Assert.Single(hero.PeekMessages());
            Assert.Empty(bob.PeekMessages());
            Assert.Empty(carol.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Whisper_Self_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var (hero, bob, _) = MakeWhisperRoom();
            Run(hero, "whisper hero = hi");
            Assert.Single(hero.PeekMessages());
            Assert.Empty(bob.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Pose_ResolvesToEmoteCommand()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.IsType<EmoteCommand>(CommandRegistry.LoggedIn.Get("pose"));
    }

    [Fact]
    public void Pose_Dispatch_BroadcastsLikeEmote()
    {
        using var env = GlobalTestEnv.Enter();
        var room = MakeRoom("stage");
        var hero = PortedHelpers.MakeCaller("Actor");
        var watcher = PortedHelpers.MakeCaller("Watcher");
        Assert.True(hero.MoveTo(room));
        Assert.True(watcher.MoveTo(room));
        Run(hero, "pose waves hello");
        Assert.Contains(watcher.PeekMessages(), m => m.Contains("waves hello"));
    }
}
