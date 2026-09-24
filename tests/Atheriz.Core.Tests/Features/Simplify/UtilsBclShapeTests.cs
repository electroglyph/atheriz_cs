using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Utils on BCL primitives: hand-rolled base64url/hex, per-call DP rows, the
// verbs output-copy shadow, and the one-method-per-chmod API go through
// framework calls instead.
public class UtilsBclShapeTests
{
    [Fact]
    public void CryptoRandom_UsesBclEncoders()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "CryptoRandom.cs");
        Assert.Contains("Base64Url.EncodeToString", src);
        Assert.Contains("Convert.ToHexStringLower", src);
        Assert.DoesNotContain(".Replace('+', '-')", src);
    }

    [Fact]
    public void CryptoRandom_BclOutput_MatchesContract()
    {
        // Same contract as CryptoRandomFillBytesTests pins: 32 bytes ->
        // 43 unpadded urlsafe chars / 64 lowercase hex chars.
        var url = CryptoRandom.UrlSafeToken();
        Assert.Equal(43, url.Length);
        Assert.Matches(@"^[A-Za-z0-9\-_]+$", url);
        Assert.DoesNotContain("=", url);
        var hex = CryptoRandom.HexToken();
        Assert.Equal(64, hex.Length);
        Assert.Matches(@"^[0-9a-f]+$", hex);
    }

    [Fact]
    public void StringDistance_RentsRowsInsteadOfAllocating()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "StringDistance.cs");
        Assert.Contains("ArrayPool<int>.Shared.Rent", src);
        Assert.DoesNotContain("new int[", src);
    }

    [Fact]
    public void StringDistance_PooledRows_MatchKnownDistances()
    {
        Assert.Equal(0, StringDistance.Levenshtein("", ""));
        Assert.Equal(3, StringDistance.Levenshtein("", "abc"));
        Assert.Equal(3, StringDistance.Levenshtein("kitten", "sitting"));
        Assert.Equal("sitting", StringDistance.BestMatch("sittin", new[] { "kitten", "sitting", "bitten" }));
    }

    [Fact]
    public void VerbsTable_ShipsEmbeddedOnly()
    {
        var csproj = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Core/Atheriz.Core.csproj");
        Assert.Contains("<EmbeddedResource Include=\"Objects/VerbConjugation/verbs.txt\" />", csproj);
        Assert.DoesNotContain("verbs.txt\" CopyToOutputDirectory", csproj);
    }

    [Fact]
    public void VerbsTable_LoadsFromEmbeddedResource()
    {
        // No output-dir copy anymore: the table must still resolve (embedded
        // wins in Conjugate either way). These irregulars are absent from the
        // synthetic fallback subset, so they prove the full embedded table.
        Assert.Equal("write", Atheriz.Core.Objects.VerbConjugation.Conjugate.VerbInfinitive("written"));
        Assert.Equal("break", Atheriz.Core.Objects.VerbConjugation.Conjugate.VerbInfinitive("broken"));
        Assert.Equal("is", Atheriz.Core.Objects.VerbConjugation.Conjugate.VerbConjugate("be", "3rd singular present"));
    }

    [Fact]
    public void FsUtil_TryChmod_AcceptsExplicitMode()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var ex = Record.Exception(() => FsUtil.TryChmod(tmp,
                UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.Null(ex);
            Assert.True(File.Exists(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
