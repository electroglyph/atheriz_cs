using Atheriz.Core.Commands;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The Execute lag-gate wrapper is shared by the no-parser early return and
// the parsed-args tail: a lagged caller runs nothing on either path, a
// non-lagged caller runs on both, and no gate means run-as-is.
[Collection("Ported")]
public sealed class LagGateWrapperTests
{
    private sealed class ParserCmd : Command
    {
        public bool Ran;
        public override string Key => "laggateparsed";
        public override string Desc => "lag gate pin";
        protected override void SetupParser(GameArgumentParser p) => p.AddArgument("target", nargs: "?", help: "t");
        public override void Run(IMessageTarget caller, object? args) => Ran = true;
    }

    private sealed class RawCmd : Command
    {
        public bool Ran;
        public override string Key => "laggateraw";
        public override string Desc => "lag gate pin";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) => Ran = true;
    }

    [Fact]
    public void Execute_LaggedCaller_RunsNothing_OnParsedPath()
    {
        var cmd = new ParserCmd();
        var puppet = new GameObject { Name = "Hero" };
        Command.GlobalLagCheck = _ => true;
        try
        {
            var (func, caller, args) = cmd.Execute(puppet, "");
            Assert.NotNull(func);
            func!(caller!, args);
            Assert.False(cmd.Ran);
        }
        finally { Command.GlobalLagCheck = null; }
    }

    [Fact]
    public void Execute_LaggedCaller_RunsNothing_OnRawPath()
    {
        var cmd = new RawCmd();
        var puppet = new GameObject { Name = "Hero" };
        Command.GlobalLagCheck = _ => true;
        try
        {
            var (func, caller, args) = cmd.Execute(puppet, "hello");
            Assert.NotNull(func);
            func!(caller!, args);
            Assert.False(cmd.Ran);
        }
        finally { Command.GlobalLagCheck = null; }
    }

    [Fact]
    public void Execute_NonLaggedCaller_Runs_OnBothPaths()
    {
        var parsed = new ParserCmd();
        var raw = new RawCmd();
        var puppet = new GameObject { Name = "Hero" };
        Command.GlobalLagCheck = _ => false;
        try
        {
            var (f1, c1, a1) = parsed.Execute(puppet, "");
            f1!(c1!, a1);
            var (f2, c2, a2) = raw.Execute(puppet, "hello");
            f2!(c2!, a2);
            Assert.True(parsed.Ran);
            Assert.True(raw.Ran);
        }
        finally { Command.GlobalLagCheck = null; }
    }

    [Fact]
    public void Execute_NoGate_Runs_OnBothPaths()
    {
        var parsed = new ParserCmd();
        var raw = new RawCmd();
        var puppet = new GameObject { Name = "Hero" };
        Command.GlobalLagCheck = null;
        var (f1, c1, a1) = parsed.Execute(puppet, "");
        f1!(c1!, a1);
        var (f2, c2, a2) = raw.Execute(puppet, "hello");
        f2!(c2!, a2);
        Assert.True(parsed.Ran);
        Assert.True(raw.Ran);
    }
}
