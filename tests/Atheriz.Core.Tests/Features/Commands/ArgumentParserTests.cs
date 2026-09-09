using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// Bad int input must raise instead of silently keeping the raw string
// (GameArgumentParser.cs:239-244 options, :296-297 positionals).
[Collection("Ported")]
public class ArgumentParserTests
{
    [Fact]
    public void Parser_InvalidIntPositional_ThrowsCommandError()
    {
        // A non-numeric positional for an int argument must be an error.
        var p = new GameArgumentParser("prog");
        p.AddArgument("count", type: typeof(int));
        Assert.Throws<CommandError>(() => p.ParseArgs(["abc"]));
    }

    [Fact]
    public void Parser_InvalidIntOption_ThrowsCommandError()
    {
        // Same rule applies to int-valued options.
        var p = new GameArgumentParser("prog");
        p.AddArgument("--count").Type(typeof(int));
        Assert.Throws<CommandError>(() => p.ParseArgs(["--count", "abc"]));
    }

    [Fact]
    public void Parser_Remainder_SwallowsDashFirstToken()
    {
        // REMAINDER consumes everything remaining, including a leading dash
        // token (owner decision 2026-09-08 — `say --help` must speak, not throw).
        var p = new GameArgumentParser("say");
        p.AddArgument("text").Nargs("REMAINDER");
        var pa = p.ParseArgs(["--help"]);
        Assert.Equal(["--help"], pa.GetList("text"));
    }

    [Fact]
    public void Parser_Remainder_SwallowsLaterDashTokens()
    {
        var p = new GameArgumentParser("say");
        p.AddArgument("text").Nargs("REMAINDER");
        var pa = p.ParseArgs(["hello", "--help"]);
        Assert.Equal(["hello", "--help"], pa.GetList("text"));
    }

    [Fact]
    public void Parser_NoRemainder_DashStillThrows()
    {
        // Without a REMAINDER positional, unknown dash tokens stay errors.
        var p = new GameArgumentParser("prog");
        p.AddArgument("name");
        Assert.Throws<CommandError>(() => p.ParseArgs(["--help"]));
    }
}
