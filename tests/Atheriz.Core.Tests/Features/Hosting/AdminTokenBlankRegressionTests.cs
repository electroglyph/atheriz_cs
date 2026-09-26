using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// Audit 10 finding 1 (blank token file authenticates) and finding 14
// (length-dependent compare): a zero-byte/whitespace token file must never
// authorize, and the comparison must be constant-time across lengths.
[Collection("Ported")]
public sealed class AdminTokenBlankRegressionTests
{
    private static string NewSecretDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_tokenblank_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void CheckAdmin_BlankTokenFile_RejectsMissingHeader()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "admin.token"), []);
            Assert.Equal("Token file not found.", AdminToken.CheckAdmin(dir, "127.0.0.1", null, "shutdown"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CheckAdmin_BlankTokenFile_RejectsEmptyHeader()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "admin.token"), "  \n");
            Assert.Equal("Token file not found.", AdminToken.CheckAdmin(dir, "127.0.0.1", "", "create_account"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CheckAdmin_BlankTokenFile_StillRejectsWrongToken()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "admin.token"), []);
            Assert.Equal("Token file not found.", AdminToken.CheckAdmin(dir, "127.0.0.1", "not-the-token", "hot_reload"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CheckAdmin_RealToken_AllowsCorrect_RejectsWrong()
    {
        var dir = NewSecretDir();
        try
        {
            var token = AdminToken.EnsureToken(dir);
            Assert.Null(AdminToken.CheckAdmin(dir, "127.0.0.1", token, "shutdown"));
            Assert.Equal("Invalid token.", AdminToken.CheckAdmin(dir, "127.0.0.1", "wrong", "shutdown"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CheckAdmin_RealTokenFile_BlankHeader_Rejected()
    {
        // Blank-provided against a real file must fail closed (the header
        // default for a missing X-Admin-Token is string.Empty).
        var dir = NewSecretDir();
        try
        {
            AdminToken.EnsureToken(dir);
            Assert.Equal("Invalid token.", AdminToken.CheckAdmin(dir, "127.0.0.1", null, "shutdown"));
            Assert.Equal("Invalid token.", AdminToken.CheckAdmin(dir, "127.0.0.1", "", "shutdown"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ValidateToken_BlankAgainstBlank_IsFalse()
    {
        Assert.False(AdminToken.ValidateToken("", ""));
        Assert.False(AdminToken.ValidateToken(null, ""));
    }

    [Fact]
    public void ValidateToken_EqualTokens_AreTrue_DifferentLengths_AreFalse()
    {
        Assert.True(AdminToken.ValidateToken("abc123", "abc123"));
        Assert.False(AdminToken.ValidateToken("abc", "abc123"));
        Assert.False(AdminToken.ValidateToken("abc123", "abc"));
    }
}
