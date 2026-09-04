// Port of atheriz/commands/loggedin/shutdown.py:79
using System.Net.Http;
using System.Threading;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ShutdownCommand : Command
{
    public override string Key => "shutdown";
    public override string Desc => "Shutdown the server.";
    public override string Category => "Admin";
    public override bool Hide => true;
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        go.Msg("Initiating server shutdown...");
        try { Atheriz.Core.ServerEvents.AtServerStop(); } catch { }
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
            var thread = new Thread(() =>
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var req = new HttpRequestMessage(HttpMethod.Post, capturedUrl);
                    req.Headers.Add("X-Admin-Token", capturedToken);
                    var resp = client.SendAsync(req).GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                    {
                        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(body);
                            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                            if (status == "ok") capturedGo.Msg("Server shutdown initiated successfully.");
                            else
                            {
                                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                                capturedGo.Msg($"Shutdown failed: {msg}");
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
            thread.IsBackground = true;
            thread.Name = "shutdown-request";
            thread.Start();
        }
        catch (Exception ex) { go.Msg($"Shutdown error: {ex.Message}"); }
    }
}