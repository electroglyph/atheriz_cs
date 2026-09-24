using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// World setup takes one options record and runs in named stages —
// directories, salt, registry reset, limbo build, credentials, checkpoint —
// instead of one 290-line method with five positional strings.
public class SetupStagesTests
{
    [Fact]
    public void Setup_TakesOptionsRecord()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "IGameSetup.cs");
        Assert.Contains("public sealed record SetupOptions(", src);
        Assert.Contains("void DoSetup(SetupOptions options);", src);
        Assert.DoesNotContain("DoSetup(string savePath", src);
    }

    [Fact]
    public void DoSetup_RunsNamedStages()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        Assert.Contains("PrepareDirectories(options.SavePath, options.SecretPath)", src);
        Assert.Contains("ResetWorldState(absSecret)", src);
        Assert.Contains("BuildLimboWorld(settings)", src);
        Assert.Contains("ResolveCredentials(options)", src);
        Assert.Contains("PersistSeed(world, settings, creds, absSave, absSecret)", src);
        Assert.Contains("SeedAccount(world, settings, creds, absSecret, db)", src);
    }

    [Fact]
    public void DoSetup_HasNoPositionalStringOverloads()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        Assert.DoesNotContain("string? username", src);
        Assert.DoesNotContain("TextReader? input", src);
    }
}
