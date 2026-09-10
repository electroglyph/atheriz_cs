using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Cached compiled patterns + single-pass stringify: same inputs produce the
// same outputs, and distinct (maxLinebreaks, maxSpacing) keys do not cross-talk.
public class StringHelperCacheTests
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
    public void CompressWhitespace_DistinctKeys_DoNotCrossTalk()
    {
        Assert.Equal("a  b", GameUtils.CompressWhitespace("a    b", maxSpacing: 2));
        Assert.Equal("a b", GameUtils.CompressWhitespace("a    b", maxSpacing: 1));
        Assert.Equal("a\nb", GameUtils.CompressWhitespace("a\n\n\nb", maxLinebreaks: 1));
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
