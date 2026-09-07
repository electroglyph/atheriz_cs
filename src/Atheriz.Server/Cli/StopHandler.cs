using System.Diagnostics;
using System.Text;
using Atheriz.Core.Settings;
using Atheriz.Server.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Atheriz.Server.Cli;

public static class StopHandler
{
    internal static AtherizSettings EffectiveSettingsValue => EffectiveSettings;
    private static AtherizSettings? _effectiveCache;
    private static AtherizSettings EffectiveSettings => _effectiveCache ??= LoadEffectiveSettings();
    internal static void InvalidateEffectiveSettings() => _effectiveCache = null;
    private static AtherizSettings LoadEffectiveSettings()
    {
        // Prefer Global if it has been initialized from host; otherwise load from appsettings.json + env
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables();
            try { builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true); } catch { }
            var cfg = builder.Build();
            var s = cfg.GetSection("Atheriz").Get<AtherizSettings>();
            if (s != null) return s;
        }
        catch { }
        return AtherizSettings.Global;
    }

    public static async Task HandleStopAsync(string[] a)
    {
        var port = ArgumentParser.ParsePort(a) ?? EffectiveSettings.WebserverPort;
        var secretPath = EffectiveSettings.SecretPath;
        var tlsOn = !string.IsNullOrEmpty(EffectiveSettings.SslCertFile);
        switch (await ShutdownClient.TryRequestShutdownAsync(port, secretPath, tlsOn))
        {
            case ShutdownRequestResult.Accepted:
                Console.WriteLine("Graceful shutdown request accepted; the server will stop itself.");
                return;
            case ShutdownRequestResult.AuthRejected:
                // A live server refused us: abort here, never escalate into signals.
                return;
            default: break;
        }
        var savePath = EffectiveSettings.SavePath;
        var pidFilePath = PidFile.LocateServerPidFile(port, savePath);
        if (!File.Exists(pidFilePath))
        {
            Console.WriteLine($"Scanning for process listening on port {port}...");
            if (PidFile.TryFindPidListeningOnPort(port, out var foundPid))
            {
                // The finder is best-effort: hold a verified per-PID check on
                // BOTH stop paths before signalling any process.
                if (!PidFile.IsServerProcess(foundPid) || !PidFile.IsProcessListeningOnPort(foundPid, port))
                {
                    Console.WriteLine($"Found process (PID: {foundPid}) on port {port} is not a verified server; refusing to terminate.");
                    return;
                }
                string foundName = "process";
                try { using var pn = Process.GetProcessById(foundPid); foundName = pn.ProcessName; } catch { }
                Console.WriteLine($"Found process {foundName} (PID: {foundPid}) listening on port {port}...");
                try
                {
                    var proc2 = Process.GetProcessById(foundPid);
                    Console.Write($"Stopping server process with PID: {foundPid}...");
                    ProcessHelper.RequestTerminate(proc2);
                    if (!await ProcessHelper.WaitForExitDotsAsync(proc2, 30))
                    {
                        Console.WriteLine();
                        Console.Write("Process did not stop in time. Killing...");
                        try { proc2.Kill(entireProcessTree: false); } catch (Exception ex) { Console.WriteLine($" Failed: {ex.Message}"); return; }
                        await ProcessHelper.WaitForExitDotsAsync(proc2, 30);
                    }
                    Console.WriteLine(" Done.");
                    try
                    {
                        var cwdL = new FileInfo($"/proc/{foundPid}/cwd").LinkTarget; if (!string.IsNullOrEmpty(cwdL)) { var pf = Path.Combine(cwdL, "save", "server.pid"); if (File.Exists(pf)) try { File.Delete(pf); } catch { } }
                    }
                    catch { }
                    return;
                }
                catch (Exception ex) { Console.WriteLine($"Error stopping found process: {ex.Message}"); return; }
            }
            Console.WriteLine("No server process found.");
            return;
        }
        int? pid = null;
        try { pid = int.Parse(File.ReadAllText(pidFilePath, Encoding.UTF8).Trim()); } catch { Console.WriteLine("Invalid PID file content."); }
        if (pid != null)
        {
            Process? proc = null;
            try { proc = Process.GetProcessById(pid.Value); }
            catch (ArgumentException) { Console.WriteLine("Process from PID file not found; removing stale PID file."); try { File.Delete(pidFilePath); } catch { } return; }
            catch (Exception ex) { Console.WriteLine($"Could not inspect PID {pid.Value}: {ex.Message}"); return; }
            bool listening = PidFile.IsProcessListeningOnPort(pid.Value, port);
            if (!listening) listening = PidFile.IsPortListening(port) && PidFile.IsServerProcess(pid.Value);
            if (!listening)
            {
                Console.WriteLine($"PID {pid} is not listening on port {port}; refusing to terminate an unverified process.");
                return;
            }
            bool isServer = PidFile.IsServerProcess(pid.Value);
            if (!isServer)
            {
                Console.WriteLine($"PID {pid} is not listening on port {port}; refusing to terminate an unverified process.");
                return;
            }
            Console.Write($"Stopping server process with PID: {pid}...");
            ProcessHelper.RequestTerminate(proc);
            if (!await ProcessHelper.WaitForExitDotsAsync(proc, 50))
            {
                Console.Write(" Timeout! Force killing...");
                try { proc.Kill(entireProcessTree: false); } catch (Exception ex) { Console.WriteLine($" Failed: {ex.Message}"); return; }
                await ProcessHelper.WaitForExitDotsAsync(proc, 30);
            }
            Console.WriteLine(" Done.");
            if (File.Exists(pidFilePath))
            {
                try
                {
                    bool stillRunning = false;
                    try { stillRunning = !proc.HasExited; } catch { }
                    if (!stillRunning) File.Delete(pidFilePath);
                    else Console.WriteLine("\nWarning: Process still exists after kill.");
                }
                catch { }
            }
        }
    }

    // Pin-compat shims: tests address create/new through StopHandler.
    public static Task HandleCreateAsync(string[] a) => CreateHandler.HandleCreateAsync(a);
    public static Task<bool> HandleNewAsync(string[] a) => NewHandler.HandleNewAsync(a);
}
