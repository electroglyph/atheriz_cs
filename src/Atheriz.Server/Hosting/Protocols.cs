using Atheriz.Core.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Atheriz.Server.Hosting;

// Protocol service wiring. Feature flags are configuration (read here,
// pre-Build, so CLI overrides already applied); the resolved settings
// instance flows through IOptionsMonitor at host start.
public static class Protocols
{
    public static void AddAtherizProtocols(IServiceCollection services, IConfiguration config)
    {
        var flags = config.GetSection("Atheriz").Get<AtherizSettings>() ?? AtherizSettings.Global;
        if (flags.TelnetEnabled)
            services.AddHostedService(sp => new TelnetHostedService(
                sp.GetRequiredService<ConnectionManager>(),
                sp.GetRequiredService<IOptionsMonitor<AtherizSettings>>().CurrentValue,
                sp.GetRequiredService<IHostApplicationLifetime>()));
    }
}
