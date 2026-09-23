using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atheriz.Core.Tests.Features.Network;

// The telnet listener runs as a hosted service (not an untracked Task.Run):
// StartAsync kicks the loop off without blocking, and StopAsync joins it
// instead of abandoning it at shutdown.
[Collection("Ported")]
public sealed class TelnetHostedServiceTests
{
    [Fact]
    public async Task HostedService_StartReturnsPromptly_StopJoinsLoop()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1", TelnetPort = 0 };
        var mgr = PortedHelpers.MakeManager(settings);
        using var host = Host.CreateDefaultBuilder().Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var service = new TelnetHostedService(mgr, settings, lifetime);
        try
        {
            await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            try { mgr.Atp.Stop(wait: false); } catch { }
        }
    }
}
