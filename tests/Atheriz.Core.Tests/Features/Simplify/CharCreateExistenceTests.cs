using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Early-exit registry lookups in AtCharCreate: same snapshot-then-scan lock
// discipline as FilterBy (snapshot under the read lock, predicates outside),
// first match in registry order wins. The account lookup returns the object
// (password check, MaxCharacters, AddCharacter need it), not just existence.
[Collection("Ported")]
public class CharCreateExistenceTests
{
    private static string Create(string acc, string ch, string pw)
    {
        var home = new Node(AtherizSettings.Global.DefaultHome);
        ObjectRegistry.AddObject(home);
        var sw = new StringWriter();
        ServerEvents.AtCharCreate(acc, ch, pw, sw);
        return sw.ToString();
    }

    [Fact]
    public void DuplicateCharacterName_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Contains("Success! Account and character created.", Create("ac1", "HeroX", "password123"));
        Assert.Contains("already exists", Create("ac2", "HeroX", "password123"));
    }

    [Fact]
    public void SameAccount_SamePassword_AddsCharacter_WrongPassword_Refused()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Contains("Success! Account and character created.", Create("ac1", "HeroA", "password123"));
        Assert.Contains("Success! Character created.", Create("ac1", "HeroB", "password123"));
        Assert.Contains("different password", Create("ac1", "HeroC", "otherpass99"));
    }

    [Fact]
    public void ExistenceHelpers_SnapshotThenScan_EarlyExit()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static bool RegistryExists("));
        Assert.Equal(1, SourceScan.Count(src, "private static GameObject? RegistryFindFirst("));
        Assert.Contains("ObjectRegistry.FilterBy(_ => true)", src);
        Assert.DoesNotContain("FilterBy(o => o.IsPc", src);
        Assert.DoesNotContain(".Count > 0", src);
    }
}
