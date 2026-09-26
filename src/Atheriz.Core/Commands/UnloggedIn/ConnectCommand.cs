using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class ConnectCommand : Command
{
    public override string Key => "connect";
    public override string Desc => "Connect to an existing account with a password.";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.AccountName, help: "The name of the account to connect to.");
        p.AddArgument(ParsedArgKeys.Password, help: "The password for the account.");
    }
    public override void Run(CommandContext ctx)
    {
        var caller = ctx.Caller;
        var pa = ctx.Args;
        if (pa is null) { caller.Msg("Invalid arguments."); return; }
        var account = TryAuthenticate(caller, pa);
        if (account is null) return;
        if (caller is BaseConnection conn2 && conn2.Session is not null)
        {
            // One wizard per session: a second `connect` arriving before the
            // first wizard reaches its prompt would otherwise start a second
            // CharSelectionAsync loop, and the two loops force-complete each
            // other's prompt slot with "" forever.
            if (!conn2.Session.TryStartWizard()) { conn2.Msg("A character selection is already in progress."); return; }
            conn2.Session.Account = account;
            conn2.SendCommand("logged_in");
// fire-and-forget async, bound to the connection lifetime: a disconnect
// mid-wizard cancels the prompts (they resolve like CancelPrompt) instead
// of stranding the wizard on a dead session. The linked source is disposed
// when the wizard completes.
            var wizardCts = CancellationTokenSource.CreateLinkedTokenSource(conn2.RetryLifetimeToken);
            _ = Task.Run(async () =>
            {
                using (wizardCts)
                {
                    try { await CharSelectionAsync(conn2, account, wizardCts.Token).ConfigureAwait(false); }
                    catch (Exception ex) { AtherizLogger.LogError($"[Connect] char_selection failed: {ex}"); }
                    finally { conn2.Session.EndWizard(); }
                }
            });
        }
        else
        {
            // For tests where caller is GameObject acting as connection
            caller.Msg($"Welcome {account.Name}.");
        }
    }

    // Credential checks shared by the sync entry and the async entry below:
    // identical messages and side effects (ban/timing-failure bookkeeping),
    // null when the login must stop. The session/wizard tail stays per entry
    // (fire-and-forget vs awaited).
    private static Account? TryAuthenticate(IMessageTarget caller, GameArgumentParser.ParsedArgs pa)
    {
        string accountName = pa.GetString(ParsedArgKeys.AccountName) ?? "";
        string password = pa.GetString(ParsedArgKeys.Password) ?? "";
        // This command is normally async; in C# we provide sync stub that checks password via ObjectRegistry
        var accounts = ObjectRegistry.FilterBy(x => x.IsAccount && x.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        if (accounts.Count == 0)
        {
            try { Account.HashPassword(password); } catch (Exception) { }
            caller.Msg("Invalid password.");
            return null;
        }
        if (accounts.Count > 1) { caller.Msg("Error: Please contact server admin."); return null; }
        var account = (Account)accounts[0];
        if (account.IsBanned)
        {
            caller.Msg($"You have been banned from this server. Reason: {account.BanReason ?? "None specified"}");
            if (caller is BaseConnection conn) conn.Close();
            return null;
        }
        if (!account.CheckPassword(password))
        {
            string host = (caller as BaseConnection)?.ClientHost ?? "?";
            int attempts = 0;
            if (host != "?")
            {
                // Atomic increment under the dict lock (F005) — no snapshot alloc, no lost updates.
                attempts = ObjectRegistry.FailedLogins.AddOrUpdate(host, (exists, cur) => cur + 1);
            }
            try { if (caller is BaseConnection bc) bc.FailedLoginAttempts++; } catch (Exception) { }
            caller.Msg("Invalid password.");
            int fail2 = (caller as BaseConnection)?.FailedLoginAttempts ?? 0;
            var settings = AtherizSettings.Global;
            if (attempts >= settings.MaxLoginAttempts || fail2 >= settings.MaxLoginAttempts)
            {
                caller.Msg("Too many failed login attempts. Please try again later.");
                if (caller is BaseConnection c2) c2.Close();
                if (host != "?") ObjectRegistry.BanIp(host, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + settings.LoginAttemptCooldown);
            }
            return null;
        }
        string host2 = (caller as BaseConnection)?.ClientHost ?? "?";
        if (host2 != "?") ObjectRegistry.FailedLogins.Remove(host2);
        try { if (caller is BaseConnection bc) bc.FailedLoginAttempts = 0; } catch (Exception) { }
        // Re-verify the ban after the PBKDF2 window: a ban landing
        // mid-hash must not bind with the stale pre-hash clearance above.
        if (account.IsBanned)
        {
            caller.Msg($"You have been banned from this server. Reason: {account.BanReason ?? "None specified"}");
            if (caller is BaseConnection conn2) conn2.Close();
            return null;
        }
        return account;
    }

    /// <summary>
    /// Async entry: same credential checks as <see cref="Run"/>, then the
    /// character-selection wizard awaited inline under the passed token
    /// linked with the connection lifetime (instead of the fire-and-forget
    /// <c>Task.Run</c> the sync entry uses).
    /// </summary>
    public override async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Caller is not BaseConnection conn || conn.Session is null) { Run(ctx); return; }
        var pa = ctx.Args;
        if (pa is null) { conn.Msg("Invalid arguments."); return; }
        var account = TryAuthenticate(conn, pa);
        if (account is null) return;
        if (!conn.Session.TryStartWizard()) { conn.Msg("A character selection is already in progress."); return; }
        conn.Session.Account = account;
        conn.SendCommand("logged_in");
        using var wizardCts = CancellationTokenSource.CreateLinkedTokenSource(ct, conn.RetryLifetimeToken);
        try { await CharSelectionAsync(conn, account, wizardCts.Token).ConfigureAwait(false); }
        catch (Exception ex) { AtherizLogger.LogError($"[Connect] char_selection failed: {ex}"); }
        finally { conn.Session.EndWizard(); }
    }

    internal static async Task CharSelectionAsync(BaseConnection caller, Account account, CancellationToken ct = default)
    {
        var settings = AtherizSettings.Global;
        while (true)
        {
            // Linked-lifetime exit: a cancelled prompt resolves to "" (it
            // never throws), so without this the wizard would answer its own
            // prompts with "" and loop "Invalid choice." on a dead session
            // forever. Never true when ct cannot cancel.
            if (ct.IsCancellationRequested) return;
            GameObject? puppetCheck;
            lock (caller.Session.Lock) puppetCheck = caller.Session.Puppet;
            if (puppetCheck is not null) break;

            var chars = ObjectRegistry.Get(account.Characters);
            // Filter to GameObjects that still exist (not deleted)
            chars = chars.Where(c => !c.IsDeleted).ToList();

            string text = "Please select a character to play: \r\n";
            for (int x = 0; x < chars.Count; x++)
            {
                var c = chars[x];
                string tag = c.IsBanned ? " [banned]" : "";
                text += $"{x}. {c.Name}{tag}\r\n";
            }
            if (settings.CharCreationEnabled)
                text += "\r\nor type 'new' to create a new character\r\n";
            if (chars.Count == 0 && !settings.CharCreationEnabled)
            {
                caller.Msg("This account has no characters to play.");
                return;
            }
            var playable = chars.Where(c => !c.IsBanned).ToList();
            if (playable.Count == 0 && !settings.CharCreationEnabled)
            {
                caller.Msg("All characters are banned.");
                return;
            }
            // Single Prompt(display): menu + prompt line in one message, so the
            // webclient prints one trailing ">" (Msg + Prompt = double prompt).
            string choice;
            try { choice = await caller.Session.Prompt(text + "Enter your choice:", false, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch { return; }
            if (choice is null) return;
            if (settings.CharCreationEnabled && choice.Trim().Equals("new", StringComparison.OrdinalIgnoreCase))
            {
                var newCmd = new NewCharacterCommand();
                try { await newCmd.RunAsync(caller, ct).ConfigureAwait(false); }
                catch (Exception ex) { AtherizLogger.LogError($"[Connect] NewCharacter failed: {ex}"); caller.Msg("Character creation failed."); }
                continue;
            }
            if (!int.TryParse(choice.Trim(), out var idx))
            {
                caller.Msg("Invalid choice.");
                continue;
            }
            if (idx < 0 || idx >= chars.Count)
            {
                caller.Msg("Invalid choice.");
                continue;
            }
            var chosen = chars[idx];
            if (chosen.IsBanned)
            {
                string msg = "That character is banned.";
                // Typed (F001): BanReason is virtual on GameObject (Account field-backed).
                string reason = chosen.BanReason;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                caller.Msg(msg);
                continue;
            }
            // at_pre_puppet check — mirrors account.at_pre_puppet(chars[choice])
            try
            {
                if (!account.AtPrePuppet(chosen))
                {
                    caller.Msg("This character is not available.");
                    continue;
                }
            }
            catch { caller.Msg("This character is not available."); continue; }

            if (!SessionPuppetHelper.TryAttach(caller, chosen)) continue;
            try { caller.Session.ConnectedAt = DateTime.UtcNow; } catch (Exception) { }
            try { chosen.AtPostPuppet(); } catch (Exception ex) { AtherizLogger.LogError($"[Connect] AtPostPuppet failed: {ex}"); }
            // In Python, char_selection loop exits after successful puppet (while puppet is None)
            break;
        }
    }
}
