using System.Diagnostics;
using System.Text.Json;

namespace Atheriz.Server.Cli;

public static class ReloadHandler
{
    public static async Task HandleReloadAsync(string[] a)
    {
        var settings = StopHandler.EffectiveSettingsValue;
        var port = ArgumentParser.ParsePort(a) ?? settings.WebserverPort;
        var tokenFile = ShutdownClient.FindTokenFile(settings.SecretPath, port);
        if (tokenFile == null) { Console.WriteLine("Error: admin.token not found. Is the server running?"); return; }
        var tlsOn = !string.IsNullOrEmpty(settings.SslCertFile);
        var url = $"{(tlsOn ? "https" : "http")}://localhost:{port}/_internal/hot_reload";
        Console.WriteLine($"Triggering hot reload at {url}...");
        var sw = Stopwatch.StartNew();
        var resp = await ShutdownClient.PostAdminAsync(port, settings.SecretPath, "/_internal/hot_reload", null, tlsOn);
        sw.Stop();
        if (resp == null) { Console.WriteLine($"Error connecting to server at {url}"); return; }
        var body = resp.Body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "ok";
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : body;
            if (status == "ok") { Console.WriteLine($"Success! {msg}"); Console.WriteLine($"Reload took {sw.Elapsed.TotalMilliseconds:F2}ms"); }
            else Console.WriteLine($"Failed: {msg}");
        }
        catch { Console.WriteLine($"Response: {body}"); }
    }
}
