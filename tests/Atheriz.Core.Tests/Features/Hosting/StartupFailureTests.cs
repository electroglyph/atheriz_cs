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
        // Poison the RESOLVED path: startup resolves ATHERIZ_SAVE_PATH first,
        // so the override must point at the uncreatable location too, or the
        // test would exercise the fixture's valid scratch dir instead.
        using var env = GlobalTestEnv.Enter();
        var file = Path.GetTempFileName();
        var poisoned = Path.Combine(file, "save");
        var settings = new AtherizSettings { SavePath = poisoned };
        var origEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", poisoned);
        try
        {
            var ex = Record.Exception(() => ServerLifecycle.DoStartup(settings));
            Assert.NotNull(ex);
            Assert.False(ServerLifecycle.StartupSucceeded);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", origEnv);
            ServerLifecycle.Reset();
            try { File.Delete(file); } catch { }
        }
    }
}
