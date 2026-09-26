using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Regression;

// Finding 9: StopHandler.LoadSettingsFor added the engine's own
// appsettings.json (next to the binaries) LAST, so the engine file
// overrode the game folder's config and the CLI controlled the wrong
// world. Atheriz.Server grants no InternalsVisibleTo, so this pins the
// shape: the game-folder sources stay, the BaseDirectory override is gone.
public sealed class CliConfigRegressionTests
{
    [Fact]
    public void LoadSettingsFor_HasNoEngineBaseDirectoryOverride()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        var region = SourceScan.Region(src, "internal static AtherizSettings LoadSettingsFor");
        Assert.Contains(".SetBasePath(gameFolder)", region, StringComparison.Ordinal);
        Assert.Contains(".AddJsonFile(\"appsettings.json\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("AppContext.BaseDirectory", region, StringComparison.Ordinal);
    }
}
