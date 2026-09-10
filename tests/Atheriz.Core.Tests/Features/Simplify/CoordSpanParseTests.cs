namespace Atheriz.Core.Tests.Features.Simplify;

// Span-based Coord parse: same accepted input set and false-never-throw
// contract as the split-based parse (no coord-widening).
[Collection("Ported")]
public class CoordSpanParseTests
{
    private static Coord Parse(string s)
    {
        Assert.True(Coord.TryParse(s, out var c), s);
        return c;
    }

    [Fact]
    public void ParenInAreaName_UsesLastOpenParen()
    {
        Assert.Equal(new Coord("My (old) Area", 1, 2, 3), Parse("My (old) Area(1,2,3)"));
    }

    [Fact]
    public void SearchForm_FourParts()
    {
        Assert.Equal(new Coord("Area", 1, 2, 3), Parse("(Area,1,2,3)"));
        Assert.Equal(new Coord("limbo", 4, 4, 4), Parse("(limbo,4,4,4)"));
    }

    [Fact]
    public void WhitespaceForm_FourTokens()
    {
        Assert.Equal(new Coord("Area", 1, 2, 3), Parse("Area 1 2 3"));
        Assert.Equal(new Coord("limbo", 4, 4, 4), Parse("  limbo\t4  4\t4 "));
    }

    [Fact]
    public void SignedAndSpaced_Numerics_Accepted()
    {
        Assert.Equal(new Coord("a", -1, +2, 3), Parse("a(-1, +2,3)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("limbo")]
    [InlineData("Area(1,2)")]
    [InlineData("Area(1,2,3,4)")]
    [InlineData("(Area,1,2)")]
    [InlineData("(Area,1,2,3,4)")]
    [InlineData("Area(a,b,c)")]
    [InlineData("Area 1 2")]
    [InlineData("Area 1 2 3 4")]
    [InlineData("Area 1 two 3")]
    [InlineData("(,1,2,3)")]
    [InlineData("()")]
    [InlineData("(Area,1,2,3")]
    [InlineData("Area)1,2,3(")]
    public void Malformed_ReturnsFalse(string? s)
    {
        Assert.False(Coord.TryParse(s, out var c));
        Assert.Equal(string.Empty, c.Area);
    }
}
