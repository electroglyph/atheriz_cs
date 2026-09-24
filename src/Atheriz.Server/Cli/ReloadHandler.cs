
namespace Atheriz.Server.Cli;

public static class ReloadHandler
{
    public static async Task<int> ReloadAsync(int? portOverride)
    {
        var settings = StopHandler.EffectiveSettingsValue;
        var port = portOverride ?? settings.WebserverPort;
        var tlsOn = !string.IsNullOrEmpty(settings.SslCertFile);
        var url = $"{(tlsOn ? "https" : "http")}://localhost:{port}/_internal/hot_reload";
        Console.WriteLine($"Triggering hot reload at {url}...");
        var sw = Stopwatch.StartNew();
        // Single end-to-end token resolution — the helper locates
        // admin.token itself. On a scheme mismatch retry once flipped.
        var resp = await ShutdownClient.PostAdminWithTlsFallbackAsync(port, settings.SecretPath, "/_internal/hot_reload", null, tlsOn).ConfigureAwait(false);
        sw.Stop();
        if (resp is null) { Console.WriteLine($"Error connecting to server at {url}"); return 1; }
        var body = resp.Body;
        try
        {
            var status = resp.GetStatus("ok");
            var msg = resp.GetMessage();
            if (status == "ok") { Console.WriteLine($"Success! {msg}"); Console.WriteLine($"Reload took {sw.Elapsed.TotalMilliseconds:F2}ms"); return 0; }
            Console.WriteLine($"Failed: {msg}");
            return 1;
        }
        catch { Console.WriteLine($"Response: {body}"); return 1; }
    }
}
