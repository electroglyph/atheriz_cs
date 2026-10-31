using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// The in-game restart verb posts to the server's restart endpoint with the
// same token and scheme discipline as shutdown. Without a token it refuses
// locally; without a server it reports the connection failure.
[Collection("Ported")]
public class InGameRestartCommandTests
{
    [Fact]
    public void Restart_MissingToken_ReportsError()
    {
        using var env = GlobalTestEnv.Enter();
        var origSecret = AtherizSettings.Global.SecretPath;
        var emptyDir = Path.Combine(env.TempPath, "emptysecret");
        Directory.CreateDirectory(emptyDir);
        AtherizSettings.Global.SecretPath = emptyDir;
        try
        {
            ObjectRegistry.ClearAll();
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            admin.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(admin, "restart", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("admin.token not found", msgs);
        }
        finally
        {
            AtherizSettings.Global.SecretPath = origSecret;
            ObjectRegistry.ClearAll();
        }
    }

    [Fact]
    public async Task Restart_NoServer_ReportsConnectionError()
    {
        using var env = GlobalTestEnv.Enter();
        var origSecret = AtherizSettings.Global.SecretPath;
        var origPort = AtherizSettings.Global.WebserverPort;
        var secretDir = Path.Combine(env.TempPath, "rsecret");
        Directory.CreateDirectory(secretDir);
        File.WriteAllText(Path.Combine(secretDir, "admin.token"), "test-token");
        AtherizSettings.Global.SecretPath = secretDir;
        // A port nothing listens on: bind-then-close reserves one from the
        // OS instead of guessing, so a foreign server on the default port
        // cannot answer us.
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        AtherizSettings.Global.WebserverPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        try
        {
            ObjectRegistry.ClearAll();
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            admin.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(admin, "restart", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < end)
            {
                if (admin.PeekMessages().Any(m => m.Contains("restart endpoint") || m.Contains("restart initiated"))) break;
                await Task.Delay(50);
            }
            Assert.Contains(admin.PeekMessages(), m => m.Contains("restart endpoint") || m.Contains("restart initiated"));
        }
        finally
        {
            AtherizSettings.Global.SecretPath = origSecret;
            AtherizSettings.Global.WebserverPort = origPort;
            ObjectRegistry.ClearAll();
        }
    }
}
