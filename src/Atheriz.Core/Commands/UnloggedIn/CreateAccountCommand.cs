using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class CreateAccountCommand : Command
{
    public override string Key => "create";
    public override string Desc => "Create a new account.";
    public override bool UseParser => false;

    // Per-verb core shared by Run + RunAsync: account-name then password
    // validation, first error wins. Scoped to validation + creation only —
    // this verb never puppets. RunAsync calls it once per prompt (name, then
    // name+password) so prompts stay interleaved with validation exactly as
    // before; the name re-check is pure and always passes the second time.
    private static string? ValidateInputs(string name, string? password = null)
        => Validation.ValidateAccountName(name) ?? (password is null ? null : Validation.ValidatePassword(password));

    public override void Run(CommandContext ctx)
    {
        var caller = ctx.Caller;
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Account creation is not enabled."); return; }
        if (!CreationCooldownHelper.TryReserve(caller, "account")) return;
        // Sync stub: expects args as string "name password" for test convenience; real flow is async prompts via Session.Prompt
        var text = ctx.RawText;
        // One walk for head + verbatim tail: the name is quote-aware while
        // the password keeps interior spacing exactly as the async prompt
        // line keeps it (passwords are credential bytes, never re-joined).
        var (head, password) = Command.SplitHeadTail(text, 1);
        string name = head.FirstOrDefault() ?? "";
        if (head.Count == 0 || password.Length == 0)
        {
            CreationCooldownHelper.Clear(caller);
            caller.Msg("Usage: create <account_name> <password> (interactive prompts in real server).");
            return;
        }
        var err = ValidateInputs(name, password);
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
    /// <summary>
    /// Async entry: delegates to the existing connection wizard when the
    /// caller is a connection, else falls back to the sync stub.
    /// </summary>
    public override Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Caller is BaseConnection conn) return RunAsync(conn, ct);
        Run(ctx);
        return Task.CompletedTask;
    }
    // Async version for real server (mirrors Python's async run). Cancellation
    // ends the wizard silently: a cancelled prompt resolves to "" like
    // CancelPrompt (flowing into the existing validation paths below), while
    // a racing teardown cancels the future itself and lands in the outer
    // OperationCanceledException catch.
    public async Task RunAsync(BaseConnection caller, CancellationToken ct = default)
    {
        try
        {
            var settings = Settings.AtherizSettings.Global;
            if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Account creation is not enabled."); return; }
            string rateKey = CreationCooldownHelper.RateKey(caller);
            if (!CreationCooldownHelper.TryReserve(caller, "account")) return;
            string name = await caller.Session.Prompt("Enter an account name:", false, ct).ConfigureAwait(false);
            name = name.Trim();
            var err = ValidateInputs(name);
            if (err is not null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
            string password = await caller.Session.Prompt("Enter a password:", false, ct).ConfigureAwait(false);
            err = ValidateInputs(name, password);
            if (err is not null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
            try
            {
                var account = Account.Create(name, password);
                double now2 = global::Atheriz.Core.Utils.GameClock.MonotonicSeconds();
                ObjectRegistry.ApplyCreationCooldown("account", rateKey, now2, settings.CreationCooldown);
                caller.Session.Account = account;
                caller.SendCommand("logged_in");
                if (settings.CharCreationEnabled)
                {
                    try { await ConnectCommand.CharSelectionAsync(caller, account, ct).ConfigureAwait(false); }
                    catch (Exception ex) { AtherizLogger.LogError($"[Create] char_selection failed: {ex}"); }
                }
                else
                {
                    caller.Msg("Account created. Character creation is not enabled.");
                }
            }
            catch (Exception ex) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(ex.Message); }
        }
        catch (OperationCanceledException) { return; }
    }
}
