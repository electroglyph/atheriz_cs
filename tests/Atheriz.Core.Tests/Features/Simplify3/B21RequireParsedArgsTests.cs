// Pins for simplify3 §4 Null-ParsedArgs entry: the shared RequireParsedArgs
// guard sends the same PrintHelp output the fourteen hand-rolled preambles
// sent for null and non-ParsedArgs input. Chunk A covers ban/unban, build,
// channel, create; chunk B covers delete, desc, door, emote, get; chunk C
// covers give, say, set, unset. Every fact resolves its job through the real
// logged-in dispatcher, then re-invokes that job with null and raw-string
// args so the guard (not the parser) produces the help text.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B21RequireParsedArgsTests
{
    private static GameObject EnterBuilder(string area, string name)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        var builder = GameObject.Create(name, isPc: true, privilege: Privilege.Builder);
        ObjectRegistry.AddObject(builder);
        Assert.True(builder.MoveTo(node));
        builder.ClearMessages();
        return builder;
    }

    private static void AssertHelpOnNullAndRaw(GameObject caller, string dispatchText, string expectedHelp)
    {
        var job = CommandDispatcher.DispatchLoggedIn(caller, dispatchText, immediate: true);
        Assert.NotNull(job);
        caller.ClearMessages();

        job!.Func(job.Caller, null);
        Assert.Contains(expectedHelp, caller.PeekMessages());

        caller.ClearMessages();
        job.Func(job.Caller, "some raw string");
        Assert.Contains(expectedHelp, caller.PeekMessages());
    }

    // ----- Chunk A: ban/unban, build, channel, create -----

    [Fact]
    public void BanCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21ban", "b21_ban_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "ban bob", new BanCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void UnbanCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21unban", "b21_unban_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "unban bob", new UnbanCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void BuildCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21build", "b21_build_builder");
        try
        {
            var expected = new BuildCommand().PrintHelp();
            AssertHelpOnNullAndRaw(builder, "build -n --room", expected);
            // The old code had a separate non-ParsedArgs else arm: any
            // non-parsed payload (not just strings) must also print help.
            builder.ClearMessages();
            new BuildCommand().Run(builder, new object());
            Assert.Contains(expected, builder.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void ChannelCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21channel", "b21_channel_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "channel -l", new ChannelCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void CreateCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21create", "b21_create_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "create myobj", new CreateCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // ----- Chunk B: delete, desc, door, emote, get -----

    [Fact]
    public void DeleteCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21delete", "b21_delete_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "delete foo", new DeleteCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void DescCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21desc", "b21_desc_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "desc hello", new DescCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void DoorCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21door", "b21_door_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "door -n", new DoorCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void EmoteCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21emote", "b21_emote_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "emote waves", new EmoteCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void GetCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21get", "b21_get_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "get foo", new GetCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // ----- Chunk C: give, say, set, unset -----

    [Fact]
    public void GiveCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21give", "b21_give_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "give foo to bar", new GiveCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void SayCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21say", "b21_say_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "say hello", new SayCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void SetCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21set", "b21_set_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "set me name value", new SetCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void UnsetCommand_NullOrRawArgs_SendsPrintHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21unset", "b21_unset_builder");
        try
        {
            AssertHelpOnNullAndRaw(builder, "unset me name", new UnsetCommand().PrintHelp());
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void RequireParsedArgs_ValidParsedArgs_ReturnsTrueWithoutMessaging()
    {
        using var env = GlobalTestEnv.Enter();
        var builder = EnterBuilder("b21guard", "b21_guard_builder");
        try
        {
            var cmd = new SayCommand();
            var pa = new GameArgumentParser.ParsedArgs();
            builder.ClearMessages();
            Assert.True(cmd.RequireParsedArgs(builder, pa, out var outPa));
            Assert.NotNull(outPa);
            Assert.Empty(builder.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
