using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Regression;

// Regression pins for audit batch A (F1-F6).
[Collection("Ported")]
public sealed class Audit6BatchATests
{
    private sealed class FromCapturingObject : GameObject
    {
        public GameObject? SeenFrom;
        public override void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors = false, string? msgType = null)
        {
            SeenFrom = fromObj;
            base.Msg(text, fromObj, mapping, raiseErrors, msgType);
        }
    }

    [Fact]
    public void GroupAdd_MultiWordName_JoinsRemainder()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var room = new Node(new Coord("f1room", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var hero = GameObject.Create("hero", isPc: true);
            var bob = GameObject.Create("Big Bob", isNpc: true);
            ObjectRegistry.AddObject(hero);
            ObjectRegistry.AddObject(bob);
            Assert.True(hero.MoveTo(room));
            Assert.True(bob.MoveTo(room));
            hero.AddFollower(bob.Id);
            hero.ClearMessages();

            var job = CommandDispatcher.DispatchLoggedIn(hero, "group add Big Bob", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);

            var text = string.Join("\n", hero.PeekMessages());
            Assert.Contains("added Big Bob", text);
            Assert.DoesNotContain("Could not find", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GroupKick_MultiWordName_JoinsRemainder()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var room = new Node(new Coord("f1kroom", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var hero = GameObject.Create("hero", isPc: true);
            var bob = GameObject.Create("Big Bob", isNpc: true);
            ObjectRegistry.AddObject(hero);
            ObjectRegistry.AddObject(bob);
            Assert.True(hero.MoveTo(room));
            Assert.True(bob.MoveTo(room));
            hero.AddFollower(bob.Id);
            var add = CommandDispatcher.DispatchLoggedIn(hero, "group add Big Bob", immediate: true);
            add!.Func(add.Caller, add.Args);
            hero.ClearMessages();

            var kick = CommandDispatcher.DispatchLoggedIn(hero, "group kick Big Bob", immediate: true);
            Assert.NotNull(kick);
            kick!.Func(kick.Caller, kick.Args);

            var text = string.Join("\n", hero.PeekMessages());
            Assert.Contains("kicked Big Bob", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void DoorCommand_BareDirectionWord_CreatesDoor()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var area = new NodeArea("f2room");
            var grid = new NodeGrid("f2room", 0);
            var start = new Node(new Coord("f2room", 0, 0, 0));
            grid.Nodes[(0, 0)] = start;
            ObjectRegistry.AddObject(start);
            var dest = new Node(new Coord("f2room", 2, 0, 0));
            grid.Nodes[(2, 0)] = dest;
            ObjectRegistry.AddObject(dest);
            area.AddGrid(grid);
            nh.AddArea(area);
            var builder = GameObject.Create("f2builder", isPc: true, privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            Assert.True(builder.MoveTo(start));
            builder.ClearMessages();

            var pa = new GameArgumentParser.ParsedArgs();
            pa["north"] = false; pa["south"] = false; pa["east"] = false; pa["west"] = false;
            pa["up"] = false; pa["down"] = false; pa["remove"] = false; pa["auto"] = true;
            pa["args"] = new List<string> { "east" };
            new DoorCommand().Run(builder, pa);

            var text = string.Join("\n", builder.PeekMessages());
            Assert.Contains("Created door at", text);
            Assert.DoesNotContain("You must specify a direction", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Look_NowhereWithDesc_StillReportsNowhere()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var puppet = GameObject.Create("f3wanderer", isPc: true, desc: "A very detailed description.");
            ObjectRegistry.AddObject(puppet);
            Assert.Null(puppet.ResolveLocationObject());
            puppet.ClearMessages();

            var pa = new GameArgumentParser.ParsedArgs();
            pa["target"] = new List<string>();
            new LookCommand().Run(puppet, pa);

            var msgs = puppet.PeekMessages();
            Assert.Contains("You are nowhere.", msgs);
            Assert.DoesNotContain("A very detailed description.", string.Join("\n", msgs));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetDisplayName_ViewDeniedOffline_DoesNotLeakName()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var target = GameObject.Create("f4hidden", isPc: true);
            var looker = GameObject.Create("f4looker", isPc: true);
            ObjectRegistry.AddObject(target);
            ObjectRegistry.AddObject(looker);
            Assert.False(target.IsConnected);
            Assert.False(target.Access(looker, "view"));
            Assert.Equal("Someone", target.GetDisplayName(looker));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtHear_HearingNonPc_ReceivesSound()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = new Node(new Coord("f5room", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var npc = GameObject.Create("f5listener", isNpc: true);
            var emitter = GameObject.Create("f5emitter", isNpc: true);
            ObjectRegistry.AddObject(npc);
            ObjectRegistry.AddObject(emitter);
            Assert.True(npc.MoveTo(room));
            Assert.True(emitter.MoveTo(room));
            Assert.False(npc.IsPc);
            Assert.True(npc.CanHear);
            npc.ClearMessages();

            npc.AtHear(emitter, "a clang", "", 50.0, false);

            Assert.Contains("You hear something", string.Join("\n", npc.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtHear_DeafNonPc_HearsNothing()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = new Node(new Coord("f5droom", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var npc = GameObject.Create("f5deaf", isNpc: true);
            var emitter = GameObject.Create("f5demitter", isNpc: true);
            ObjectRegistry.AddObject(npc);
            ObjectRegistry.AddObject(emitter);
            Assert.True(npc.MoveTo(room));
            Assert.True(emitter.MoveTo(room));
            npc.CanHear = false;
            npc.ClearMessages();

            npc.AtHear(emitter, "a clang", "", 50.0, false);

            Assert.Empty(npc.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ChannelMsg_ForwardsSender_ToListeners()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var sender = GameObject.Create("f6sender", isPc: true);
            var probe = new FromCapturingObject();
            ObjectRegistry.AddObject(sender);
            ObjectRegistry.AddObject(probe);
            var channel = Channel.Create("f6channel", sender);
            channel.AddListener(probe);

            channel.Msg("hello", sender);

            Assert.Same(sender, probe.SeenFrom);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
