using Atheriz.Core.Concurrency;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Logging;

// Section 2.6 pins: library diagnostics route through AtherizLogger (the
// MEL ILogger pipeline) instead of direct Console writes. The logger echoes
// every kept message to Console.Error, so these assertions use the logger's
// "LEVEL: category: message" format prefix — a direct Console write would
// emit the raw text without the prefix. Source scans pin the removal itself.
[Collection("Ported")]
public class LoggerRoutingTests
{
    [Fact]
    public void SubmitAfterStop_RoutesThroughLogger()
    {
        using var cap = new CaptureAtherizLog();
        using var pool = new AsyncThreadPool(maxThreads: 1, queueLimit: 100, reliefLimit: 0);
        pool.Stop(wait: false, timeout: TimeSpan.FromSeconds(2));
        try
        {
            Assert.False(pool.AddTask(() => { }));
            Assert.Contains(
                "ERROR: atheriz: [AsyncThreadPool] task submitted after stop; discarded",
                cap.Read());
        }
        finally
        {
            pool.Dispose();
        }
    }

    [Fact]
    public void QueueFull_RoutesThroughLogger()
    {
        using var cap = new CaptureAtherizLog();
        using var pool = new AsyncThreadPool(maxThreads: 1, queueLimit: 1, reliefLimit: 0);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        try
        {
            Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }));
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(pool.AddTask(() => { }));
            Assert.False(pool.AddTask(() => { }));
            Assert.Contains(
                "WARNING: atheriz: [AsyncThreadPool] task queue full (1); dropping task",
                cap.Read());
        }
        finally
        {
            gate.Set();
            pool.Dispose();
        }
    }

    // Fully converted files must contain no direct Console writes at all.
    public static IEnumerable<object[]> ConvertedFiles => new List<object[]>
    {
        new object[] { "src/Atheriz.Server/Hosting/KestrelConfig.cs" },
        new object[] { "src/Atheriz.Server/Hosting/ProtocolBootstrap.cs" },
        new object[] { "src/Atheriz.Server/Hosting/StaticFileConfig.cs" },
        new object[] { "src/Atheriz.Server/Hosting/AdminRoutes.cs" },
        new object[] { "src/Atheriz.Server/Infrastructure/PidFile.cs" },
        new object[] { "src/Atheriz.Server/Infrastructure/ServerLifecycle.cs" },
        new object[] { "src/Atheriz.Core/Globals/StartStop.cs" },
        new object[] { "src/Atheriz.Core/Globals/GlobalServices.cs" },
        new object[] { "src/Atheriz.Core/Globals/SaltProvider.cs" },
        new object[] { "src/Atheriz.Core/Commands/LoggedIn/ExitCommand.cs" },
        new object[] { "src/Atheriz.Core/Commands/UnloggedIn/ConnectCommand.cs" },
        new object[] { "src/Atheriz.Core/Commands/UnloggedIn/CreateAccountCommand.cs" },
        new object[] { "src/Atheriz.Core/Plugins/PluginLoader.cs" },
    };

    [Theory]
    [MemberData(nameof(ConvertedFiles))]
    public void ConvertedFile_HasNoDirectConsoleWrites(string relativePath)
    {
        // StartStop keeps one logger-failure fallback below; it is covered by
        // KeptFallbackCounts and excluded here.
        if (relativePath.EndsWith("Globals/StartStop.cs", StringComparison.Ordinal))
            return;
        var source = SourceScan.Read(relativePath);
        Assert.DoesNotContain("Console.WriteLine", source);
        Assert.DoesNotContain("Console.Error.WriteLine", source);
    }

    // Files with deliberate keeps: logger-failure fallbacks
    // (try { Log } catch { Console }) and CLI prompt validation. Counts pin
    // the keeps so a reintroduced direct write fails loudly.
    public static IEnumerable<object[]> KeptFallbackFiles => new List<object[]>
    {
        new object[] { "src/Atheriz.Server/Hosting/WebSocketHandler.cs", 2 },
        new object[] { "src/Atheriz.Core/Globals/StartStop.cs", 1 },
        new object[] { "src/Atheriz.Core/InitialSetup.cs", 2 },
        new object[] { "src/Atheriz.Core/Concurrency/AsyncThreadPool.cs", 5 },
        new object[] { "src/Atheriz.Core/Concurrency/AsyncTicker.cs", 1 },
    };

    [Theory]
    [MemberData(nameof(KeptFallbackFiles))]
    public void KeptFallbackFile_HasExactlyExpectedConsoleWrites(string relativePath, int expected)
    {
        var source = SourceScan.Read(relativePath);
        int actual = SourceScan.Count(source, "Console.WriteLine")
            + SourceScan.Count(source, "Console.Error.WriteLine");
        Assert.True(actual == expected,
            $"{relativePath}: expected {expected} kept Console writes, found {actual}");
    }
}
