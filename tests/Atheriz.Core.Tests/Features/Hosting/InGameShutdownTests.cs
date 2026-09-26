using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Atheriz.Core.Tests.Features.Hosting;

// Audit 10 finding 13: the in-game `shutdown` command must follow the
// server's scheme (https when SslCertFile is set, with the loopback cert
// callback and a flipped-scheme retry) and must fire at_server_stop exactly
// once — the route's DoShutdown owns the hooks, not an eager call in the
// command. Neuter (restore the eager AtServerStop) drives the counter to 2.
[Collection("Ported")]
public sealed class InGameShutdownTests
{
    private sealed class StopCounter : GameObject
    {
        public int Stops;
        public override void AtServerStop(object? sender) => Interlocked.Increment(ref Stops);
    }

    [Fact]
    public async Task InGameShutdown_FiresAtServerStopOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = Path.Combine(Path.GetTempPath(), "apin13_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var secret = Path.Combine(tmp, "secret");
        Directory.CreateDirectory(secret);
        var token = new string('a', 64);
        await File.WriteAllTextAsync(Path.Combine(secret, "admin.token"), token);

        var settings = new AtherizSettings
        {
            SecretPath = secret,
            ServerName = "Pin13",
            NetworkProtocols = Array.Empty<string>(),
            AutosaveOnShutdown = false,
            TimeSystemEnabled = false,
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = tmp });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(settings);
        AdminAuthServices.AddAdminAuth(builder.Services);
        var app = builder.Build();
        app.MapAdminRoutes(settings);
        app.UseAuthentication();
        app.UseAuthorization();
        await app.StartAsync();
        var prevGlobal = AtherizSettings.Global;
        try
        {
            var addr = app.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal));
            settings.WebserverPort = new Uri(addr).Port;
            AtherizSettings.Global = settings;

            var counter = new StopCounter();
            ObjectRegistry.AddObject(counter);
            var pc = GameObject.Create("pin13admin", isPc: true, privilege: Privilege.Admin);
            ObjectRegistry.AddObject(pc);

            var job = CommandDispatcher.DispatchLoggedIn(pc, "shutdown", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);

            Assert.True(PortedHelpers.WaitFor(() => counter.Stops >= 1, 15000));
            Thread.Sleep(2000);
            Assert.Equal(1, counter.Stops);
        }
        finally
        {
            AtherizSettings.Global = prevGlobal;
            try { await app.StopAsync(); } catch { }
            await app.DisposeAsync();
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    [Fact]
    public void ShutdownCommand_SelectsSchemeAndCertCallback()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", "AdminCommands.cs");
        Assert.DoesNotContain("ServerEvents.AtServerStop", src);
        Assert.Contains("SslCertFile", src);
        Assert.Contains("ServerCertificateCustomValidationCallback", src);
        Assert.Contains("TryPostShutdownAsync(second", src);
    }
}
