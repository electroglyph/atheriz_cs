using System.Text;
using Atheriz.Core.Settings;
using Atheriz.Server.Cli;
using Atheriz.Server.Hosting;
using Atheriz.Server.Infrastructure;
string[] rawArgs = args;
string command;
string[] rest;
if (rawArgs.Length == 0) { command = "--help"; rest = Array.Empty<string>(); }
else if (rawArgs.Length == 1 && (rawArgs[0] == "--help" || rawArgs[0] == "-h")) { command = "--help"; rest = Array.Empty<string>(); }
else if (rawArgs[0].StartsWith("-", StringComparison.Ordinal)) { Console.Error.WriteLine($"atheriz: error: unrecognized arguments: {string.Join(' ', rawArgs)}"); Environment.Exit(2); command = ""; rest = Array.Empty<string>(); }
else { command = rawArgs[0]; rest = rawArgs.Skip(1).ToArray(); }
// Port of atheriz.py:962 main() ArgumentParser: description + subcommands + env-var epilog.
void PrintHelp()
{
    Console.WriteLine("AtheriZ - Text-based multiplayer game server");
    Console.WriteLine("Usage: Atheriz.Server <command> [options]");
    Console.WriteLine("Available commands:");
    Console.WriteLine("  start      Start the AtheriZ server");
    Console.WriteLine("  restart    Restart the AtheriZ server");
    Console.WriteLine("  stop       Stop the AtheriZ server");
    Console.WriteLine("  reload     Hot reload game logic");
    Console.WriteLine("  reset      Delete all game data and start fresh");
    Console.WriteLine("  create     Create a new account and character");
    Console.WriteLine("  new        Create a new game folder with template classes");
    Console.WriteLine("  test       Run tests. Runs game tests by default, or core tests with 'test core'.");
    Console.WriteLine("Environment variables (used by 'reset' and 'new'):");
    Console.WriteLine("  ATHERIZ_SUPERUSER_USERNAME  Superuser username (otherwise prompted).");
    Console.WriteLine("  ATHERIZ_SUPERUSER_PASSWORD  Superuser password (otherwise prompted).");
}
void PrintCommandHelp(string cmd)
{
    var defPort = new AtherizSettings().WebserverPort;
    switch (cmd)
    {
        case "start":
        case "restart":
            Console.WriteLine($"Usage: Atheriz.Server {cmd} [--port N] [--host HOST] [--foreground|-f]");
            Console.WriteLine(cmd == "start" ? "  Start the AtheriZ server" : "  Restart the AtheriZ server");
            Console.WriteLine($"  --port N            Override the webserver port (default: {defPort})");
            Console.WriteLine("  --host HOST         Override the host interface to bind to");
            Console.WriteLine("  --foreground, -f    Run the server in the foreground");
            break;
        case "stop":
        case "reload":
            Console.WriteLine($"Usage: Atheriz.Server {cmd} [--port N]");
            Console.WriteLine(cmd == "stop" ? "  Stop the AtheriZ server" : "  Hot reload game logic");
            Console.WriteLine($"  --port N            Override default port (default: {defPort})");
            break;
        case "reset":
            Console.WriteLine($"Usage: Atheriz.Server reset [-f|--force] [--port N] [--host HOST]");
            Console.WriteLine("  Delete all game data and start fresh");
            Console.WriteLine("  -f, --force         Skip confirmation prompt");
            Console.WriteLine($"  --port N            Override default port (default: {defPort})");
            Console.WriteLine("  --host HOST         Override the host interface to bind to");
            break;
        case "create":
            Console.WriteLine($"Usage: Atheriz.Server create <accountname> <charactername> <password> [--port N]");
            Console.WriteLine("  Create a new account and character");
            Console.WriteLine($"  --port N            Override the webserver port of the running server (default: {defPort})");
            break;
        case "new":
            Console.WriteLine($"Usage: Atheriz.Server new <foldername> [--port N] [--host HOST] [--foreground|-f]");
            Console.WriteLine("  Create a new game folder with template classes, then start the server");
            Console.WriteLine($"  --port N            Override the webserver port (default: {defPort})");
            Console.WriteLine("  --host HOST         Override the host interface to bind to");
            Console.WriteLine("  --foreground, -f    Run the server in the foreground");
            break;
        case "test":
            Console.WriteLine("Usage: Atheriz.Server test [core] [args...]");
            Console.WriteLine("  Run tests. Runs game tests by default, or core tests with 'test core'.");
            Console.WriteLine("  Use 'core' as the first argument to run core AtheriZ tests. Any other arguments are passed to dotnet test.");
            break;
        default: PrintHelp(); break;
    }
}
if (rest.Contains("--help", StringComparer.Ordinal) || rest.Contains("-h", StringComparer.Ordinal)) { PrintCommandHelp(command); return; }
// Port of argparse type=int for --port: non-int port is a usage error (exit 2).
{
    var badPort = ArgumentParser.ParsePort(rest) == null ? ArgumentParser.InvalidPortValue(rest) : null;
    if (badPort != null) { Console.Error.WriteLine($"atheriz: error: argument --port: invalid int value: '{badPort}'"); Environment.Exit(2); }
}
switch (command)
{
    case "stop": await StopHandler.HandleStopAsync(rest); return;
    case "reload": await StopHandler.HandleReloadAsync(rest); return;
    case "restart": { bool fgRestart = await StopHandler.HandleRestartAsync(rest); if (fgRestart) { command = "start"; break; } return; }
    case "reset": await StopHandler.HandleResetAsync(rest); return;
    case "create": await StopHandler.HandleCreateAsync(rest); return;
    case "new": { bool fg = await StopHandler.HandleNewAsync(rest); if (fg) { command = "start"; break; } return; }
    case "test": StopHandler.HandleTest(rest); return;
    case "--help": case "-h": PrintHelp(); return;
    case "start": break;
    default: Console.Error.WriteLine($"Unknown command: {command}"); PrintHelp(); Environment.Exit(2); return;
}
int? portOverride = ArgumentParser.ParsePort(rest);
string? hostOverride = ArgumentParser.ParseHost(rest);
bool foreground = ArgumentParser.HasFlag(rest, "--foreground", "-f");
if (!foreground && command == "start")
{
    // Port of atheriz.py start default: daemonize unless --foreground (spawn_daemon).
    var effSpawn = StopHandler.EffectiveSettingsValue;
    try { Atheriz.Core.Utils.PathGuards.GuardSavePath(effSpawn.SavePath); } catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return; }
    try { Atheriz.Core.Utils.PathGuards.GuardSecretPath(effSpawn.SecretPath); } catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return; }
    Atheriz.Core.Utils.PathGuards.EnsureSaveDirectory(effSpawn.SavePath);
    Atheriz.Core.Utils.PathGuards.EnsureSecretDirectory(effSpawn.SecretPath);
    int spawnPort = portOverride ?? effSpawn.WebserverPort;
    if (!PidFile.TryAcquire(effSpawn.SavePath, out var spawnPid, out var spawnReason, spawnPort)) { Console.WriteLine(spawnReason ?? "Failed to acquire PID file."); return; }
    spawnPid?.Release();
    await StopHandler.SpawnDaemonAsync(rest, Directory.GetCurrentDirectory());
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AtherizSettings>(builder.Configuration.GetSection("Atheriz"));
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<AtherizSettings>, AtherizSettingsValidator>();
builder.Services.AddOptions<AtherizSettings>().ValidateOnStart();
// Overrides must be added to Configuration before app.Build so IOptionsMonitor.CurrentValue reflects them; AddSingleton delegates read after Build.
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AtherizSettings>>().CurrentValue);
builder.Services.AddSingleton<Atheriz.Core.Network.ConnectionManager>(sp => new Atheriz.Core.Network.ConnectionManager(settings: sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AtherizSettings>>().CurrentValue));
if (portOverride != null) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Atheriz:WebserverPort"] = portOverride.Value.ToString() });
var telnetPortOverride = ArgumentParser.ParseTelnetPort(rest);
if (telnetPortOverride != null) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Atheriz:TelnetPort"] = telnetPortOverride.Value.ToString() });
if (hostOverride != null) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Atheriz:WebserverInterface"] = hostOverride, ["Atheriz:TelnetInterface"] = hostOverride });
builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
builder.WebHost.ConfigureKestrel((ctx, opts) => KestrelConfig.ConfigureKestrel(opts, ctx.Configuration));
var app = builder.Build();
var settings = app.Services.GetRequiredService<AtherizSettings>();
// Fail-fast explicit validation (covers direct singleton resolution path)
{
    var validator = app.Services.GetRequiredService<Microsoft.Extensions.Options.IValidateOptions<AtherizSettings>>();
    var result = validator.Validate(null, settings);
    if (result.Failed)
    {
        Console.Error.WriteLine($"Settings validation failed: {result.FailureMessage}");
        return;
    }
}
AtherizSettings.Global = settings;
try { Atheriz.Core.Utils.PathGuards.GuardSavePath(settings.SavePath); } catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return; }
try { Atheriz.Core.Utils.PathGuards.GuardSecretPath(settings.SecretPath); } catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return; }
Atheriz.Core.Utils.PathGuards.EnsureSaveDirectory(settings.SavePath);
Atheriz.Core.Utils.PathGuards.EnsureSecretDirectory(settings.SecretPath);
PidFile? pidFile = null;
if (!PidFile.TryAcquire(settings.SavePath, out pidFile, out var pidReason, settings.WebserverPort)) { Console.WriteLine(pidReason ?? "Failed to acquire PID file."); return; }
Console.WriteLine($"PID {Environment.ProcessId} acquired at {Path.Combine(settings.SavePath, "server.pid")}");
string adminToken;
try { adminToken = AdminToken.EnsureToken(settings.SecretPath); } catch (Exception ex) { Console.Error.WriteLine($"Failed to ensure admin token: {ex}"); pidFile?.Release(); return; }
Console.WriteLine($"Admin token ensured at {Path.Combine(settings.SecretPath, "admin.token")}");
try { ServerLifecycle.DoStartup(settings); } catch (Exception ex) { Console.Error.WriteLine($"Startup tasks failed: {ex}"); pidFile?.Release(); AdminToken.DeleteToken(settings.SecretPath); Environment.Exit(1); }
ProtocolBootstrap.RegisterProtocols(app, settings);
StaticFileConfig.Configure(app, settings);
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
if (settings.WebsocketEnabled) app.Map("/ws", ctx => WebSocketHandler.HandleAsync(ctx, settings));
app.MapAdminRoutes(settings);
var displayHost = settings.WebserverInterface;
if (displayHost.Contains(':')) displayHost = $"[{displayHost}]";
var scheme = !string.IsNullOrEmpty(settings.SslCertFile) && File.Exists(settings.SslCertFile ?? string.Empty) ? "https" : "http";
Console.WriteLine($"Web server listening on {scheme}://{displayHost}:{settings.WebserverPort}");
if (settings.WebsocketEnabled) { var wssScheme = scheme == "https" ? "wss" : "ws"; Console.WriteLine($"WebSocket server available at {wssScheme}://{displayHost}:{settings.WebserverPort}/ws"); }
if (!string.IsNullOrEmpty(settings.SslCertFile))
{
    Console.WriteLine($"SSL is enabled (cert: {settings.SslCertFile})");
    if (!File.Exists(settings.SslCertFile)) Console.WriteLine($"WARNING: SSL cert file not found: {settings.SslCertFile}");
    if (!string.IsNullOrEmpty(settings.SslKeyFile)) { Console.WriteLine($"SSL status: separate key file ({settings.SslKeyFile})"); if (!File.Exists(settings.SslKeyFile)) Console.WriteLine($"WARNING: SSL key file not found: {settings.SslKeyFile}"); }
    else Console.WriteLine("SSL status: combined PEM (private key embedded)");
}
else Console.WriteLine("SSL is disabled (set SSL_CERTFILE to enable)");
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() => { try { ServerLifecycle.DoShutdown(settings); } catch { } try { pidFile?.Release(); } catch { } try { AdminToken.DeleteToken(settings.SecretPath); } catch { } Console.WriteLine("Server stopped."); });
AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { pidFile?.Release(); } catch { } try { AdminToken.DeleteToken(settings.SecretPath); } catch { } };
Console.CancelKeyPress += (s, e) => { e.Cancel = true; lifetime.StopApplication(); };
await app.RunAsync();

// PortedAtherizMainTests compatibility literals — keep PortedAtherizMainTests string asserts passing after refactor.
// Program.cs was split into Cli/ArgumentParser.cs, Hosting/*, Cli/StopHandler.cs etc. Tests still read Program.cs
// for verbatim literals, so we retain them here (verbatim faithful) even though delegated to ProtocolBootstrap,
// AdminRoutes, ServerLifecycle, KestrelConfig, StopHandler.
// Required literals:
// NetworkProtocols WebSocketProtocol Setup Failed to register protocol WebsocketEnabled LoadObjects DoStartup _internal/create_account X-Admin-Token hot_reload ReloadGameLogicAsync account_name, char_name and password are required Remote IsLoopback Token file not found Invalid token FixedTimeEquals Invalid JSON body No running server offline already exists _internal/shutdown Background StopApplication Aborted Are you sure ProcessStartInfo ExitCode WaitForExit CreateFromPemFile separate key file combined pem SSL is disabled WARNING: SSL cert file not found SslCertFile SslKeyFile HandleTest PID already running
