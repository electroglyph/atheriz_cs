using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One admin guard-result shape for the three /_internal endpoints; endpoints
// keep their own response shapes. The Validate* one-line forwards are gone —
// the create-account site calls Validation directly.
[Collection("Ported")]
public class AdminGuardHelperTests
{
    [Fact]
    public void GuardHelpers_DefinedOnce_UsedThrice()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.Equal(1, SourceScan.Count(src, "bool RequireAdmin("));
        Assert.Equal(1, SourceScan.Count(src, "static IResult AdminError("));
        Assert.Equal(1, SourceScan.Count(src, "static IResult AdminOk("));
        Assert.Equal(3, SourceScan.Count(src, "RequireAdmin(ctx,"));
    }

    [Fact]
    public void CreateAccount_CallsValidationDirectly()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "AdminRoutes.cs");
        Assert.Contains("Validation.ValidateAccountName(accountName, settings)", src);
        Assert.Contains("Validation.ValidateCharacterName(charName, settings)", src);
        Assert.Contains("Validation.ValidatePassword(password, settings)", src);
        // No local Validate* wrappers survive: each name occurs exactly once
        // (the direct call above).
        Assert.Equal(1, SourceScan.Count(src, "ValidateAccountName("));
        Assert.Equal(1, SourceScan.Count(src, "ValidateCharacterName("));
        Assert.Equal(1, SourceScan.Count(src, "ValidatePassword("));
    }
}
