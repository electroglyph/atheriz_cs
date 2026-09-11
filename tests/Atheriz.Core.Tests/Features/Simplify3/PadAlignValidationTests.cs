// Pins for the Pad align switch (FuncParserHelpers.cs): only c/l/r are
// accepted with a center fallback, using ordinal comparison, through both the
// helper and the $pad engine path.
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class PadAlignValidationTests
{
    [Fact]
    public void Pad_CenterAlign_PadsBothSides()
    {
        Assert.Equal("  hi  ", FuncParserHelpers.Pad("hi", 6, "c"));
    }

    [Fact]
    public void Pad_LeftAlign_PadsRight()
    {
        Assert.Equal("hi    ", FuncParserHelpers.Pad("hi", 6, "l"));
    }

    [Fact]
    public void Pad_RightAlign_PadsLeft()
    {
        Assert.Equal("    hi", FuncParserHelpers.Pad("hi", 6, "r"));
    }

    [Fact]
    public void Pad_BogusAlign_FallsBackToCenter()
    {
        Assert.Equal("  hi  ", FuncParserHelpers.Pad("hi", 6, "bogus"));
    }

    [Fact]
    public void Pad_UppercaseAlign_FallsBackToCenter()
    {
        Assert.Equal("  hi  ", FuncParserHelpers.Pad("hi", 6, "C"));
    }

    [Fact]
    public void Pad_NullAlign_FallsBackToCenter()
    {
        Assert.Equal("  hi  ", FuncParserHelpers.Pad("hi", 6, null!));
    }

    [Fact]
    public void Pad_AlignViaParser_BogusMatchesCenter()
    {
        using var env = GlobalTestEnv.Enter();
        var parser = new FuncParser(FuncParser.FuncParserCallables);

        Assert.Equal("  hi  ", parser.Parse("$pad(hi,6,bogus)")?.ToString());
        Assert.Equal("  hi  ", parser.Parse("$pad(hi,6,c)")?.ToString());
        Assert.Equal("hi    ", parser.Parse("$pad(hi,6,l)")?.ToString());
        Assert.Equal("    hi", parser.Parse("$pad(hi,6,r)")?.ToString());
    }
}
