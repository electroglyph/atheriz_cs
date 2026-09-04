// Port of atheriz/commands/unloggedin/create.py:67
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class CreateAccountCommand : Command
{
    public override string Key => "create";
    public override string Desc => "Create a new account.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!Settings.AtherizSettings.Global.AccountCreationEnabled) { caller.Msg("Account creation is not enabled."); return; }
        // Sync stub: expects args as string "name password" for test convenience; real flow is async prompts via Session.Prompt
        var text = args as string ?? "";
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            caller.Msg("Usage: create <account_name> <password> (interactive prompts in real server).");
            return;
        }
        string name = parts[0];
        string password = parts[1];
        var err = Validation.ValidateAccountName(name);
        if (err != null) { caller.Msg(err); return; }
        err = Validation.ValidatePassword(password);
        if (err != null) { caller.Msg(err); return; }
        try
        {
            // check uniqueness via ObjectRegistry
            var exists = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0;
            if (exists) { caller.Msg($"Account with this name ({name}) already exists."); return; }
            var account = Account.Create(name, password);
            // Account.Create already does AddObjectUnique
            if (caller is BaseConnection conn && conn.Session != null)
            {
                conn.Session.Account = account;
                conn.SendCommand("logged_in");
            }
            caller.Msg($"Account {name} created.");
        }
        catch (InvalidOperationException ex) { caller.Msg(ex.Message); }
        catch (ArgumentException ex) { caller.Msg(ex.Message); }
    }
    // Async version for real server (mirrors Python's async run)
    public async Task RunAsync(BaseConnection caller)
    {
        var settings = Settings.AtherizSettings.Global;
        if (!settings.AccountCreationEnabled) { caller.Msg("Account creation is not enabled."); return; }
        string host = caller.ClientHost ?? "?";
        string rateKey = host != "?" ? host : caller.SessionId ?? caller.GetHashCode().ToString();
        double now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        if (!ObjectRegistry.TryReserveCreationCooldown("account", rateKey, now, settings.CreationCooldown))
        { caller.Msg("Creation is temporarily rate-limited. Please try again later."); return; }
        string name = await caller.Session.Prompt("Enter an account name:");
        name = name.Trim();
        var err = Validation.ValidateAccountName(name);
        if (err != null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
        string password = await caller.Session.Prompt("Enter a password:");
        err = Validation.ValidatePassword(password);
        if (err != null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
        try
        {
            var account = Account.Create(name, password);
            double now2 = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
            ObjectRegistry.ApplyCreationCooldown("account", rateKey, now2, settings.CreationCooldown);
            caller.Session.Account = account;
            caller.SendCommand("logged_in");
            if (settings.CharCreationEnabled)
            {
                try { await ConnectCommand.CharSelectionAsync(caller, account); }
                catch (Exception ex) { Console.Error.WriteLine($"[Create] char_selection failed: {ex}"); }
            }
            else
            {
                caller.Msg("Account created. Character creation is not enabled.");
            }
        }
        catch (Exception ex) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(ex.Message); }
    }
}
