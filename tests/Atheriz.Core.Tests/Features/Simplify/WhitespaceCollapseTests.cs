using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Span whitespace collapse (no regex, no pattern cache): same inputs produce
// the same outputs as the old three-regex pipeline, and distinct
// (maxLinebreaks, maxSpacing) caps do not cross-talk.
public class WhitespaceCollapseTests
{
    [Fact]
    public void CompressWhitespace_RepeatedCalls_Agree()
    {
        const string input = "a    b\n\n\nc";
        var first = GameUtils.CompressWhitespace(input, maxLinebreaks: 1, maxSpacing: 2);
        var second = GameUtils.CompressWhitespace(input, maxLinebreaks: 1, maxSpacing: 2);
        Assert.Equal(first, second);
        Assert.Equal("a  b\nc", first);
    }

    [Fact]
    public void CompressWhitespace_DistinctCaps_DoNotCrossTalk()
    {
        Assert.Equal("a  b", GameUtils.CompressWhitespace("a    b", maxSpacing: 2));
        Assert.Equal("a b", GameUtils.CompressWhitespace("a    b", maxSpacing: 1));
        Assert.Equal("a\nb", GameUtils.CompressWhitespace("a\n\n\nb", maxLinebreaks: 1));
    }

    // Defaults preserve a single blank line, and larger caps reach the
    // parameterized pass: the old \s* pre-pass capped every run at 2 breaks
    // first (maxLinebreaks >= 3 was dead) and the old default erased blanks.
    [Fact]
    public void CompressWhitespace_Default_PreservesSingleBlankLine()
    {
        Assert.Equal("a\n\nb", GameUtils.CompressWhitespace("a\n\nb"));
    }

    [Fact]
    public void CompressWhitespace_MaxLinebreaks3_PreservesThreeBreaks()
    {
        Assert.Equal("a\n\n\nb", GameUtils.CompressWhitespace("a\n\n\n\n\nb", maxLinebreaks: 3));
    }

    [Fact]
    public void CompressWhitespace_Quirks_Preserved()
    {
        // Leading spaces stay (the old `(?<=\S)` gate never collapsed them).
        Assert.Equal("  hello  world", GameUtils.CompressWhitespace("  hello   world  ", maxSpacing: 2));
        // Spaces around a break stay: the pre-break run is too short and the
        // post-break run has no non-whitespace predecessor.
        Assert.Equal("a \n b", GameUtils.CompressWhitespace("a \n\n b", maxLinebreaks: 1));
        // A zero cap disables that collapse (the old `{0,}` pattern no-opped).
        Assert.Equal("a    b", GameUtils.CompressWhitespace("a    b", maxSpacing: 0));
        Assert.Equal("a\n\n\nb", GameUtils.CompressWhitespace("a\n\n\nb", maxLinebreaks: 0));
        // Blank gaps holding tabs collapse like blank gaps holding spaces.
        Assert.Equal("a\nb", GameUtils.CompressWhitespace("a\n \t\nb", maxLinebreaks: 1));
        Assert.Equal("", GameUtils.CompressWhitespace(null!));
    }

    [Fact]
    public void CompressWhitespace_HasNoPatternCache()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "GameUtils.cs");
        Assert.DoesNotContain("CompressPatternCache", src);
        Assert.DoesNotContain("GetCompressPatterns", src);
    }

    [Fact]
    public void GameUtils_HasNoRuntimeRegex()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "GameUtils.cs");
        Assert.DoesNotContain("new Regex(", src);
        Assert.Contains("[GeneratedRegex(", src);
    }

    [Fact]
    public void IterToString_SinglePass_MatchesJoinTruncation()
    {
        Assert.Equal("", GameUtils.IterToString(null));
        Assert.Equal("", GameUtils.IterToString(Array.Empty<object?>()));
        Assert.Equal("solo", GameUtils.IterToString(new object?[] { "solo" }));
        Assert.Equal("1 and 2", GameUtils.IterToString(new object?[] { 1, 2 }, sep: " and ", endsep: " and "));
        Assert.Equal("1, 2, and 3", GameUtils.IterToString(new object?[] { 1, 2, 3 }, sep: ",", endsep: ", and "));
        Assert.Equal("\"a\" and \"b\"", GameUtils.IterToString(new object?[] { "a", "b" }, sep: ",", endsep: ", and ", addQuote: true));
    }
}
