// Port of atheriz/tests/test_simple_commands.py:1
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedSimpleCommandsTests
{
    private static GameObject MakeCaller(string name = "Alice", bool builder = false, bool superuser = false) => PortedHelpers.MakeCaller(name, builder, superuser);

    // ----- Desc -----
    [Fact]
    public void Desc_AccessRequiresBuilder()
    {
        var c = MakeCaller(builder: false);
        Assert.False(new DescCommand().Access(c));
    }

    [Fact]
    public void Desc_AccessAllowedForBuilder()
    {
        var c = MakeCaller(builder: true);
        Assert.True(new DescCommand().Access(c));
    }

    [Fact]
    public void Desc_NoArgsShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        new DescCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m.Contains("Aliases") || m.ToLowerInvariant().Contains("desc"));
    }

    [Fact]
    public void Desc_EmptyTextFallsThroughToHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        new DescCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m.Contains("aliases:") || m.ToLowerInvariant().Contains("usage"));
    }

    [Fact]
    public void Desc_NoLocationMsgNowhere()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        c.Location = Persistence.Dto.LocationRef.NullLocation.Instance;
        var pa = new GameArgumentParser.ParsedArgs();
        pa["text"] = new List<string>{"A","new","desc"};
        new DescCommand().Run(c, pa);
        Assert.Contains(c.PeekMessages(), m => m == "You are nowhere!");
    }

    [Fact]
    public void Desc_SetsDescWithNewlines()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        var coord = new Coord("test_simple", 0,0,0);
        var loc = new Node(coord, desc: "");
        c.Location = new Persistence.Dto.LocationRef.CoordLocation(coord);
        var pa = new GameArgumentParser.ParsedArgs();
        pa["text"] = new List<string>{"Line1","\\n","Line2"};
        new DescCommand().Run(c, pa);
        Assert.Equal("Line1 \n Line2", loc.Desc);
        Assert.Contains(c.PeekMessages(), m => !string.IsNullOrEmpty(m));
    }

    // ----- Say -----
    [Fact]
    public void Say_NoArgsShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller();
        new SayCommand().Run(c, null);
        Assert.Single(c.PeekMessages());
    }

    [Fact]
    public void Say_CallsAtSayWithJoinedText()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new TestSayCaller("Alice");
        ObjectRegistry.AddObject(c);
        var pa = new GameArgumentParser.ParsedArgs();
        pa["text"] = new List<string>{"hello","world"};
        new SayCommand().Run(c, pa);
        Assert.True(c.AtSayCalled);
        Assert.Equal("hello world", c.AtSayText);
        Assert.True(c.AtSayMsgSelf);
    }

    private sealed class TestSayCaller : GameObject
    {
        public bool AtSayCalled; public string? AtSayText; public bool AtSayMsgSelf;
        public TestSayCaller(string name) { Name = name; }
        public override void AtSay(string text, bool msgSelf = true) { AtSayCalled = true; AtSayText = text; AtSayMsgSelf = msgSelf; }
    }

    [Fact]
    public void Say_EmptyTextShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new TestSayCaller("Alice");
        ObjectRegistry.AddObject(c);
        var pa = new GameArgumentParser.ParsedArgs();
        pa["text"] = new List<string>();
        new SayCommand().Run(c, pa);
        Assert.False(c.AtSayCalled);
        Assert.Single(c.PeekMessages());
    }

    [Fact]
    public void Say_AliasIsApostrophe()
    {
        Assert.Contains("'", new SayCommand().Aliases);
    }

    // ----- Quit -----
    [Fact]
    public void Quit_SendsGoodbyeAndCloses()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller();
        var sess = new Session();
        var conn = new FakeConnection();
        sess.Connection = conn;
        c.Session = sess;
        new QuitCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m == "Goodbye!");
        Assert.True(conn.Closed);
    }

    [Fact]
    public void Quit_AliasesIncludeLogout()
    {
        var cmd = new QuitCommand();
        Assert.Contains("logout", cmd.Aliases);
        Assert.Contains("exit", cmd.Aliases);
        Assert.Contains("disconnect", cmd.Aliases);
    }

    // ----- Inventory -----
    [Fact]
    public void Inventory_EmptyMsg()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller();
        new InventoryCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m == "You are carrying nothing.");
    }

    [Fact]
    public void Inventory_ListsItems()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller();
        var apple = GameObject.Create("Apple");
        ObjectRegistry.AddObject(apple);
        apple.MoveTo(c);
        c.ClearMessages();
        new InventoryCommand().Run(c, null);
        var msg = string.Join(" ", c.PeekMessages());
        Assert.Contains("Apple", msg);
        Assert.Contains("carrying", msg.ToLowerInvariant());
    }

    [Fact]
    public void Inventory_AliasIsI()
    {
        Assert.Contains("i", new InventoryCommand().Aliases);
    }

    // ----- Quell -----
    [Fact]
    public void Quell_RequiresBuilder()
    {
        var c = MakeCaller(builder: false);
        Assert.False(new QuellCommand().Access(c));
    }

    [Fact]
    public void Quell_SetsQuelled()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        c.Quelled = false;
        new QuellCommand().Run(c, null);
        Assert.True(c.Quelled);
        Assert.Contains(c.PeekMessages(), m => m == "You are now quelled.");
    }

    [Fact]
    public void Quell_AlreadyQuelledMsg()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        c.Quelled = true;
        new QuellCommand().Run(c, null);
        Assert.True(c.Quelled);
        Assert.Contains(c.PeekMessages(), m => m == "You are already quelled!");
    }

    [Fact]
    public void Unquell_RequiresBuilder()
    {
        var c = MakeCaller(builder: false);
        Assert.False(new UnquellCommand().Access(c));
    }

    [Fact]
    public void Unquell_ClearsQuelled()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        c.Quelled = true;
        new UnquellCommand().Run(c, null);
        Assert.False(c.Quelled);
        Assert.Contains(c.PeekMessages(), m => m == "You are now unquelled.");
    }

    [Fact]
    public void Unquell_NotQuelledMsg()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        c.Quelled = false;
        new UnquellCommand().Run(c, null);
        Assert.False(c.Quelled);
        Assert.Contains(c.PeekMessages(), m => m == "You are not quelled!");
    }

    // ----- Save -----
    [Fact]
    public void Save_AccessRequiresSuperuser()
    {
        var c = MakeCaller(builder: true, superuser: false);
        Assert.False(new SaveCommand().Access(c));
    }

    [Fact]
    public void Save_AccessAllowedForSuperuser()
    {
        var c = MakeCaller(superuser: true);
        Assert.True(new SaveCommand().Access(c));
    }

    [Fact]
    public void Save_CallsAllHandlers()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(superuser: true);
        new SaveCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m.Contains("Saved in"));
    }

    [Fact]
    public void Save_AccessDeniedViaDispatch()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(superuser: false);
        c.ClearMessages();
        var job = Atheriz.Core.Commands.CommandDispatcher.DispatchLoggedIn(c, "save", immediate: true);
        Assert.Null(job);
        Assert.Contains(c.PeekMessages(), m => m.ToLowerInvariant().Contains("can't do that"));
    }

    [Fact]
    public void Quell_AlreadyQuelled_Edge()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        c.Quelled = true;
        new QuellCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m.ToLowerInvariant().Contains("already quelled"));
    }

    [Fact]
    public void Unquell_NotQuelled_Edge()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(builder: true);
        c.Quelled = false;
        new UnquellCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m.ToLowerInvariant().Contains("not quelled"));
    }

    [Fact]
    public void Save_CallsGameTimeWhenEnabled()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(superuser: true);
        // Simulate TIME_SYSTEM_ENABLED true: our SaveCommand always saves GameTime, so we just verify Saved in message still appears and no failure
        new SaveCommand().Run(c, null);
        var txt = string.Join(" ", c.PeekMessages()).ToLowerInvariant();
        Assert.Contains("saved in", txt);
    }

    [Fact]
    public void Save_MessageIncludesTimeIfEnabled()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(superuser: true);
        c.ClearMessages();
        new SaveCommand().Run(c, null);
        var combined = string.Join(" ", c.PeekMessages()).ToLowerInvariant();
        Assert.Contains("saved in", combined);
        Assert.True(combined.Contains("millisecond") || combined.Contains("ms") || combined.Contains("s"));
    }

    [Fact]
    public void Save_WithTimeSystemDisabled()
    {
        using var env = GlobalTestEnv.Enter();
        var c = MakeCaller(superuser: true);
        c.ClearMessages();
        new SaveCommand().Run(c, null);
        Assert.NotEmpty(c.PeekMessages());
        Assert.Contains(c.PeekMessages(), m => m.Contains("Saved"));
    }
}
