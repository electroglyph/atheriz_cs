using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// Startup must fail fast instead of serving traffic half-initialized.
[Collection("Ported")]
public class StartupFailureTests
{
    [Fact]
    public void DoStartup_DatabaseSetupFailure_RethrowsInsteadOfServingHalfInitialized()
    {
        // Behavior: when the database cannot be created, DoStartup must surface
        // the failure so the host exits (Program.cs:149) instead of binding
        // Kestrel with /health ok and /ready 503 forever. Today the
        // EnsureCreated failure at ServerLifecycle.cs:54 and the
        // StartStop.DoStartup failure at ServerLifecycle.cs:59-65 are logged
        // and swallowed (return), so startup reports success with no world.
        // A save path nested under a regular file can never be created, which
        // forces EnsureCreated to throw deterministically on any machine.
        using var env = GlobalTestEnv.Enter();
        var file = Path.GetTempFileName();
        var settings = new AtherizSettings { SavePath = Path.Combine(file, "save") };
        try
        {
            var ex = Record.Exception(() => ServerLifecycle.DoStartup(settings));
            Assert.NotNull(ex);
            Assert.False(ServerLifecycle.StartupSucceeded);
        }
        finally
        {
            ServerLifecycle.ResetForTesting();
            try { File.Delete(file); } catch { }
        }
    }
}
