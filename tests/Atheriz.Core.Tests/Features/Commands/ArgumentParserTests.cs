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
}
