using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins for the adapter re-entrancy guard: a command overriding only one of
// RunParsed/RunRaw and receiving the other input shape must get help, not a
// stack overflow (adapter -> context -> default -> adapter ping-pong).
[Collection("Ported")]
public sealed class AdapterReentryPinsTests
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

    // Legacy shape: overrides only the untyped entry, like the 51
    // test doubles across the suite. The context entry must bridge back
    // to it on first entry (LagGateCommand relies on this path).
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
