// Pins for simplify3 §4 batch B20b: Desc Node-type-test removal, Door
// GetLinks double-copy removal, Put exclude hoist, Build EnsureLinks table,
// alias-list helper, "..." nargs unification, dispatcher lower removal,
// connect double-cast removal, spam password retention removal, none comment.
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B20bCommandCleanupTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (NodeHandler Nh, Node Node, GameObject Builder) EnterBuilderInNode(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        var builder = GameObject.Create("b20b_builder", isPc: true, privilege: Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        Assert.True(builder.MoveTo(node));
        builder.ClearMessages();
        return (nh, node, builder);
    }

    [Fact]
    public void DescCommand_RunOnNodeLocation_UpdatesDesc()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node, builder) = EnterBuilderInNode("b20bdesc");
        try
        {
            RunJob(CommandDispatcher.DispatchLoggedIn(builder, "desc A quiet room", immediate: true));
            Assert.Equal("A quiet room", node.Desc);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void DoorCommand_RepairWrongCoord_RelinksToCorrectCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var area = new NodeArea("b20bdoor");
            var grid = new NodeGrid("b20bdoor", 0);
            var start = new Node(new Coord("b20bdoor", 0, 0, 0));
            grid.Nodes[(0, 0)] = start;
            ObjectRegistry.AddObject(start);
            var dest = new Node(new Coord("b20bdoor", 2, 0, 0));
            grid.Nodes[(2, 0)] = dest;
            ObjectRegistry.AddObject(dest);
            area.AddGrid(grid);
            nh.AddArea(area);
            var caller = GameObject.Create("b20b_doorbuilder", isPc: true, privilege: Privilege.Builder);
            ObjectRegistry.AddObject(caller);
            Assert.True(caller.MoveTo(start));
            // Wrong-coord links on both ends force both repair loops.
            dest.AddLink(new NodeLink("west", new Coord("b20bdoor", 9, 9, 0), new List<string> { "w" }));
            start.AddLink(new NodeLink("east", new Coord("b20bdoor", 9, 9, 0), new List<string> { "e" }));
            caller.ClearMessages();

            var pa = new GameArgumentParser.ParsedArgs();
            pa["north"] = false; pa["south"] = false; pa["east"] = true; pa["west"] = false;
            pa["up"] = false; pa["down"] = false; pa["remove"] = false; pa["auto"] = true;
            pa["args"] = new List<string>();
            new DoorCommand().Run(caller, pa);

            var destWest = dest.GetLinks().Where(l => l.Name == "west").ToList();
            Assert.Single(destWest);
            Assert.Equal(new Coord("b20bdoor", 0, 0, 0), destWest[0].Coord);
            var hereEast = start.GetLinks().Where(l => l.Name == "east").ToList();
            Assert.Single(hereEast);
            Assert.Equal(new Coord("b20bdoor", 2, 0, 0), hereEast[0].Coord);
            var text = string.Join("\n", caller.PeekMessages());
            Assert.Contains("Removed link 'west'", text);
            Assert.Contains("Removed link 'east'", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_PutAllInContainer_MovesAllItems()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node, holder) = EnterBuilderInNode("b20bputall");
        try
        {
            var chest = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(chest);
            Assert.True(chest.MoveTo(node));
            var one = GameObject.Create("one", isItem: true);
            ObjectRegistry.AddObject(one);
            Assert.True(one.MoveTo(holder));
            var two = GameObject.Create("two", isItem: true);
            ObjectRegistry.AddObject(two);
            Assert.True(two.MoveTo(holder));
            holder.ClearMessages();

            RunJob(CommandDispatcher.DispatchLoggedIn(holder, "put all in chest", immediate: true));

            Assert.Contains(one.Id, chest.ContentsSnapshot);
            Assert.Contains(two.Id, chest.ContentsSnapshot);
            Assert.Empty(holder.ContentsSnapshot);
            var text = string.Join("\n", holder.PeekMessages());
            Assert.Contains("You put one in chest.", text);
            Assert.Contains("You put two in chest.", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void PutCommand_PutSingleInContainer_MovesItem()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, node, holder) = EnterBuilderInNode("b20bputone");
        try
        {
            var chest = GameObject.Create("chest", isContainer: true);
            ObjectRegistry.AddObject(chest);
            Assert.True(chest.MoveTo(node));
            var coin = GameObject.Create("coin", isItem: true);
            ObjectRegistry.AddObject(coin);
            Assert.True(coin.MoveTo(holder));
            holder.ClearMessages();

            var cmd = new PutCommand();
            cmd.Run(holder, cmd.Parser!.ParseArgs(["coin", "in", "chest"]));

            Assert.Contains(coin.Id, chest.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, holder.ContentsSnapshot);
            Assert.Contains($"You put {coin.Name} in {chest.Name}.", holder.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void BuildCommand_BuildNorthRoom_CreatesBidirectionalLinks()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var mh = GlobalServices.GetMapHandler();
        try { nh.RemoveArea("b20bbuild"); } catch { }
        var area = new NodeArea("b20bbuild");
        var grid = new NodeGrid("b20bbuild", 0);
        var start = new Node(new Coord("b20bbuild", 0, 0, 0), desc: "Start");
        grid.Nodes[(0, 0)] = start;
        ObjectRegistry.AddObject(start);
        area.AddGrid(grid);
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        try
        {
            // Seed an east neighbor placeholder so the EnsureLinks table takes
            // the east row for the new room (forward link even with no node).
            var mi = mh.EnsureMapInfo("b20bbuild", 0);
            mi.Lock.EnterWriteLock();
            try { mi.PreGrid[(1, 1)] = AtherizSettings.Global.RoomPlaceholder; }
            finally { mi.Lock.ExitWriteLock(); }
            var caller = GameObject.Create("b20b_builder2", isPc: true, privilege: Privilege.Builder);
            ObjectRegistry.AddObject(caller);
            caller.Location = new LocationRef.CoordLocation(start.Coord);
            start.AddObject(caller);
            caller.ClearMessages();

            var pa = new GameArgumentParser.ParsedArgs();
            pa["n"] = true; pa["e"] = false; pa["s"] = false; pa["w"] = false;
            pa["u"] = false; pa["d"] = false; pa["x"] = false;
            pa["room"] = true; pa["road"] = false; pa["path"] = false;
            pa["desc"] = null; pa["single"] = false; pa["double"] = false;
            pa["round"] = false; pa["none"] = false; // none:true would set ch="" and skip the walls/EnsureLinks block under test
            new BuildCommand().Run(caller, pa);

            var created = nh.GetNode(new Coord("b20bbuild", 0, 1, 0));
            Assert.NotNull(created);
            Assert.Contains(start.GetLinks(), l => l.Name == "north" && l.Coord.Equals(new Coord("b20bbuild", 0, 1, 0)));
            Assert.Contains(created!.GetLinks(), l => l.Name == "south" && l.Coord.Equals(new Coord("b20bbuild", 0, 0, 0)));
            // EnsureLinks east row (n,s,e,w order preserved): forward link exists.
            Assert.Contains(created.GetLinks(), l => l.Name == "east" && l.Coord.Equals(new Coord("b20bbuild", 1, 1, 0)));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void FormatAliasList_CommandWithoutAliases_ReturnsKeyOnly()
    {
        var cmd = new DescCommand();
        Assert.Empty(cmd.Aliases);
        Assert.Equal("desc", HelpHelper.FormatAliasList(cmd));
        Assert.Contains("Aliases: desc", HelpHelper.FormatNoParser(cmd));
    }

    [Fact]
    public void FormatAliasList_CommandWithAliases_JoinsKeyAndAliases()
    {
        var cmd = new LookCommand();
        Assert.NotEmpty(cmd.Aliases);
        Assert.Equal("look, l", HelpHelper.FormatAliasList(cmd));
        Assert.Contains("Aliases: look, l", HelpHelper.FormatNoParser(cmd));
        Assert.Contains($"Aliases: {HelpHelper.FormatAliasList(cmd)}", cmd.PrintHelp());
    }

    [Fact]
    public void AddArgument_EllipsisNargs_ParsesRemainderOnBothOverloads()
    {
        var viaBuilder = new GameArgumentParser("b20b_a");
        viaBuilder.AddArgument("msg").Nargs("...");
        var a = viaBuilder.ParseArgs(["hello", "world"]);
        Assert.Equal(new List<string> { "hello", "world" }, a.GetList("msg"));

        var viaSingle = new GameArgumentParser("b20b_b");
        viaSingle.AddArgument("msg", "", "...");
        var b = viaSingle.ParseArgs(["hello", "world"]);
        Assert.Equal(new List<string> { "hello", "world" }, b.GetList("msg"));

        var viaRemainder = new GameArgumentParser("b20b_c");
        viaRemainder.AddArgument("msg", "", "REMAINDER");
        var c = viaRemainder.ParseArgs(["hello", "world"]);
        Assert.Equal(new List<string> { "hello", "world" }, c.GetList("msg"));
    }

    [Fact]
    public void Dispatch_UppercaseNoAliasKey_BlockedLikeLowercase()
    {
        using var env = GlobalTestEnv.Enter();
        CommandRegistry.Reset();
        var _ = CommandRegistry.LoggedIn;
        CommandDispatcher.SetSettings(new AtherizSettings { AutoCommandAliasing = true });
        try
        {
            var lowerPuppet = new GameObject { Name = "HeroLow" };
            lowerPuppet.ClearMessages();
            Assert.Null(CommandDispatcher.DispatchLoggedIn(lowerPuppet, "n", immediate: true));
            Assert.Contains("You can't do that.", lowerPuppet.PeekMessages());

            var upperPuppet = new GameObject { Name = "HeroUp" };
            upperPuppet.ClearMessages();
            Assert.Null(CommandDispatcher.DispatchLoggedIn(upperPuppet, "N", immediate: true));
            Assert.Contains("You can't do that.", upperPuppet.PeekMessages());
        }
        finally
        {
            CommandDispatcher.SetSettings(new AtherizSettings());
            CommandRegistry.Reset();
        }
    }

    [Fact]
    public void ConnectCommand_RunWithValidCredentials_WelcomesAccount()
    {
        using var env = GlobalTestEnv.Enter();
        var account = Account.Create("b20b_acc", "b20b_pw");
        Assert.NotNull(account);
        var caller = GameObject.Create("b20b_caller");
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();

        var pa = new ConnectCommand().Parser!.ParseArgs(["b20b_acc", "b20b_pw"]);
        new ConnectCommand().Run(caller, pa);

        Assert.Contains("Welcome b20b_acc.", string.Join("\n", caller.PeekMessages()));
    }

    [Fact]
    public void ConnectCommand_RunWithWrongPassword_ReportsInvalid()
    {
        using var env = GlobalTestEnv.Enter();
        var account = Account.Create("b20b_acc2", "b20b_correct");
        Assert.NotNull(account);
        var caller = GameObject.Create("b20b_caller2");
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();

        var pa = new ConnectCommand().Parser!.ParseArgs(["b20b_acc2", "b20b_wrong"]);
        new ConnectCommand().Run(caller, pa);

        Assert.Contains("Invalid password.", string.Join("\n", caller.PeekMessages()));
    }

    [Fact]
    public void SpamCommand_RunOnce_CreatesPasswordCheckedAccountWithoutPersistingPassword()
    {
        using var env = GlobalTestEnv.Enter();
        var origSave = AtherizSettings.Global.SavePath;
        var tmp = Path.Combine(env.TempPath, "b20bspam");
        Directory.CreateDirectory(tmp);
        AtherizSettings.Global.SavePath = tmp;
        try
        {
            var admin = GameObject.Create("b20b_root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            admin.ClearMessages();

            var pa = new SpamCommand().Parser!.ParseArgs(["1"]);
            new SpamCommand().Run(admin, pa);

            var accounts = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name.Equals("account1", StringComparison.OrdinalIgnoreCase));
            Assert.Single(accounts);
            var created = (Account)accounts[0];
            Assert.True(created.CheckPassword("password1"));
            var creds = File.ReadAllText(Path.Combine(tmp, "spam_accounts.txt"));
            Assert.Contains("account1", creds);
            Assert.DoesNotContain("password1", creds);
            Assert.Contains("Created 1 accounts/chars", string.Join("\n", admin.PeekMessages()));
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }

    [Fact]
    public void NoneCommand_RunUnknownInput_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        CommandRegistry.Reset();
        var _ = CommandRegistry.LoggedIn;
        try
        {
            var puppet = new GameObject { Name = "Hero" };
            puppet.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(puppet, "b20bnosuchcmd xyz", immediate: true);
            RunJob(job);
            Assert.Contains(puppet.PeekMessages(), m => m.Contains("not found"));
        }
        finally { CommandRegistry.Reset(); }
    }
}
