using Atheriz.Core.Settings;
// Port of atheriz/commands/unloggedin/connect.py:154
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class ConnectCommand : Command
{
    public override string Key => "connect";
    public override string Desc => "Connect to an existing account with a password.";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("account_name", help: "The name of the account to connect to.");
        p.AddArgument("password", help: "The password for the account.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { caller.Msg("Invalid arguments."); return; }
        string accountName = pa.GetString("account_name") ?? "";
        string password = pa.GetString("password") ?? "";
        // This command is normally async; in C# we provide sync stub that checks password via ObjectRegistry
        var accounts = ObjectRegistry.FilterBy(x => x.IsAccount && x.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        if (accounts.Count == 0)
        {
            try { Account.HashPassword(password); } catch { }
            caller.Msg("Invalid password.");
            return;
        }
        if (accounts.Count > 1) { caller.Msg("Error: Please contact server admin."); return; }
        var account = accounts[0] as Account ?? (Account)accounts[0];
        if (account.IsBanned)
        {
            caller.Msg($"You have been banned from this server. Reason: {account.BanReason ?? "None specified"}");
            if (caller is BaseConnection conn) conn.Close();
            return;
        }
        if (!account.CheckPassword(password))
        {
            string host = (caller as BaseConnection)?.ClientHost ?? "?";
            int attempts = 0;
            if (host != "?")
            {
                var dict = ObjectRegistry.FailedLogins.Snapshot();
                int cur = dict.TryGetValue(host, out var v) ? v : 0;
                ObjectRegistry.FailedLogins.Set(host, cur + 1);
                attempts = cur + 1;
            }
            try { if (caller is BaseConnection bc) bc.FailedLoginAttempts++; } catch { }
            caller.Msg("Invalid password.");
            int fail2 = (caller as BaseConnection)?.FailedLoginAttempts ?? 0;
            var settings = AtherizSettings.Default;
            if (attempts > settings.MaxLoginAttempts || fail2 > settings.MaxLoginAttempts)
            {
                caller.Msg("Too many failed login attempts. Please try again later.");
                if (caller is BaseConnection c2) c2.Close();
                if (host != "?") ObjectRegistry.BanIp(host, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + settings.LoginAttemptCooldown);
            }
            return;
        }
        string host2 = (caller as BaseConnection)?.ClientHost ?? "?";
        if (host2 != "?") ObjectRegistry.FailedLogins.Remove(host2);
        try { if (caller is BaseConnection bc) bc.FailedLoginAttempts = 0; } catch { }
        if (caller is BaseConnection conn2 && conn2.Session != null)
        {
            conn2.Session.Account = account;
            try { conn2.Session.AccountId = account.Id; } catch { }
            conn2.SendCommand("logged_in");
            // Port of connect.py:154 await char_selection(caller, account) — fire-and-forget async
            _ = Task.Run(async () =>
            {
                try { await CharSelectionAsync(conn2, account); }
                catch (Exception ex) { Console.Error.WriteLine($"[Connect] char_selection failed: {ex}"); }
            });
        }
        else
        {
            // For tests where caller is GameObject acting as connection
            caller.Msg($"Welcome {account.Name}.");
        }
    }

    // Port of atheriz/commands/unloggedin/connect.py:21 char_selection
    internal static async Task CharSelectionAsync(BaseConnection caller, Account account)
    {
        var settings = AtherizSettings.Default;
        while (true)
        {
            GameObject? puppetCheck;
            lock (caller.Session.Lock) puppetCheck = caller.Session.Puppet;
            if (puppetCheck != null) break;

            var chars = ObjectRegistry.Get(account.Characters).Where(o => o != null).ToList()!;
            // Filter to GameObjects that still exist (not deleted)
            chars = chars.Where(c => c != null && !c.IsDeleted).ToList();

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
            caller.Msg(text);
            string choice;
            try { choice = await caller.Session.Prompt("Enter your choice:"); }
            catch (OperationCanceledException) { return; }
            catch { return; }
            if (choice == null) return;
            if (settings.CharCreationEnabled && choice.Trim().Equals("new", StringComparison.OrdinalIgnoreCase))
            {
                var newCmd = new NewCharacterCommand();
                try { await newCmd.RunAsync(caller); }
                catch (Exception ex) { Console.Error.WriteLine($"[Connect] NewCharacter failed: {ex}"); caller.Msg("Character creation failed."); }
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
                try
                {
                    var reasonField = chosen.GetType().GetProperty("BanReason")?.GetValue(chosen) as string;
                    if (!string.IsNullOrEmpty(reasonField)) msg += $" Reason: {reasonField}";
                    else
                    {
                        // Try via flags or direct
                        var br = chosen.GetType().GetProperty("BanReason")?.GetValue(chosen) as string;
                        if (!string.IsNullOrEmpty(br)) msg += $" Reason: {br}";
                    }
                } catch { }
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

            bool success = false;
            lock (caller.Session.Lock)
            {
                // Port of connect.py:70-77 with caller.session.lock: with char.lock:
                // Need to check char.session and is_deleted under char lock
                chosen.SyncRoot.EnterWriteLock();
                try
                {
                    // Check if already puppeted or deleted
                    bool hasSession = false;
                    try { hasSession = chosen.Session != null; } catch { hasSession = false; }
                    if (hasSession || chosen.IsDeleted)
                    {
                        // will msg outside lock
                    }
                    else
                    {
                        caller.Session.Puppet = chosen;
                        try { chosen.Session = caller.Session; } catch { }
                        caller.Session.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        try { caller.Session.ConnectedAt = DateTime.UtcNow; } catch { }
                        success = true;
                    }
                }
                finally { chosen.SyncRoot.ExitWriteLock(); }
            }
            if (!success)
            {
                caller.Msg("This character is not available.");
                continue;
            }
            try { chosen.AtPostPuppet(); } catch (Exception ex) { Console.Error.WriteLine($"[Connect] AtPostPuppet failed: {ex}"); }
            // In Python, char_selection loop exits after successful puppet (while puppet is None)
            break;
        }
    }
}
