using System.IO;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ReloadCommand : LoggedInCommand
{
    public override string Key => "reload";
    public override string Desc => "Reload game logic and modules.";
    public override string Category => "Admin";
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
            try { if (channel is Atheriz.Core.Objects.Channel ch) ch.Send("Server is reloading..."); } catch (Exception) { }
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
                Atheriz.Core.Plugins.PluginReloader.ReloadGameLogicWithBuildAsync(GlobalServices.GetAsyncTicker(), AtherizSettings.Global),
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
        if (channel is Atheriz.Core.Objects.Channel ch)
        {
            try { ch.Send(result); } catch (Exception) { }
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
    public override string Desc => "Save all the things. `save export <file>` dumps the object rows to a JSON file.";
    public override string Category => "Admin";
    public override bool Hide => false;
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        // Uses the live singletons (never throwaway instances) and settings.SavePath (never hardcoded "save").
        if (TryExport(go, raw)) return;
        go.Msg("Saving...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // One atomic checkpoint (see CheckpointWriter): each section is
        // guarded inside, so one failure neither skips the remaining sections
        // nor leaves "Saving..." with no follow-up.
        var settings = AtherizSettings.Global;
        var failures = Persistence.CheckpointWriter.WriteCheckpoint(
            settings, "save",
            forceNodes: true,
            includeTime: settings.TimeSystemEnabled);
        foreach (var f in failures) go.Msg($"Save {f.Name} failed: {f.Message}");
        sw.Stop();
        if (failures.Count == 0) go.Msg($"Saved in {sw.Elapsed.TotalMilliseconds} milliseconds.");
        else go.Msg($"Save completed with {failures.Count} error(s) in {sw.Elapsed.TotalMilliseconds} milliseconds.");
    }

    private static bool TryExport(IMessageTarget go, string raw)
    {
        var arg = (raw ?? "").Trim();
        if (arg.StartsWith("export", StringComparison.OrdinalIgnoreCase))
            arg = arg.Substring("export".Length).Trim();
        else
            return false;
        arg = arg.Trim().Trim('"');
        if (string.IsNullOrEmpty(arg)) { go.Msg("Usage: save export <file>."); return true; }
        try
        {
            int count = Persistence.WorldExport.ExportToFile(arg);
            go.Msg($"Exported {count} objects to {arg}.");
        }
        catch (Exception ex) { go.Msg($"Export failed: {ex.Message}"); }
        return true;
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
                    var attempt = await AdminEndpointClient.TryPostAsync(first, port, capturedToken, "/_internal/shutdown").ConfigureAwait(false)
                        ?? await AdminEndpointClient.TryPostAsync(second, port, capturedToken, "/_internal/shutdown").ConfigureAwait(false);
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

public sealed class RestartCommand : LoggedInCommand
{
    public override string Key => "restart";
    public override string Desc => "Rebuild and restart the server.";
    public override string Category => "Admin";
    public override bool Hide => true;
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        go.Msg("Initiating server restart...");
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
        // Same fail-closed blank-token shape as shutdown: the server would
        // only answer "Invalid token." after "Initiating...".
        if (string.IsNullOrWhiteSpace(token))
        {
            go.Msg("Error: admin.token not found.");
            return;
        }
        // Same scheme selection and flipped-scheme retry as shutdown: the
        // setting may not match the live listener.
        var capturedGo = go;
        var capturedToken = token;
        bool tlsOn = !string.IsNullOrEmpty(settings.SslCertFile);
        string first = tlsOn ? "https" : "http";
        string second = tlsOn ? "http" : "https";
        try
        {
            // Same background semantics as shutdown: the pool thread reports
            // back through Msg, which is safe cross-thread.
            _ = Task.Run(async () =>
            {
                try
                {
                    var attempt = await AdminEndpointClient.TryPostAsync(first, port, capturedToken, "/_internal/restart").ConfigureAwait(false)
                        ?? await AdminEndpointClient.TryPostAsync(second, port, capturedToken, "/_internal/restart").ConfigureAwait(false);
                    if (attempt is null)
                    {
                        capturedGo.Msg("Error connecting to restart endpoint: no response (tried http and https).");
                        return;
                    }
                    var (ok, statusCode, body) = attempt.Value;
                    if (ok)
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(body);
                            string? status = null, msg = null;
                            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                                throw new InvalidOperationException("Unexpected restart response.");
                            if (doc.RootElement.TryGetProperty("status", out var statusProp)) status = statusProp.GetString();
                            if (doc.RootElement.TryGetProperty("message", out var messageProp)) msg = messageProp.GetString();
                            switch (status)
                            {
                                case "ok": capturedGo.Msg("Server restart initiated successfully."); break;
                                default: capturedGo.Msg($"Restart failed: {msg}"); break;
                            }
                        }
                        catch { capturedGo.Msg("Server restart initiated successfully."); }
                    }
                    else
                    {
                        capturedGo.Msg($"Restart failed with HTTP {statusCode}");
                    }
                }
                catch (Exception ex) { capturedGo.Msg($"Restart error: {ex.Message}"); }
            });
        }
        catch (Exception ex) { go.Msg($"Restart error: {ex.Message}"); }
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

/// <summary>
/// Mirrors <c>evennia/commands/default/admin.py:CmdBoot</c>.
/// </summary>
public sealed class BootCommand : LoggedInCommand
{
    public override string Key => "boot";
    public override string Desc => "Disconnect someone's session.";
    public override string Category => "Admin";
    public override string ExtraDesc => "Use 'boot <account, character, or session id>[ = <reason>]'.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        string text = (raw ?? "").Trim();
        if (text.Length == 0) { go.Msg(PrintHelp()); return; }
        int eq = text.IndexOf('=');
        string targetPart = (eq < 0 ? text : text[..eq]).Trim();
        string reason = eq < 0 ? "You have been disconnected." : text[(eq + 1)..].Trim();
        if (targetPart.Length == 0) { go.Msg(PrintHelp()); return; }
        if (reason.Length == 0) reason = "You have been disconnected.";
        var mgr = GlobalServices.GetConnectionManager();
        var kicked = new List<BaseConnection>();
        foreach (var conn in mgr.GetAllConnections())
        {
            string? sid, accName, puppetName;
            try
            {
                sid = conn.SessionId;
                accName = conn.Session.Account?.Name;
                puppetName = conn.Session.Puppet?.Name;
            }
            catch { continue; }
            if (targetPart.Equals(sid, StringComparison.OrdinalIgnoreCase)
                || targetPart.Equals(accName, StringComparison.OrdinalIgnoreCase)
                || targetPart.Equals(puppetName, StringComparison.OrdinalIgnoreCase))
                kicked.Add(conn);
        }
        if (kicked.Count == 0) { CommandHelpers.MsgCouldNotFind(go, targetPart); return; }
        foreach (var conn in kicked)
        {
            try { conn.Msg(reason); } catch (Exception) { }
            try { mgr.Disconnect(conn); } catch (Exception) { }
        }
        go.Msg($"Booted {kicked.Count} session(s) ({targetPart}).");
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/admin.py:CmdNewPassword</c>.
/// </summary>
public sealed class UserpasswordCommand : LoggedInCommand
{
    public override string Key => "userpassword";
    public override string Desc => "Set a new password on another account.";
    public override string Category => "Admin";
    public override string ExtraDesc => "Use 'userpassword <account> = <new password>'.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        string text = (raw ?? "").Trim();
        int eq = text.IndexOf('=');
        string accPart = (eq < 0 ? "" : text[..eq]).Trim();
        string password = eq < 0 ? "" : text[(eq + 1)..].Trim();
        if (accPart.Length == 0 || password.Length == 0) { go.Msg(PrintHelp()); return; }
        var acc = ObjectRegistry.FilterBy(o => o is Account a && a.Name.Equals(accPart, StringComparison.OrdinalIgnoreCase))
            .OfType<Account>()
            .FirstOrDefault();
        if (acc is null) { CommandHelpers.MsgCouldNotFind(go, accPart); return; }
        var pwErr = Validation.ValidatePassword(password, AtherizSettings.Global);
        if (pwErr is not null) { go.Msg(pwErr); return; }
        acc.SetPassword(password);
        go.Msg($"Password changed for {acc.Name}.");
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/admin.py:CmdPerm</c>, over the
/// privilege ranks this engine has instead of permission strings: viewing
/// or granting a rank. SUPERUSER ONLY.
/// </summary>
public sealed class PermCommand : LoggedInCommand
{
    public override string Key => "perm";
    public override IReadOnlyList<string> Aliases => ["setperm"];
    public override string Desc => "View or grant a privilege rank.";
    public override string Category => "Admin";
    public override string ExtraDesc => "Use 'perm <object>' to view, 'perm <object> = <player|helper|builder|admin>' to grant. Prefix the target with * to target an account by name.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        string text = (raw ?? "").Trim();
        if (text.Length == 0) { go.Msg(PrintHelp()); return; }
        int eq = text.IndexOf('=');
        string targetPart = (eq < 0 ? text : text[..eq]).Trim();
        string rankPart = eq < 0 ? "" : text[(eq + 1)..].Trim();
        if (targetPart.Length == 0) { go.Msg(PrintHelp()); return; }
        GameObject? target;
        if (targetPart.StartsWith("*", StringComparison.Ordinal))
        {
            string q = targetPart[1..].Trim();
            var accs = ObjectRegistry.FilterBy(o => o is Account a && a.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
                .OfType<Account>()
                .ToList();
            if (accs.Count == 0) { CommandHelpers.MsgCouldNotFind(go, targetPart); return; }
            if (accs.Count > 1) { go.Msg($"Multiple accounts match '{q}'."); return; }
            target = accs[0];
        }
        else
        {
            var (resolved, error) = TargetResolution.ResolveObject(go, targetPart, _ => true, CommandHelpers.FormatNoMatchFound(targetPart));
            if (error is not null) { go.Msg(error); return; }
            if (resolved is null) return;
            target = resolved;
        }
        if (eq < 0) { go.Msg($"{target.Name}: {target.PrivilegeLevel}"); return; }
        if (!TryParseRank(rankPart, out var rank)) { go.Msg(PrintHelp()); return; }
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
        target.PrivilegeLevel = rank;
        go.Msg($"{target.Name} is now {rank}.");
    }

    private static bool TryParseRank(string text, out Privilege rank)
    {
        rank = text.ToLowerInvariant() switch
        {
            "player" => Privilege.Player,
            "helper" => Privilege.Helper,
            "builder" => Privilege.Builder,
            "admin" => Privilege.Admin,
            _ => (Privilege)(-1),
        };
        return (int)rank >= 0;
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/admin.py:CmdWall</c>.
/// </summary>
public sealed class WallCommand : LoggedInCommand
{
    public override string Key => "wall";
    public override string Desc => "Announce to every connected session.";
    public override string Category => "Admin";
    public override string ExtraDesc => "Use 'wall <message>'. Reaches all sessions, including unlogged-in ones.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        string text = (raw ?? "").Trim();
        if (text.Length == 0) { go.Msg(PrintHelp()); return; }
        go.Msg("Announcing to all connected sessions...");
        GlobalServices.GetConnectionManager().Broadcast($"{go.Name} shouts: {text}");
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/system.py:CmdAccounts</c>.
/// </summary>
public sealed class AccountsCommand : LoggedInCommand
{
    public override string Key => "accounts";
    public override IReadOnlyList<string> Aliases => ["account"];
    public override string Desc => "Show account stats, or delete an account.";
    public override string Category => "Admin";
    public override string ExtraDesc => "Use 'accounts' for stats, 'accounts --delete <account>' to delete with its characters.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        string text = (raw ?? "").Trim();
        if (text.StartsWith("--delete ", StringComparison.OrdinalIgnoreCase))
        {
            DeleteAccount(go, text[9..].Trim(), PrintHelp());
            return;
        }
        int accounts = ObjectRegistry.FilterBy(o => o.IsAccount).Count;
        int characters = ObjectRegistry.FilterBy(o => o.IsPc).Count;
        int online = 0;
        try
        {
            foreach (var conn in GlobalServices.GetConnectionManager().GetAllConnections())
                try { if (conn.Session.Account is not null) online++; } catch (Exception) { }
        }
        catch (Exception) { }
        go.Msg($"Accounts: {accounts}, characters: {characters}, online sessions: {online}.");
    }

    private static void DeleteAccount(GameObject go, string name, string help)
    {
        if (name.Length == 0) { go.Msg(help); return; }
        var acc = ObjectRegistry.FilterBy(o => o is Account a && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OfType<Account>()
            .FirstOrDefault();
        if (acc is null) { CommandHelpers.MsgCouldNotFind(go, name); return; }
        // Kick live sessions first so no puppet outlives its account.
        var mgr = GlobalServices.GetConnectionManager();
        foreach (var conn in mgr.GetAllConnections())
        {
            bool theirs = false;
            try { theirs = ReferenceEquals(conn.Session.Account, acc); } catch (Exception) { }
            if (!theirs) continue;
            try { conn.Msg($"Account {acc.Name} has been deleted."); } catch (Exception) { }
            try { mgr.Disconnect(conn); } catch (Exception) { }
        }
        int chars = 0;
        foreach (int id in acc.Characters.ToList())
        {
            var c = ObjectRegistry.GetSingle(id);
            if (c is null || c.IsDeleted) continue;
            c.Delete(go);
            chars++;
        }
        acc.Delete(go);
        go.Msg($"Deleted account {acc.Name} and {chars} character(s).");
    }
}

/// <summary>
/// Cold reboot beside <c>reload</c>: full checkpoint save plus the reload
/// hooks, so non-persistent state is purged instead of hot-loaded.
/// </summary>
public sealed class ResetCommand : LoggedInCommand
{
    public override string Key => "reset";
    public override string Desc => "Save and cold-reboot the server state.";
    public override string Category => "Admin";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        go.Msg("Resetting...");
        var capturedGo = go;
        void Say(string message) { try { capturedGo.Msg(message); } catch (Exception) { } }
        try
        {
            _ = FinishResetAsync(
                RunResetCoreAsync(AtherizSettings.Global, engineBuild: null, gameBuild: null, Say, ct,
                    caller: $"{go.Name} ({go.Id})"),
                Say);
        }
        catch (Exception ex) { Say($"Reset failed: {ex.Message}"); }
    }

    private static async Task FinishResetAsync(Task runTask, Action<string> say)
    {
        ArgumentNullException.ThrowIfNull(runTask);
        ArgumentNullException.ThrowIfNull(say);
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { say("Reset canceled."); }
        catch (Exception ex) { say($"Reset failed: {ex.Message}"); }
    }

    // Build-first reset core, shared by the verb above and tests: rebuilds
    // the engine server when stale plus every stale game project (the same
    // pass CLI reset runs), then the historical checkpoint plus reload
    // hooks. Any build failure aborts before the checkpoint — live state
    // untouched. A rebuilt engine cannot swap in-process, so the outcome
    // names the required restart.
    internal static async Task RunResetCoreAsync(
        AtherizSettings settings,
        Func<string, CancellationToken, Task<bool>>? engineBuild,
        Func<string, CancellationToken, Task<bool>>? gameBuild,
        Action<string> say,
        CancellationToken cancellationToken,
        string? engineRoot = null,
        IReadOnlyList<string>? gameProjects = null,
        string? caller = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(say);
        Atheriz.Core.Plugins.BuildCoordinator.BuildAllResult build;
        try
        {
            build = await Atheriz.Core.Plugins.BuildCoordinator.EnsureAllBuiltAsync(
                settings, engineRoot, gameProjects, engineBuild, gameBuild, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) { say($"Reset failed: {ex.Message}"); return; }
        if (!build.Ok) { say($"Reset aborted: {build.Summary} Live state untouched."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var failures = Persistence.CheckpointWriter.WriteCheckpoint(
            settings, "save",
            forceNodes: true,
            includeTime: settings.TimeSystemEnabled);
        foreach (var f in failures) say($"Save {f.Name} failed: {f.Message}");
        sw.Stop();
        if (failures.Count != 0) { say($"Reset aborted with {failures.Count} save error(s)."); return; }
        try { Atheriz.Core.ServerEvents.AtServerReload(); }
        catch (Exception ex) { say($"Reset failed: {ex.Message}"); return; }
        try { AtherizLogger.LogInformation($"Reset triggered by {caller ?? "unknown"} in {sw.Elapsed.TotalMilliseconds} milliseconds."); } catch (Exception) { }
        say($"Reset complete in {sw.Elapsed.TotalMilliseconds} milliseconds.");
        if (build.EngineBuilt) say("Engine was rebuilt; restart the server to run the new server binary.");
    }
}
