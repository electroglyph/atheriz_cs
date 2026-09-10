using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// The hand scans reproduce the regex edge cases exactly: negative numbers
// ("-5", "-5.", "-.5") parse as positional values, "--" stays an unknown
// option, and backslash escapes (including a trailing backslash) survive the
// pre-scan byte-for-byte.
[Collection("Ported")]
public sealed class ParserCharacterScanTests
{
    private static GameArgumentParser SinglePositional()
    {
        var p = new GameArgumentParser();
        p.AddArgument("value", help: "v");
        return p;
    }

    [Fact]
    public void NegativeIntegers_ParseAsValues()
    {
        Assert.Equal("-5", SinglePositional().ParseArgs(["-5"]).GetString("value"));
        Assert.Equal("-5.", SinglePositional().ParseArgs(["-5."]).GetString("value"));
        Assert.Equal("-.5", SinglePositional().ParseArgs(["-.5"]).GetString("value"));
        Assert.Equal("-5.5", SinglePositional().ParseArgs(["-5.5"]).GetString("value"));
    }

    [Fact]
    public void DoubleDash_IsNotANegativeNumber()
    {
        Assert.Throws<CommandError>(() => SinglePositional().ParseArgs(["--"]));
        Assert.Equal("-", SinglePositional().ParseArgs(["-"]).GetString("value"));
    }

    [Fact]
    public void SplitArgs_EscapedDash_StaysLiteral()
    {
        Assert.Equal([@"\-"], Command.SplitArgs(@"\-"));
    }

    [Fact]
    public void SplitArgs_TrailingBackslash_Survives()
    {
        Assert.Equal([@"foo\"], Command.SplitArgs(@"foo\"));
    }

    [Fact]
    public void SplitArgs_QuotedEscape_Unchanged()
    {
        Assert.Equal(["'"], Command.SplitArgs(@"\'"));
        Assert.Equal(["a b"], Command.SplitArgs("\"a b\""));
    }
}
