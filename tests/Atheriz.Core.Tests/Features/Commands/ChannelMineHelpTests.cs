using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// `channel -m` subscriptions listing + the clearer usage construction
// (flags named in the usage line) + example coverage on the quirky commands.
[Collection("Ported")]
public sealed class ChannelMineHelpTests
{
    private static GameObject MakePc(string name)
    {
        var go = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(go);
        go.IsConnected = true;
        go.ClearMessages();
        return go;
    }

    private static GameArgumentParser.ParsedArgs MineArgs(
        string? channel = null, bool mine = true, bool list = false,
        bool subscribe = false, bool unsubscribe = false, bool replay = false,
        List<string>? message = null)
    {
        var pa = new GameArgumentParser.ParsedArgs();
        pa["channel"] = channel;
        pa["list"] = list;
        pa["mine"] = mine;
        pa["unsubscribe"] = unsubscribe;
        pa["subscribe"] = subscribe;
        pa["replay"] = replay;
        pa["message"] = message ?? [];
        return pa;
    }

    [Fact]
    public void Usage_NamesFlagsAndValuePlaceholders()
    {
        var p = new GameArgumentParser("t");
        p.AddArgument("-c", "--channel").Help("Channel to target");
        p.AddArgument("-s", "--subscribe").Help("Subscribe").Action(GameArgumentParser.ArgAction.StoreTrue);
        var usage = p.FormatHelp().Split('\n')[0];
        Assert.Contains("[-c, --channel CHANNEL]", usage);
        Assert.Contains("[-s, --subscribe]", usage);
    }

    [Fact]
    public void Usage_RequiredFlag_DropsBrackets()
    {
        var p = new GameArgumentParser("t");
        p.AddArgument("--force", action: "store_true", required: true);
        var usage = p.FormatHelp().Split('\n')[0];
        Assert.Contains("--force", usage);
        Assert.DoesNotContain("[--force", usage);
    }

    [Fact]
    public void ChannelAction_MineBeatsSubscribeButLosesToList()
    {
        var p = new GameArgumentParser();
        ChannelActionParser.AddCommonArgs(p);
        p.AddArgument("-l", "--list").Help("x").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-m", "--mine").Help("x").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-s", "--subscribe").Help("x").Action(GameArgumentParser.ArgAction.StoreTrue);
        Assert.Equal(ChannelAction.Mine, ChannelActionParser.Parse(p.ParseArgs(["-m", "-s"])));
        Assert.Equal(ChannelAction.List, ChannelActionParser.Parse(p.ParseArgs(["-l", "-m"])));
    }

    [Fact]
    public void Mine_ListsSubscribedChannels()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var ch1 = Channel.Create("mine_alpha");
            var ch2 = Channel.Create("mine_beta");
            var caller = MakePc("mine_lister");
            caller.Subscribe(ch1);
            caller.Subscribe(ch2);
            caller.ClearMessages();
            new ChannelCommand().Run(caller, MineArgs());
            var text = string.Join("\n", caller.PeekMessages());
            Assert.Contains("2 subscribed channels", text);
            Assert.Contains("mine_alpha", text);
            Assert.Contains("mine_beta", text);
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void Mine_WhenNoneSubscribed_ReportsNone()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            Channel.Create("mine_other");
            var caller = MakePc("mine_lonely");
            new ChannelCommand().Run(caller, MineArgs());
            Assert.Contains("You are not subscribed to any channels.", caller.PeekMessages());
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void Mine_ExcludesUnsubscribedChannels()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var ch1 = Channel.Create("mine_in");
            Channel.Create("mine_out");
            var caller = MakePc("mine_picker");
            caller.Subscribe(ch1);
            caller.ClearMessages();
            new ChannelCommand().Run(caller, MineArgs());
            var text = string.Join("\n", caller.PeekMessages());
            Assert.Contains("mine_in", text);
            Assert.DoesNotContain("mine_out", text);
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void Mine_BeatsSubscribe_DoesNotSubscribe()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var chan = Channel.Create("mine_vs_sub");
            var caller = MakePc("mine_vs_caller");
            new ChannelCommand().Run(caller, MineArgs("mine_vs_sub", subscribe: true));
            var text = string.Join("\n", caller.PeekMessages());
            Assert.Contains("You are not subscribed to any channels.", text);
            Assert.DoesNotContain(chan.Id, caller.ChannelsSnapshot);
        }
        finally { ChannelCommand.ClearCache(); }
    }

    [Fact]
    public void Mine_AfterUnsubscribe_DropsChannel()
    {
        using var env = GlobalTestEnv.Enter();
        ChannelCommand.ClearCache();
        try
        {
            var chan = Channel.Create("mine_gone");
            var caller = MakePc("mine_leaver");
            caller.Subscribe(chan);
            caller.Unsubscribe(chan);
            caller.ClearMessages();
            new ChannelCommand().Run(caller, MineArgs());
            Assert.Contains("You are not subscribed to any channels.", caller.PeekMessages());
        }
        finally { ChannelCommand.ClearCache(); }
    }

    public static IEnumerable<object[]> QuirkyCommands() =>
    [
        [typeof(ChannelCommand)],
        [typeof(BaseChannelCommand)],
        [typeof(GiveCommand)],
        [typeof(GetCommand)],
        [typeof(PutCommand)],
        [typeof(BanCommand)],
        [typeof(UnbanCommand)],
        [typeof(CreateCommand)],
        [typeof(SetCommand)],
        [typeof(UnsetCommand)],
        [typeof(MoveCommand)],
        [typeof(NounCommand)],
        [typeof(DoorCommand)],
        [typeof(BuildCommand)],
        [typeof(YellCommand)],
        [typeof(SocialsCommand)],
        [typeof(SaveCommand)],
        [typeof(CreateAccountCommand)],
        [typeof(NewCharacterCommand)],
    ];

    [Theory]
    [MemberData(nameof(QuirkyCommands))]
    public void QuirkyCommand_Help_ShowsExample(Type commandType)
    {
        var cmd = (Command)Activator.CreateInstance(commandType)!;
        Assert.Contains("Use '", cmd.PrintHelp());
    }
}
