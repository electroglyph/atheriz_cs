
namespace Atheriz.Server.Cli;

public static class ReloadHandler
{
    public static async Task HandleReloadAsync(string[] a)
    {
        var settings = StopHandler.EffectiveSettingsValue;
        var port = ArgumentParser.ParsePort(a) ?? settings.WebserverPort;
        var tlsOn = !string.IsNullOrEmpty(settings.SslCertFile);
        var url = $"{(tlsOn ? "https" : "http")}://localhost:{port}/_internal/hot_reload";
        Console.WriteLine($"Triggering hot reload at {url}...");
        var sw = Stopwatch.StartNew();
        // single end-to-end token resolution — the helper below
        // locates admin.token itself, so no separate pre-lookup exists to go
        // stale. On a scheme mismatch (settings say https, server speaks
        // plaintext or vice versa) retry once with the flipped scheme.
        var resp = await ShutdownClient.PostAdminWithTlsFallbackAsync(port, settings.SecretPath, "/_internal/hot_reload", null, tlsOn).ConfigureAwait(false);
        sw.Stop();
        if (resp is null) { Console.WriteLine($"Error connecting to server at {url}"); return; }
        var body = resp.Body;
        try
        {
            var status = resp.GetStatus("ok");
            var msg = resp.GetMessage();
            if (status == "ok") { Console.WriteLine($"Success! {msg}"); Console.WriteLine($"Reload took {sw.Elapsed.TotalMilliseconds:F2}ms"); }
            else Console.WriteLine($"Failed: {msg}");
        }
        catch { Console.WriteLine($"Response: {body}"); }
    }
}
