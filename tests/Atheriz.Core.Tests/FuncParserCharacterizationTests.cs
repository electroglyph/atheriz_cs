using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests;

// Stage 2a characterization pins: exact observable behavior of the char-loop
// scanner (ParseInternal), captured BEFORE the recursive-descent rewrite.
// Every expectation below was hand-traced through the current code and then
// verified green; the 2b rewrite must reproduce all of them byte-for-byte.
[Collection("Ported")]
public class FuncParserCharacterizationTests
{
    private static FuncParser EmptyParser() => new FuncParser();
    private static FuncParser FullParser() => new FuncParser(FuncParser.FuncParserCallables);

    [Fact]
    public void UnknownFunc_EchoesVerbatim()
    {
        Assert.Equal("$foo(bar)", EmptyParser().Parse("$foo(bar)"));
    }

    [Fact]
    public void UnknownNestedFunc_EchoPreservesInnerEchoVerbatim()
    {
        // Inner $bar(x) echoes as "$bar(x)"; the outer ( never accumulates
        // into InFuncStr (cleared when FuncName is taken), so the outer echo
        // is rebuilt as "$foo(" + inner-result + ")".
        Assert.Equal("$foo($bar(x))", EmptyParser().Parse("$foo($bar(x))"));
    }

    [Fact]
    public void UnknownOuter_KnownInner_SplicesExecutedValue()
    {
        // $add(1,2) executes to "3" inside-out; the unknown outer echo shows it.
        Assert.Equal("$foo(3)", FullParser().Parse("$foo($add(1,2))"));
    }

    [Fact]
    public void DollarDollar_EscapesToLiteralDollar()
    {
        Assert.Equal("$foo", EmptyParser().Parse("$$foo"));
    }

    [Fact]
    public void Backslash_EscapesStartChar()
    {
        Assert.Equal("$foo()", EmptyParser().Parse("\\$foo()"));
    }

    [Fact]
    public void UnterminatedFunc_PartialEcho()
    {
        Assert.Equal("$foo(bar", EmptyParser().Parse("$foo(bar"));
    }

    [Fact]
    public void QuotedArg_QuotesStrippedInEcho()
    {
        Assert.Equal("$foo(bar baz)", EmptyParser().Parse("$foo(\"bar baz\")"));
    }

    [Fact]
    public void StripMode_ReturnsEmptyForTopLevelCall()
    {
        Assert.Equal("", EmptyParser().Parse("$foo(bar)", false, false, true));
    }

    [Fact]
    public void EscapeMode_PrefixesEchoWithBackslash()
    {
        Assert.Equal("\\$foo(bar)", EmptyParser().Parse("$foo(bar)", false, true));
    }

    [Fact]
    public void PureCall_ReturnStrFalse_ReturnsRawValue()
    {
        var result = FullParser().ParseToAny("$toint(4)");
        Assert.IsType<int>(result);
        Assert.Equal(4, result);
    }

    [Fact]
    public void PureCall_ReturnStrTrue_ReturnsString()
    {
        Assert.Equal("4", FullParser().Parse("$toint(4)"));
    }

    [Fact]
    public void MixedContent_ReturnStrFalse_FallsBackToString()
    {
        // Literal text outside the call flips to string mode even for ParseToAny.
        var result = FullParser().ParseToAny("x$toint(4)");
        Assert.IsType<string>(result);
        Assert.Equal("x4", result);
    }

    [Fact]
    public void MaxNesting_RaisesWithDepthInMessage()
    {
        var deep = string.Concat(System.Linq.Enumerable.Repeat("$a(", 21)) + "x" + new string(')', 21);
        var ex = Assert.Throws<FuncParser.ParsingError>(() => EmptyParser().Parse(deep, true));
        Assert.Equal("Only allows for parsing nesting function defs to a max depth of 20.", ex.Message);
    }

    [Fact]
    public void MaxNesting_WithoutRaiseErrors_TreatsDollarLiteral()
    {
        // Past the depth guard the inner $ is appended literally, not opened.
        var deep = string.Concat(System.Linq.Enumerable.Repeat("$a(", 21)) + "x" + new string(')', 21);
        var result = EmptyParser().Parse(deep, false);
        Assert.IsType<string>(result);
        Assert.Contains("$a(", (string)result!);
    }

    [Fact]
    public void EmptyArg_Skipped_ButQuotedEmpty_Kept()
    {
        var spy = new FuncParser(new Dictionary<string, FuncParser.ParserCallable>
        {
            ["spy"] = (a, k, ctx, raw) => a.Length.ToString(),
        });
        Assert.Equal("0", spy.Parse("$spy()"));
        Assert.Equal("1", spy.Parse("$spy(\"\")"));
    }

    [Fact]
    public void KwargFlow_UnquotedEmpty_AndQuotedEmpty_BothSet()
    {
        // The spy sees the callable kwargs dict, which always carries the two
        // reserved merges (funcparser, raise_errors) on top of parsed kwargs.
        var spy = new FuncParser(new Dictionary<string, FuncParser.ParserCallable>
        {
            ["spy"] = (a, k, ctx, raw) => k.Count + ":" + (k.TryGetValue("a", out var v) ? v : "<missing>"),
        });
        Assert.Equal("3:", spy.Parse("$spy(a=)"));
        Assert.Equal("3:", spy.Parse("$spy(a=\"\")"));
        Assert.Equal("$foo(a=1)", EmptyParser().Parse("$foo(a=1)"));
    }
}
