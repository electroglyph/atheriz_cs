using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Two-row Levenshtein DP + MinBy best match: identical distances (including
// argument symmetry from the shorter-row swap) and first-minimum ties.
public class StringDistanceTwoRowTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("look", "look", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("look", "lock", 1)]
    [InlineData("", "abc", 3)]
    public void Levenshtein_MatchesFullTableDistances(string a, string b, int expected)
    {
        Assert.Equal(expected, StringDistance.Levenshtein(a, b));
        Assert.Equal(expected, StringDistance.Levenshtein(b, a));
    }

    [Fact]
    public void Levenshtein_OversizedInput_ComparesTruncatedPrefix()
    {
        // Over-length inputs compare on their capped prefixes: the old
        // content-blind constant returned Max(length) for every over-length
        // pair, collapsing them all to the same value.
        var big = new string('x', StringDistance.MaxInputLength + 1);
        Assert.Equal(StringDistance.MaxInputLength, StringDistance.Levenshtein(big, "y"));
    }

    [Fact]
    public void Levenshtein_OversizedInput_StillOrdersByContent()
    {
        // Equal-length over-cap inputs all collapsed to the same constant,
        // so ordering (and BestMatch) degraded to first-candidate-wins.
        var query = "abcdef" + new string('q', 1100);
        var near = "abcdef" + new string('q', 1100);
        var far = new string('f', query.Length);
        Assert.True(StringDistance.Levenshtein(query, near) < StringDistance.Levenshtein(query, far));
    }

    [Fact]
    public void BestMatch_OversizedInput_PicksClosestContent()
    {
        var query = "abcdef" + new string('q', 1100);
        var near = "abcdef" + new string('q', 1100);
        var far = new string('f', query.Length);
        Assert.Equal(near, StringDistance.BestMatch(query, new[] { far, near }));
    }

    [Fact]
    public void BestMatch_ReturnsFirstMinimum_OnTie()
    {
        Assert.Equal("ac", StringDistance.BestMatch("ab", new[] { "ac", "ad", "zzz" }));
    }

    [Fact]
    public void BestMatch_EmptyCandidates_ReturnsNull()
    {
        Assert.Null(StringDistance.BestMatch("lok", Array.Empty<string>()));
    }

    [Fact]
    public void Distance_NullInputs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StringDistance.Levenshtein(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => StringDistance.Levenshtein("a", null!));
        Assert.Throws<ArgumentNullException>(() => StringDistance.BestMatch(null!, new[] { "a" }));
        Assert.Throws<ArgumentNullException>(() => StringDistance.BestMatch("a", null!));
    }
}
