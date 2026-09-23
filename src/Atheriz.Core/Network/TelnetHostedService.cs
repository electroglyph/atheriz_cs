using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using telnet_cs.Server;

namespace Atheriz.Core.Network;

// Hosted-service owner for the telnet listener: the accept-loop body used to
// run on an untracked Task.Run started from TelnetProtocol.Setup, so shutdown
// never joined it. StartAsync kicks the loop off synchronously (the same
// non-blocking kickoff the Task.Run gave); StopAsync joins it via the linked
// lifetime token instead of abandoning it.
public sealed class TelnetHostedService : BackgroundService
{
    private readonly ConnectionManager _manager;
    private readonly AtherizSettings _settings;
    private readonly IHostApplicationLifetime _lifetime;

    public TelnetHostedService(ConnectionManager manager, AtherizSettings settings, IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lifetime);
        _manager = manager;
        _settings = settings;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.ApplicationStopping);
        TelnetServer? server = null;
        try
        {
            var tlsCert = _settings.TelnetTlsEnabled ? TelnetProtocol.BuildTelnetSslContext(_settings) : null;
            if (tlsCert is not null) Atheriz.Core.AtherizLogger.LogInformation($"SSL is enabled for telnet (cert: {_settings.SslCertFile}) with auto-detection for plaintext clients");
            else if (_settings.TelnetTlsEnabled) Atheriz.Core.AtherizLogger.LogWarning("TELNET_TLS_ENABLED is on but no usable cert — running plaintext");
            var handoff = new ConcurrentQueue<string?>();
            var filter = TelnetProtocol.BuildAcceptFilter(_manager, handoff);
            var options = TelnetProtocol.BuildServerOptions(_settings, tlsCert, filter);
            Atheriz.Core.AtherizLogger.LogInformation($"Starting Telnet Protocol on {_settings.TelnetInterface}:{_settings.TelnetPort}");
            server = new TelnetServer(_settings.TelnetPort, options);
            server.Start();
            var running = server;
            using var reg = _lifetime.ApplicationStopping.Register(() => { try { running.Stop(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); } });
            await TelnetProtocol.AcceptLoopAsync(server, handoff, _manager, _settings, linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"[Telnet] server failed: {ex}"); }
        finally { try { server?.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); } Atheriz.Core.AtherizLogger.LogInformation("Telnet Protocol server stopped."); }
    }
}
