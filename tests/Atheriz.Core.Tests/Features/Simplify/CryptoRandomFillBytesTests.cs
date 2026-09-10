using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared FillBytes core: UrlSafeToken + HexToken keep identical lengths,
// alphabets, and non-positive guard behavior.
public class CryptoRandomFillBytesTests
{
    [Fact]
    public void UrlSafeToken_DefaultLength_MatchesPython32Bytes()
    {
        Assert.Equal(43, CryptoRandom.UrlSafeToken().Length);
    }

    [Fact]
    public void HexToken_DefaultLength_MatchesPython32Bytes()
    {
        Assert.Equal(64, CryptoRandom.HexToken().Length);
    }

    [Fact]
    public void Tokens_VaryPerCall_AndStayInAlphabet()
    {
        var a = CryptoRandom.UrlSafeToken();
        var b = CryptoRandom.UrlSafeToken();
        Assert.NotEqual(a, b);
        Assert.Matches(@"^[A-Za-z0-9\-_]+$", a);
        Assert.Matches(@"^[0-9a-f]+$", CryptoRandom.HexToken());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TokenHelpers_RejectNonPositiveByteCounts(int bytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.UrlSafeToken(bytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.HexToken(bytes));
    }
}
