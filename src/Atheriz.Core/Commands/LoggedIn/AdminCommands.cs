using System.IO;
using System.Net.Http;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ReloadCommand : LoggedInCommand
{
    public override string Key => "reload";
    public override string Desc => "Reload game logic and modules.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        Atheriz.Core.Objects.GameObject? channel;
        try
        {
            // the channel fetch itself can throw; keep it inside
            // try so a broken channel service still reaches the reload.
            channel = GlobalServices.GetServerChannel();
            try { channel?.Msg("Server is reloading..."); } catch (Exception) { }
            try { Atheriz.Core.ServerEvents.AtServerReload(); } catch (Exception) { }
            try { AtherizLogger.LogInformation($"Reload triggered by {go.Name} ({go.Id})"); } catch (Exception) { }
        }
        catch (Exception ex) { try { go.Msg($"Reload failed: {ex.Message}"); } catch (Exception) { } return; }
        // never block the command thread on the async reload work
        // (Python reload.py:43 runs it inline, stalling dispatch). The
        // continuation reports the first cause instead of swallowing it.
        var capturedGo = go;
        var capturedChannel = channel;
        try
        {
// use PluginReloader async API or ServerLifecycle.
            // Arguments evaluate here (not inside the async method) so a
            // throwing ticker fetch lands in this catch, as before.
            _ = FinishReloadAsync(
                Atheriz.Core.Plugins.PluginReloader.ReloadGameLogicAsync(GlobalServices.GetAsyncTicker(), AtherizSettings.Global),
                capturedGo, capturedChannel);
        }
        catch (Exception ex) { try { go.Msg($"Reload failed: {ex.Message}"); } catch (Exception) { } }
    }

    // Async form of the old ContinueWith chain: canceled reports "canceled",
    // a fault falls back to ServerLifecycle.DoReload (completed/failed), and
    // success reports the reloader's own result. Same outcome strings.
    private static async Task FinishReloadAsync(Task<string> reloadTask, Atheriz.Core.Objects.GameObject go, Atheriz.Core.Objects.GameObject? channel)
    {
        ArgumentNullException.ThrowIfNull(reloadTask);
        string result;
        try
        {
            result = await reloadTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { result = "Reload canceled."; }
        catch (Exception)
        {
            // fallback to ServerLifecycle.DoReload which handles ticker/map save
            try { Atheriz.Core.Globals.StartStop.DoReload(AtherizSettings.Global); result = "Reload completed."; }
            catch (Exception ex2) { result = $"Reload failed: {ex2.Message}"; }
        }
        if (channel is not null)
        {
            try { channel.Msg(result); } catch (Exception) { }
            try { go.Msg(result); } catch (Exception) { }
        }
        else
        {
            try { go.Msg(result); } catch (Exception) { }
        }
    }
}

public sealed class SaveCommand : LoggedInCommand
{
    public override string Key => "save";
    public override string Desc => "Save all the things.";
    public override string Category => "Admin";
    public override bool Hide => true;
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        go.Msg("Saving...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Uses the live singletons (never throwaway instances) and settings.SavePath (never hardcoded "save").
        // each save is guarded so one failure neither skips the
        // remaining saves nor leaves "Saving..." with no follow-up.
        int failures = 0;
        failures += TrySave(go, "objects", () => ObjectRegistry.SaveObjects());
        failures += TrySave(go, "map", () => GlobalServices.GetMapHandler().Save());
        failures += TrySave(go, "nodes", () => GlobalServices.GetNodeHandler().Save(force: true));
        if (AtherizSettings.Global.TimeSystemEnabled)
            failures += TrySave(go, "gametime", () => GlobalServices.GetGameTime().Save());
        sw.Stop();
        if (failures == 0) go.Msg($"Saved in {sw.Elapsed.TotalMilliseconds} milliseconds.");
        else go.Msg($"Save completed with {failures} error(s) in {sw.Elapsed.TotalMilliseconds} milliseconds.");
    }

    private static int TrySave(IMessageTarget go, string what, Action save)
    {
        try { save(); return 0; }
        catch (Exception ex) { go.Msg($"Save {what} failed: {ex.Message}"); return 1; }
    }
}

public sealed class ShutdownCommand : LoggedInCommand
{
    public override string Key => "shutdown";
    public override string Desc => "Shutdown the server.";
    public override string Category => "Admin";
    public override bool Hide => true;
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    // Loopback-tolerant client factory (mirrors ShutdownClient.PostAdminAsync
    // in Atheriz.Server, which Core cannot reference): self-signed dev certs
    // cannot chain-verify, so chain/name errors are tolerated ONLY on a
    // loopback host — never blind-trust a non-loopback peer.
    private static HttpClient CreateLoopbackClient()
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
        {
            if (req.RequestUri is not Uri u) return false;
            bool loopback = u.Host == "localhost" || u.Host == "127.0.0.1" || u.Host == "::1";
            if (errors == System.Net.Security.SslPolicyErrors.None) return true;
            if (!loopback || cert is null) return false;
            const System.Net.Security.SslPolicyErrors tolerated =
                System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors | System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch;
            return (errors & ~tolerated) == 0;
        } };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }
    // One shutdown POST attempt over the given scheme. Null on any failure
    // so the caller can retry with the flipped scheme.
    private static async Task<(bool Ok, int StatusCode, string Body)?> TryPostShutdownAsync(string scheme, int port, string token)
    {
        try
        {
            using var client = CreateLoopbackClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{scheme}://localhost:{port}/_internal/shutdown");
            req.Headers.Add("X-Admin-Token", token);
            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch { return null; }
    }
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
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
        // Fail-closed on blank (zero-byte/whitespace) token file: posting an
        // empty bearer only earns a server-side "Invalid token." after
        // "Initiating..." — refuse locally with the same shape as a missing
        // file. Core cannot reference the Server-side TryReadTokenFile
        // helper (that would circularize), so the trim+blank check is
        // duplicated here and must stay in sync with it.
        if (string.IsNullOrWhiteSpace(token))
        {
            go.Msg("Error: admin.token not found.");
            return;
        }
        // Scheme follows the same setting the server listens on
        // (SslCertFile set => https), with one flipped-scheme retry so a
        // stale setting never dials a live server on the wrong scheme.
        // capture go for thread
        var capturedGo = go;
        var capturedToken = token;
        bool tlsOn = !string.IsNullOrEmpty(settings.SslCertFile);
        string first = tlsOn ? "https" : "http";
        string second = tlsOn ? "http" : "https";
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
                    var attempt = await TryPostShutdownAsync(first, port, capturedToken).ConfigureAwait(false)
                        ?? await TryPostShutdownAsync(second, port, capturedToken).ConfigureAwait(false);
                    if (attempt is null)
                    {
                        capturedGo.Msg("Error connecting to shutdown endpoint: no response (tried http and https).");
                        return;
                    }
                    var (ok, statusCode, body) = attempt.Value;
                    if (ok)
                    {
                        // No eager AtServerStop here: the route's own
                        // DoShutdown fires at_server_stop exactly once. The
                        // old eager call ran the hooks a second time — and
                        // ran them even when the request then failed.
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
                        capturedGo.Msg($"Shutdown failed with HTTP {statusCode}");
                    }
                }
                catch (Exception ex) { capturedGo.Msg($"Shutdown error: {ex.Message}"); }
            });
        }
        catch (Exception ex) { go.Msg($"Shutdown error: {ex.Message}"); }
    }
}

public sealed class SpamCommand : LoggedInCommand
{
    public override string Key => "spam";
    public override string Desc => "Create multiple test accounts and characters.";
    public override bool Hide => true;
    public override string Category => "Admin";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Count, type: typeof(int), help: "Number of accounts to create"); }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        if (pa[ParsedArgKeys.Count] is null) { go.Msg("Usage: spam <count>"); return; }
        // No floor: count 0/negative runs an empty loop with the count
        // messages below (established behavior, not a refusal).
        _ = CommandHelpers.TryGetCount(pa, ParsedArgKeys.Count, 0, null, out int count);
        if (count > 1000) { go.Msg("Maximum count is 1000."); return; }
        go.Msg($"Creating {count} accounts and characters...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var settings = AtherizSettings.Global;
        var home = go.ResolveLocationObject();
        // Hoisted once: per-index names cannot self-collide within one run,
        // so the old per-iteration FilterBy saw the same answer every time.
        // Both pools are hoisted: an account name AND a character name each
        // live in one namespace per kind, so a pre-existing PC (or account)
        // holding char{idx}/account{idx} skips instead of forking a
        // duplicate (duplicate PC names cause permanent Multiple-matches
        // ambiguity on search).
        var existingNames = new HashSet<string>(
            ObjectRegistry.FilterBy(o => o.IsAccount).Select(o => o.Name),
            StringComparer.OrdinalIgnoreCase);
        var existingPcNames = new HashSet<string>(
            ObjectRegistry.FilterBy(o => o.IsPc).Select(o => o.Name),
            StringComparer.OrdinalIgnoreCase);
        List<(string a, string c)> created = [];
        for (int idx = 1; idx <= count; idx++)
        {
            string an = $"account{idx}";
            string pw = $"password{idx}";
            string cn = $"char{idx}";
            try
            {
                if (existingNames.Contains(an)) { go.Msg($"Account '{an}' already exists, skipping..."); continue; }
                if (existingPcNames.Contains(cn)) { go.Msg($"Character '{cn}' already exists, skipping..."); continue; }
                var account = Account.Create(an, pw);
                if (account is null) { go.Msg($"Account '{an}' already exists, skipping..."); continue; }
                var character = GameObject.Create(cn, "", isPc: true, isMapable: true);
                character.Symbol = "A";
                character.Home = Persistence.Dto.LocationRef.FromCoord(settings.DefaultHome);
                if (home is Node node) character.MoveTo(node);
                else if (home is not null) character.MoveTo(home);
                account.AddCharacter(character);
                ObjectRegistry.AddObject(character);
                created.Add((an, cn));
            }
            catch (InvalidOperationException) { go.Msg($"Account '{an}' already exists, skipping..."); }
            catch (Exception ex) { go.Msg($"Failed {an}: {ex.Message}"); }
        }
        // single save after the loop, not O(n) saves inside it.
        // Best-effort: spam's job is creating the accounts in-registry; a
        // bad save path reports instead of discarding the created accounts.
        try { ObjectRegistry.SaveObjects(Atheriz.Core.Persistence.AtherizDbContextFactory.ResolveSavePath(settings)); }
        catch (Exception ex) { go.Msg($"Save failed: {ex.Message}"); }
        var credsFile = Path.Combine(settings.SavePath, "spam_accounts.txt");
        try
        {
            Directory.CreateDirectory(settings.SavePath);
            using var f = new StreamWriter(credsFile, false, System.Text.Encoding.UTF8);
            f.NewLine = "\n";
            // passwords are never persisted — account/character
            // names only (Python's plaintext password column removed).
            f.Write("# Account Name | Character Name\n");
            foreach (var (a, c) in created)
                f.Write($"{a}|{c}\n");
        }
        catch (Exception) { }
        sw.Stop();
        go.Msg($"Created {created.Count} accounts/chars in {sw.Elapsed.TotalMilliseconds} milliseconds. Names saved to {credsFile}");
    }
}
