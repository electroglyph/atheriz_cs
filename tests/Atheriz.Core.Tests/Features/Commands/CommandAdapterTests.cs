using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// The untyped Run(caller, args) adapter splits parsed input to RunParsed
// and raw strings to RunRaw; single-override commands receiving the other
// shape get help instead of recursing, and legacy Run-only overrides are
// still reached through the context entry.
[Collection("Ported")]
public sealed class CommandAdapterTests
{
    private sealed class ParsedOnlyCommand : Command
    {
        public override string Key => "parsedonly";
        public bool SawParsed;
        public override void RunParsed(CommandContext ctx) => SawParsed = true;
    }

    private sealed class RawOnlyCommand : Command
    {
        public override string Key => "rawonly";
        public bool SawRaw;
        public override void RunRaw(CommandContext ctx) => SawRaw = true;
    }

    private sealed class LegacyCommand : Command
    {
        public override string Key => "legacy";
        public bool SawLegacy;
        public override void Run(IMessageTarget caller, object? args) => SawLegacy = true;
    }

    private sealed class FakeCaller : IMessageTarget
    {
        public readonly List<string> Msgs = [];
        public void Msg(string text) => Msgs.Add(text);
    }

    private sealed class StrictCommand : Command
    {
        public override string Key => "strict";
        public bool SawParsed;
        public bool SawRaw;
        public override void RunParsed(CommandContext ctx) => SawParsed = true;
        public override void RunRaw(CommandContext ctx) => SawRaw = true;
    }

    [Fact]
    public void CommandAdapter_ParsedArgs_ReachesRunParsed()
    {
        var cmd = new StrictCommand();
        cmd.Run(new FakeCaller(), new GameArgumentParser.ParsedArgs());
        Assert.True(cmd.SawParsed);
        Assert.False(cmd.SawRaw);
    }

    [Fact]
    public void CommandAdapter_RawString_ReachesRunRaw()
    {
        var cmd = new StrictCommand();
        cmd.Run(new FakeCaller(), "raw text");
        Assert.True(cmd.SawRaw);
        Assert.False(cmd.SawParsed);
    }

    [Fact]
    public void ParsedOnlyCommand_RawString_SendsHelpAndReturns()
    {
        var cmd = new ParsedOnlyCommand();
        var caller = new FakeCaller();
        cmd.Run(caller, "raw text");
        Assert.False(cmd.SawParsed);
        Assert.Single(caller.Msgs);
        Assert.Equal(cmd.PrintHelp(), caller.Msgs[0]);
    }

    [Fact]
    public void RawOnlyCommand_ParsedArgs_SendsHelpAndReturns()
    {
        var cmd = new RawOnlyCommand();
        var caller = new FakeCaller();
        cmd.Run(caller, new GameArgumentParser.ParsedArgs());
        Assert.False(cmd.SawRaw);
        Assert.Single(caller.Msgs);
        Assert.Equal(cmd.PrintHelp(), caller.Msgs[0]);
    }

    [Fact]
    public void LegacyOverride_ContextEntry_BridgesToUntypedRun()
    {
        var cmd = new LegacyCommand();
        var caller = new FakeCaller();
        cmd.Run(new CommandContext(caller, null, new GameArgumentParser.ParsedArgs(), "", CancellationToken.None));
        Assert.True(cmd.SawLegacy);
        Assert.Empty(caller.Msgs);
    }

    [Fact]
    public void SingleOverride_MatchingShape_StillReachesOverride()
    {
        var parsed = new ParsedOnlyCommand();
        var parsedCaller = new FakeCaller();
        parsed.Run(parsedCaller, new GameArgumentParser.ParsedArgs());
        Assert.True(parsed.SawParsed);
        Assert.Empty(parsedCaller.Msgs);

        var raw = new RawOnlyCommand();
        var rawCaller = new FakeCaller();
        raw.Run(rawCaller, "raw text");
        Assert.True(raw.SawRaw);
        Assert.Empty(rawCaller.Msgs);
    }
}
