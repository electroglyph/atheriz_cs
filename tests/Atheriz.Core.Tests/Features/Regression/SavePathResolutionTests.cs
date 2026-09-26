using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Regression;

// Finding 15: startup/shutdown read the world from settings.SavePath while
// every save resolved ATHERIZ_SAVE_PATH first, so an override split the
// world across two files. Each production call site now routes through
// ResolveSavePath. A source scan pins the routing: calling the registry or
// factory methods directly with a resolved path passes either way, so only
// the call-site text discriminates.
public sealed class SavePathResolutionTests
{
    [Fact]
    public void StartupAndShutdownPaths_ResolveSavePath()
    {
        var startStop = SourceScan.Read("src", "Atheriz.Core", "Globals", "StartStop.cs");
        Assert.Contains("LoadObjects(AtherizDbContextFactory.ResolveSavePath(settings))", startStop, StringComparison.Ordinal);
        Assert.Contains("IsDirty(AtherizDbContextFactory.ResolveSavePath(settings))", startStop, StringComparison.Ordinal);
        Assert.Contains("new AtherizDbContext(AtherizDbContextFactory.ResolveSavePath(settings))", startStop, StringComparison.Ordinal);

        var lifecycle = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "ServerLifecycle.cs");
        Assert.Contains("new AtherizDbContext(AtherizDbContextFactory.ResolveSavePath(settings))", lifecycle, StringComparison.Ordinal);

        var admin = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "AdminCommands.cs");
        Assert.Contains("SaveObjects(Atheriz.Core.Persistence.AtherizDbContextFactory.ResolveSavePath(settings))", admin, StringComparison.Ordinal);

        var factory = SourceScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContextFactory.cs");
        Assert.Contains("ResolveSavePath(AtherizSettings.Global)", factory, StringComparison.Ordinal);
    }
}
