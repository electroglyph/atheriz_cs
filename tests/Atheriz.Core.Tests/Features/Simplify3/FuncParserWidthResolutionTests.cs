using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Width kwargs win over positional widths, which win over the 78 default;
// per-verb align/fillchar/suffix/indent handling is unchanged.
[Collection("Ported")]
public class FuncParserWidthResolutionTests
{
    private static FuncParser FullParser() => new(FuncParser.FuncParserCallables);

    [Fact]
    public void Pad_WidthKwarg_BeatsPositionalWidth()
    {
        var parser = FullParser();

        var result = parser.Parse("$pad(hi,4,width=8)")?.ToString();

        Assert.Equal("   hi   ", result);
    }

    [Fact]
    public void Pad_PositionalWidth_BeatsDefault()
    {
        var parser = FullParser();

        var result = parser.Parse("$pad(hi,6)")?.ToString();

        Assert.Equal("  hi  ", result);
        Assert.Equal(6, result!.Length);
    }

    [Fact]
    public void Pad_AlignAndFillchar_PerCallerUntouched()
    {
        var parser = FullParser();

        var left = parser.Parse("$pad(hi,6,l,*)")?.ToString();
        var right = parser.Parse("$pad(hi,6,align=r,fillchar=*)")?.ToString();

        Assert.Equal("hi****", left);
        Assert.Equal("****hi", right);
    }

    [Fact]
    public void Crop_WidthKwarg_BeatsPositionalWidth()
    {
        var parser = FullParser();

        var result = parser.Parse("$crop(hello world,4,width=8)")?.ToString();

        Assert.Equal("hel[...]", result);
    }

    [Fact]
    public void Crop_SuffixKwarg_BeatsPositionalSuffix()
    {
        var parser = FullParser();

        var result = parser.Parse("$crop(hello world,8,suffix=!!)")?.ToString();

        Assert.Equal("hello !!", result);
    }

    [Fact]
    public void Justify_WidthKwarg_BeatsPositionalWidth()
    {
        var parser = FullParser();

        var result = parser.Parse("$ljust(hi,4,width=6)")?.ToString();

        Assert.Equal("hi    ", result);
    }

    [Fact]
    public void Justify_DefAlign_PreservedPerVerb()
    {
        var parser = FullParser();

        var left = parser.Parse("$ljust(hi,6)")?.ToString();
        var right = parser.Parse("$rjust(hi,6)")?.ToString();
        var full = parser.Parse("$just(hi,6)")?.ToString();

        Assert.Equal("hi    ", left);
        Assert.Equal("    hi", right);
        Assert.Equal("hi", full);
    }

    [Fact]
    public void Justify_IndentKwarg_BeatsPositionalIndent()
    {
        var parser = FullParser();

        var result = parser.Parse("$ljust(hi,6,l,0,indent=2)")?.ToString();

        Assert.Equal("  hi    ", result);
    }
}
