// Port of atheriz/commands/unloggedin/create.py:67
using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class CreateAccountCommand : Command
{
    public override string Key => "create";
    public override string Desc => "Create a new account.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Account creation is not enabled."); return; }
        if (!CreationCooldownHelper.TryReserve(caller, "account")) return;
        // Sync stub: expects args as string "name password" for test convenience; real flow is async prompts via Session.Prompt
        var text = args as string ?? "";
        var parts = Command.SplitStubArgs(text);
        if (parts.Count < 2)
        {
            CreationCooldownHelper.Clear(caller);
            caller.Msg("Usage: create <account_name> <password> (interactive prompts in real server).");
            return;
        }
        string name = parts[0];
        // Passwords may contain spaces (Python prompts the whole line) — join, don't truncate.
        string password = string.Join(" ", parts.Skip(1));
        var err = Validation.ValidateAccountName(name);
        if (err is not null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
        err = Validation.ValidatePassword(password);
        if (err is not null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
        try
        {
            // check uniqueness via ObjectRegistry
            var exists = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0;
            if (exists) { CreationCooldownHelper.Clear(caller); caller.Msg($"Account with this name ({name}) already exists."); return; }
            var account = Account.Create(name, password);
            CreationCooldownHelper.Apply(caller, "account");
            // Account.Create already does AddObjectUnique
            if (caller is BaseConnection conn && conn.Session is not null)
            {
                conn.Session.Account = account;
                conn.SendCommand("logged_in");
            }
            caller.Msg($"Account {name} created.");
        }
        catch (InvalidOperationException ex) { CreationCooldownHelper.Clear(caller); caller.Msg(ex.Message); }
        catch (ArgumentException ex) { CreationCooldownHelper.Clear(caller); caller.Msg(ex.Message); }
    }
    // Async version for real server (mirrors Python's async run)
    public async Task RunAsync(BaseConnection caller)
    {
        var settings = Settings.AtherizSettings.Global;
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Account creation is not enabled."); return; }
        string rateKey = CreationCooldownHelper.RateKey(caller);
        if (!CreationCooldownHelper.TryReserve(caller, "account")) return;
        string name = await caller.Session.Prompt("Enter an account name:");
        name = name.Trim();
        var err = Validation.ValidateAccountName(name);
        if (err is not null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
        string password = await caller.Session.Prompt("Enter a password:");
        err = Validation.ValidatePassword(password);
        if (err is not null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
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
