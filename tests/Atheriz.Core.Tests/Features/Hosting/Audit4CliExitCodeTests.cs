using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// Regression pins for the audit4 server fix (S-1): CLI handler
// failure/abort paths must record a nonzero exit signal (consumed by
// Program.cs) instead of silently succeeding.
[Collection("Ported")]
public class Audit4CliExitCodeTests
{
    private static int FindFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    }

    [Fact]
    public async Task New_RejectedFolder_SignalsFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_exitrej_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var origCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "my-game"));
            CliExitCode.Set(0);
            var result = await NewHandler.HandleNewAsync(new[] { "my-game", "--foreground" });
            Assert.False(result);
            Assert.Equal(1, CliExitCode.Code);
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task New_ForegroundSuccess_SignalsZero()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_exitok_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var origCwd = Directory.GetCurrentDirectory();
        var oldUser = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldPass = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", "s1exitadmin");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", "s1ExitPass123");
        try
        {
            Directory.SetCurrentDirectory(root);
            CliExitCode.Set(1);
            var result = await NewHandler.HandleNewAsync(new[] { "s1exitgame", "--foreground" });
            Assert.True(result);
            Assert.Equal(0, CliExitCode.Code);
        }
        finally
        {
            try { Directory.SetCurrentDirectory(origCwd); } catch { }
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldPass);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task Reload_NoServer_SignalsFailure()
    {
        CliExitCode.Set(0);
        await ReloadHandler.HandleReloadAsync(new[] { "--port", FindFreePort().ToString() });
        Assert.Equal(1, CliExitCode.Code);
    }

    [Fact]
    public async Task Stop_NoServer_SignalsFailure()
    {
        CliExitCode.Set(0);
        await StopHandler.HandleStopAsync(new[] { "--port", FindFreePort().ToString() });
        Assert.Equal(1, CliExitCode.Code);
    }
}
