using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Small validation holes with sharp edges: zero-byte tokens must be rejected
// instead of minting empty credentials (CryptoRandom.cs:30-49 throws only for
// negative sizes), and best-effort chmod must surface programmer error
// (ArgumentNull) rather than swallowing it — best-effort covers IO/permission
// failures only (FsUtil.cs:13-22). Pure checks, no engine globals.
public sealed class TokenValidationTests
{
    [Fact]
    public void CryptoRandom_UrlSafeToken_ZeroBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.UrlSafeToken(0));
    }

    [Fact]
    public void CryptoRandom_HexToken_ZeroBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.HexToken(0));
    }

    [Fact]
    public void FsUtil_TryChmod_NullPath_SurfacesArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => FsUtil.TryChmod0600(null!));
        Assert.Throws<ArgumentNullException>(() => FsUtil.TryChmod0700(null!));
    }
}
