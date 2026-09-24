using Atheriz.Server.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atheriz.Core.Tests.Features.Hosting;

// Protocol service wiring: the telnet listener is a hosted service exactly
// when telnet is enabled. Replaces the old string-allowlist Setup pins.
[Collection("Ported")]
public sealed class ProtocolsTests
{
    private static ServiceCollection ServicesWithFlag(bool telnetEnabled, out IConfiguration config)
    {
        var services = new ServiceCollection();
        config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Atheriz:TelnetEnabled"] = telnetEnabled ? "true" : "false" })
            .Build();
        return services;
    }

    [Fact]
    public void AddAtherizProtocols_RegistersTelnetWhenEnabled()
    {
        var services = ServicesWithFlag(true, out var config);
        Protocols.AddAtherizProtocols(services, config);
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddAtherizProtocols_SkipsTelnetWhenDisabled()
    {
        var services = ServicesWithFlag(false, out var config);
        Protocols.AddAtherizProtocols(services, config);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }
}
