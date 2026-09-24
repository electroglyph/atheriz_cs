using Atheriz.Core.Network;
using Atheriz.Server.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atheriz.Server.Hosting;

// Foreground server startup shared by start/new/restart/reset. Returns the
// process exit code instead of exiting: the CLI applies it. Two builds:
// WebApplication when HTTP is on (routes, static files, admin), plain
// generic Host when off (telnet/game protocols only — Kestrel never starts,
// so no bind and no NullWebServer stand-in).
public static class ServerHost
{
    internal static void AddAtherizCore(IServiceCollection services, IConfiguration config)
    {
        services.Configure<AtherizSettings>(config.GetSection("Atheriz"));
        services.AddSingleton<IValidateOptions<AtherizSettings>, AtherizSettingsValidator>();
        services.AddOptions<AtherizSettings>().ValidateOnStart();
        // Overrides must be added to Configuration before Build so
        // IOptionsMonitor.CurrentValue reflects them; delegates read after Build.
        services.AddSingleton(sp => sp.GetRequiredService<IOptionsMonitor<AtherizSettings>>().CurrentValue);
        services.AddSingleton<ConnectionManager>(sp => new ConnectionManager(settings: sp.GetRequiredService<IOptionsMonitor<AtherizSettings>>().CurrentValue));
        AdminAuthServices.AddAdminAuth(services);
    }

    internal static void ApplyCliOverrides(ConfigurationManager config, int? port, string? host, int? telnetPort)
    {
        if (port is not null) config.AddInMemoryCollection(new Dictionary<string, string?> { ["Atheriz:WebserverPort"] = port.Value.ToString() });
        if (telnetPort is not null) config.AddInMemoryCollection(new Dictionary<string, string?> { ["Atheriz:TelnetPort"] = telnetPort.Value.ToString() });
        if (host is not null) config.AddInMemoryCollection(new Dictionary<string, string?> { ["Atheriz:WebserverInterface"] = host, ["Atheriz:TelnetInterface"] = host });
    }

    internal static bool EnsureDirs(AtherizSettings s)
    {
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(s.SavePath); } catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return false; }
        try { Atheriz.Core.Utils.PathGuards.GuardSecretPath(s.SecretPath); } catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return false; }
        Atheriz.Core.Utils.PathGuards.EnsureSaveDirectory(s.SavePath);
        Atheriz.Core.Utils.PathGuards.EnsureSecretDirectory(s.SecretPath);
        return true;
    }

    private static AtherizSettings? ResolveSettings(IServiceProvider services)
    {
        var settings = services.GetRequiredService<AtherizSettings>();
        var validator = services.GetRequiredService<IValidateOptions<AtherizSettings>>();
        var result = validator.Validate(null, settings);
        if (result.Failed)
        {
            Console.Error.WriteLine($"Settings validation failed: {result.FailureMessage}");
            return null;
        }
        return settings;
    }

    private static void RegisterShutdown(IHostApplicationLifetime lifetime, AtherizSettings settings, PidFile? pidFile, bool withToken)
    {
        lifetime.ApplicationStopping.Register(() =>
        {
            try { ServerLifecycle.DoShutdown(settings); } catch { }
            try { pidFile?.Release(); } catch { }
            if (withToken) try { AdminToken.DeleteToken(settings.SecretPath); } catch { }
            Console.WriteLine("Server stopped.");
        });
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            try { pidFile?.Release(); } catch { }
            if (withToken) try { AdminToken.DeleteToken(settings.SecretPath); } catch { }
        };
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; lifetime.StopApplication(); };
    }

    public static async Task<int> RunForegroundAsync(int? portOverride, string? hostOverride, int? telnetOverride)
    {
        // Fail closed before claiming: an unparseable interface must never
        // bind a fallback (Kestrel and telnet both require literal IPs).
        // Shared gate with the background parent (DaemonSpawner.IsSafeHost).
        if (!DaemonSpawner.IsSafeHost(hostOverride))
        {
            Console.Error.WriteLine($"Invalid --host value: {hostOverride}");
            return 2;
        }
        // Captured before detach: TryDetachThisProcess clears the marker.
        bool daemon = DaemonDetach.IsDaemonChild;
        if (daemon) { DaemonDetach.TryDetachThisProcess(); DaemonCrashLog.Install(); }
        // Branch on pre-CLI configuration: no flag overrides WebserverEnabled.
        if (!StopHandler.EffectiveSettingsValue.WebserverEnabled)
            return await RunHeadlessAsync(portOverride, hostOverride, telnetOverride).ConfigureAwait(false);

        var builder = WebApplication.CreateBuilder();
        if (daemon) builder.Logging.ClearProviders();
        AddAtherizCore(builder.Services, builder.Configuration);
        ApplyCliOverrides(builder.Configuration, portOverride, hostOverride, telnetOverride);
        builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.WebHost.ConfigureKestrel((ctx, opts) => KestrelConfig.ConfigureKestrel(opts, ctx.Configuration));
        Protocols.AddAtherizProtocols(builder.Services, builder.Configuration);
        var app = builder.Build();
        var settings = ResolveSettings(app.Services);
        if (settings is null) return 1;
        AtherizSettings.Global = settings;
        try { Atheriz.Core.AtherizLogger.ApplySettings(settings); } catch { }
        if (daemon) { try { Atheriz.Core.AtherizLogger.LogInformation($"Daemon detach: {DaemonDetach.DetachNote}."); } catch { } }
        if (!EnsureDirs(settings)) return 1;
        if (!PidFile.TryAcquire(settings.SavePath, out var pidFile, out var pidReason, settings.WebserverPort)) { Console.WriteLine(pidReason ?? "Failed to acquire PID file."); return 1; }
        Console.WriteLine($"PID {Environment.ProcessId} acquired at {Path.Combine(settings.SavePath, "server.pid")}");
        try { _ = AdminToken.EnsureToken(settings.SecretPath); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to ensure admin token: {ex}"); pidFile?.Release(); return 1; }
        Console.WriteLine($"Admin token ensured at {Path.Combine(settings.SecretPath, "admin.token")}");
        try { ServerLifecycle.DoStartup(settings); }
        catch (Exception ex) { Console.Error.WriteLine($"Startup tasks failed: {ex}"); pidFile?.Release(); AdminToken.DeleteToken(settings.SecretPath); return 1; }
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        StaticFileConfig.Configure(app, settings);
        if (settings.WebsocketEnabled) app.Map("/ws", ctx => WebSocketHandler.HandleAsync(ctx, settings));
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAdminRoutes(settings);
        if (!daemon) PrintBanners(settings);
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        RegisterShutdown(lifetime, settings, pidFile, withToken: true);
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    internal const string HeadlessBanner = "Web server disabled (WebserverEnabled=false); HTTP, webclient, WebSocket and admin routes are off. Telnet and game protocols still run.";

    // No-HTTP build: generic host, telnet/game protocols only.
    internal static async Task<int> RunHeadlessAsync(int? portOverride, string? hostOverride, int? telnetOverride)
    {
        // Captured before detach: TryDetachThisProcess clears the marker.
        bool daemon = DaemonDetach.IsDaemonChild;
        if (daemon) { DaemonDetach.TryDetachThisProcess(); DaemonCrashLog.Install(); }
        if (!daemon) Console.WriteLine(HeadlessBanner);
        var builder = Host.CreateApplicationBuilder();
        if (daemon) builder.Logging.ClearProviders();
        AddAtherizCore(builder.Services, builder.Configuration);
        ApplyCliOverrides(builder.Configuration, portOverride, hostOverride, telnetOverride);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        Protocols.AddAtherizProtocols(builder.Services, builder.Configuration);
        var host = builder.Build();
        var settings = ResolveSettings(host.Services);
        if (settings is null) return 1;
        AtherizSettings.Global = settings;
        try { Atheriz.Core.AtherizLogger.ApplySettings(settings); } catch { }
        if (daemon) { try { Atheriz.Core.AtherizLogger.LogInformation($"Daemon detach: {DaemonDetach.DetachNote}."); } catch { } }
        if (!EnsureDirs(settings)) return 1;
        if (!PidFile.TryAcquire(settings.SavePath, out var pidFile, out var pidReason, settings.WebserverPort)) { Console.WriteLine(pidReason ?? "Failed to acquire PID file."); return 1; }
        Console.WriteLine($"PID {Environment.ProcessId} acquired at {Path.Combine(settings.SavePath, "server.pid")}");
        try { ServerLifecycle.DoStartup(settings); }
        catch (Exception ex) { Console.Error.WriteLine($"Startup tasks failed: {ex}"); pidFile?.Release(); return 1; }
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        RegisterShutdown(lifetime, settings, pidFile, withToken: false);
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    internal static void PrintBanners(AtherizSettings settings)
    {
        foreach (var line in FormatBannerLines(settings)) Console.WriteLine(line);
    }

    // Shared banner text with the background parent (DaemonSpawner prints
    // these while the detached child stays silent): same lines in both.
    /// <summary>Operator banner lines for a web-enabled server.</summary>
    public static string[] FormatBannerLines(AtherizSettings settings)
    {
        var lines = new List<string>();
        var displayHost = settings.WebserverInterface;
        if (displayHost.Contains(':')) displayHost = $"[{displayHost}]";
        var scheme = "http";
        if (!string.IsNullOrEmpty(settings.SslCertFile) && File.Exists(settings.SslCertFile ?? string.Empty))
        {
            // Claim https only when the cert actually loads; with
            // AllowInsecureTlsFallback the plaintext fallback serves http.
            try { Atheriz.Core.Utils.TlsCertLoader.Load(settings.SslCertFile!, settings.SslKeyFile)?.Dispose(); scheme = "https"; }
            catch when (settings.AllowInsecureTlsFallback) { scheme = "http"; }
        }
        lines.Add($"Web server listening on {scheme}://{displayHost}:{settings.WebserverPort}");
        if (settings.WebsocketEnabled) { var wssScheme = scheme == "https" ? "wss" : "ws"; lines.Add($"WebSocket server available at {wssScheme}://{displayHost}:{settings.WebserverPort}/ws"); }
        if (!string.IsNullOrEmpty(settings.SslCertFile))
        {
            lines.Add($"SSL is enabled (cert: {settings.SslCertFile})");
            if (!File.Exists(settings.SslCertFile)) lines.Add($"WARNING: SSL cert file not found: {settings.SslCertFile}");
            if (!string.IsNullOrEmpty(settings.SslKeyFile)) { lines.Add($"SSL status: separate key file ({settings.SslKeyFile})"); if (!File.Exists(settings.SslKeyFile)) lines.Add($"WARNING: SSL key file not found: {settings.SslKeyFile}"); }
            else lines.Add("SSL status: combined PEM (private key embedded)");
        }
        else lines.Add("SSL is disabled (set SSL_CERTFILE to enable)");
        return [.. lines];
    }
}
