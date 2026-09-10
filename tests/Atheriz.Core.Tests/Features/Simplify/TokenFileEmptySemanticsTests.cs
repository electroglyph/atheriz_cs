using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Simplify;

// One token-file read shared by the five admin-token sites. Empty-file
// semantics stay per-site: EnsureToken treats empty as missing (regenerate),
// ReadToken/CheckAdmin let it flow into the comparison.
[Collection("Ported")]
public class TokenFileEmptySemanticsTests
{
    private static string NewSecretDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_tokensem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void EnsureToken_MissingFile_CreatesHexToken()
    {
        var dir = NewSecretDir();
        try
        {
            string token = AdminToken.EnsureToken(dir);
            Assert.Equal(64, token.Length);
            Assert.Matches("^[0-9a-f]+$", token);
            Assert.Equal(token, File.ReadAllText(Path.Combine(dir, "admin.token")).Trim());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void EnsureToken_EmptyFile_TreatedAsMissing()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "admin.token"), "   \n");
            // Exists-but-empty: the atomic create loses the race to the
            // existing file and the reread finds nothing usable.
            Assert.Throws<InvalidOperationException>(() => AdminToken.EnsureToken(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReadToken_EmptyFile_FlowsThrough()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "admin.token"), "  \n");
            Assert.Equal("", AdminToken.ReadToken(dir));
            Assert.Null(AdminToken.ReadToken(Path.Combine(Path.GetTempPath(), "atheriz_tokensem_missing_" + Guid.NewGuid().ToString("N"))));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CheckAdmin_EmptyFile_Mismatches()
    {
        var dir = NewSecretDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "admin.token"), "\n");
            Assert.Equal("Invalid token.", AdminToken.CheckAdmin(dir, "127.0.0.1", "anything", "test"));
            Assert.Equal("Token file not found.", AdminToken.CheckAdmin(Path.Combine(dir, "nodir"), "127.0.0.1", "anything", "test"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TokenRead_LivesInOneHelper_PreservingEmpty()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AdminToken.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static string? TryReadTokenFile("));
        Assert.Contains("Empty is NOT mapped", src);
    }
}
