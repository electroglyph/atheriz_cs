using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Collapsed chmod/guard/directory wrappers: shared cores keep identical
// messages, guards, and create+chmod+probe behavior.
public class FsGuardCollapseTests
{
    [Fact]
    public void GuardSecretPath_Absolute_Passes()
    {
        PathGuards.GuardSecretPath(Path.GetTempPath());
    }

    [Fact]
    public void GuardSecretPath_Relative_ThrowsMentioningSecretPath()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PathGuards.GuardSecretPath("secret"));
        Assert.Contains("SECRET_PATH", ex.Message);
    }

    [Fact]
    public void EnsureSecretDirectory_WritableDir_CreatesAndPasses()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"), "secret");
        try
        {
            PathGuards.EnsureSecretDirectory(dir);
            Assert.True(Directory.Exists(dir));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { } }
    }

    [Fact]
    public void EnsureSavePathValid_CustomKind_PreservesKindInMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PathGuards.EnsureSavePathValid("rel-save", "custom"));
        Assert.Contains("custom", ex.Message);
        Assert.Contains("SAVE_PATH", ex.Message);
    }

    [Fact]
    public void EnsureSavePathValid_DefaultKind_MatchesGuardSavePath()
    {
        string? guardMsg = null;
        string? legacyMsg = null;
        try { PathGuards.GuardSavePath("rel-save"); } catch (InvalidOperationException ex) { guardMsg = ex.Message; }
        try { PathGuards.EnsureSavePathValid("rel-save"); } catch (InvalidOperationException ex) { legacyMsg = ex.Message; }
        Assert.NotNull(guardMsg);
        Assert.Equal(guardMsg, legacyMsg);
    }
}
