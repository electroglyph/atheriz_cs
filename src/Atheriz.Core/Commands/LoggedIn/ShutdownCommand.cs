// Port of atheriz/commands/loggedin/shutdown.py:79
using System.Net.Http;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ShutdownCommand : Command
{
    public override string Key => "shutdown";
    public override string Desc => "Shutdown the server.";
    public override string Category => "Admin";
    public override bool Hide => true;
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    // One shared client: a fresh HttpClient per shutdown burns a socket
    // pool per invocation and strands sockets in TIME_WAIT after dispose.
    private static readonly HttpClient SharedShutdownClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        go.Msg("Initiating server shutdown...");
        var settings = AtherizSettings.Global;
        int port = settings.WebserverPort;
        string secretPath = settings.SecretPath;
        var tokenFile = Path.Combine(secretPath, "admin.token");
        if (!File.Exists(tokenFile))
        {
            go.Msg("Error: admin.token not found.");
            return;
        }
        string token;
        try { token = File.ReadAllText(tokenFile).Trim(); }
        catch (Exception ex) { go.Msg($"Error reading token: {ex.Message}"); return; }
        string url = $"http://localhost:{port}/_internal/shutdown";
        // capture go for thread
        var capturedGo = go;
        var capturedToken = token;
        var capturedUrl = url;
        try
        {
            // Task pool thread, not a raw Thread: same background semantics
            // without a per-invocation OS thread. Replies go through Msg,
            // whose log append + session read hold as one critical section
            // with the socket send outside the lock — safe cross-thread.
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = SharedShutdownClient;
                    var req = new HttpRequestMessage(HttpMethod.Post, capturedUrl);
                    req.Headers.Add("X-Admin-Token", capturedToken);
                    var resp = await client.SendAsync(req).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        // fire the stop hooks only once the shutdown
                        // is confirmed. Python runs at_server_stop() eagerly
                        // (shutdown.py:53, before the request); if the request
                        // then fails, hooks already ran for a live server.
                        try { Atheriz.Core.ServerEvents.AtServerStop(); } catch (Exception) { }
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(body);
                            string? status = null, msg = null;
                            // Non-object roots threw in the old EnumerateObject
                            // loop (landing on the success message below) —
                            // keep the same outcome.
                            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                                throw new InvalidOperationException("Unexpected shutdown response.");
                            if (doc.RootElement.TryGetProperty("status", out var statusProp)) status = statusProp.GetString();
                            if (doc.RootElement.TryGetProperty("message", out var messageProp)) msg = messageProp.GetString();
                            switch (status)
                            {
                                case "ok": capturedGo.Msg("Server shutdown initiated successfully."); break;
                                default: capturedGo.Msg($"Shutdown failed: {msg}"); break;
                            }
                        }
                        catch { capturedGo.Msg("Server shutdown initiated successfully."); }
                    }
                    else
                    {
                        capturedGo.Msg($"Shutdown failed with HTTP {(int)resp.StatusCode}");
                    }
                }
                catch (HttpRequestException ex) { capturedGo.Msg($"Error connecting to shutdown endpoint: {ex.Message}"); }
                catch (Exception ex) { capturedGo.Msg($"Shutdown error: {ex.Message}"); }
            });
        }
        catch (Exception ex) { go.Msg($"Shutdown error: {ex.Message}"); }
    }
}