using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class NewCharacterCommand : Command
{
    public override string Key => "new";
    public override string Desc => "Create a new character for your account.";
    public override bool UseParser => false;

    // Per-verb core shared by Run + RunAsync: atomic unique registration of
    // the new character. Throws InvalidOperationException on a duplicate
    // exactly like the inline call it replaces; cooldown release and
    // messaging stay at the call sites (they pair differently per path).
    // (Kept per-verb: the guest verb's twin looks identical but must stay
    // scoped to its own verb.)
    private static void RegisterNewCharacter(GameObject character, string name)
        => ObjectRegistry.AddObjectUnique(character, o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase), $"Character with this name ({name}) already exists.");

    public override void Run(CommandContext ctx)
    {
        var caller = ctx.Caller;
        var settings = Settings.AtherizSettings.Global;
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Character creation is not enabled."); return; }
        if (!CreationCooldownHelper.TryReserve(caller, "character")) return;
        // sync stub for tests: expects "name gender desc"
        var text = ctx.RawText;
        // One walk for head + verbatim tail (see CreateAccountCommand).
        var (head, descTail) = Command.SplitHeadTail(text, 2);
        if (head.Count == 0) { CreationCooldownHelper.Clear(caller); caller.Msg("Usage: new <name> (interactive in real server)."); return; }
        string name = head[0];
        var err = Validation.ValidateCharacterName(name);
        if (err is not null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
        if (caller is BaseConnection conn && conn.Session?.Account is Account acc)
        {
            if (acc.Characters.Count >= settings.MaxCharacters) { CreationCooldownHelper.Clear(caller); caller.Msg($"You already have {settings.MaxCharacters} characters."); return; }
            if (CreationValidation.PcNameExists(name)) { CreationCooldownHelper.Clear(caller); caller.Msg($"Character with this name ({name}) already exists."); return; }
            // Desc is the raw remainder after name+gender (re-joined
            // tokens would collapse interior spacing the async prompt
            // line keeps verbatim). The tail is empty exactly when there
            // is no third token, which is the old `parts.Count > 2` gate
            // (Head is capped at the first two tokens).
            string desc = descTail;
            var character = GameObject.Create(name, desc, isPc: true);
            character.Gender = head.Count > 1 ? head[1] : "neutral";
            try
            {
                RegisterNewCharacter(character, name);
            }
            catch (InvalidOperationException ex)
            {
                CreationCooldownHelper.Clear(caller);
                caller.Msg(ex.Message);
                try { character.IsDeleted = true; } catch (Exception) { }
                return;
            }
            CreationCooldownHelper.Apply(caller, "character");
            // Atomic cap reserve: the pre-check above is advisory; this
            // decides under the account write lock.
            if (!acc.TryAddCharacter(character, settings.MaxCharacters))
            {
                CreationCooldownHelper.Clear(caller);
                caller.Msg($"You already have {settings.MaxCharacters} characters.");
                try { character.IsDeleted = true; } catch (Exception) { }
                try { ObjectRegistry.RemoveObject(character); } catch (Exception) { }
                return;
            }
            if (!CharacterPuppetSetup.AttachAndHome(conn, character)) return;
            caller.Msg($"Character {name} created.");
        }
        else
        {
            // For GameObject test caller, just validate.
            // Nothing is created on this path, so release the entry reservation:
            // otherwise the next `new` from the same caller is rate-limited.
            CreationCooldownHelper.Clear(caller);
            if (CreationValidation.PcNameExists(name)) caller.Msg($"Character with this name ({name}) already exists.");
            else caller.Msg($"Would create character {name} (no account session).");
        }
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
    public async Task RunAsync(BaseConnection caller, CancellationToken ct = default)
    {
        try
        {
            var settings = Settings.AtherizSettings.Global;
            if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Character creation is not enabled."); return; }
            var account = caller.Session.Account as Account;
            if (account is null) { caller.Msg("You must be logged in first."); return; }
            if (account.Characters.Count >= settings.MaxCharacters) { caller.Msg($"You already have {settings.MaxCharacters} characters."); return; }
            if (!CreationCooldownHelper.TryReserve(caller, "character")) return;
            string name = await caller.Session.Prompt("Enter a name for your character:", false, ct).ConfigureAwait(false);
            name = name.Trim();
            var err = Validation.ValidateCharacterName(name);
            if (err is not null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
            string gender = await caller.Session.Prompt("Enter your character's gender:", false, ct).ConfigureAwait(false);
            gender = gender.Trim();
            if (string.IsNullOrEmpty(gender)) { CreationCooldownHelper.Clear(caller); caller.Msg("Gender cannot be empty."); return; }
            string desc = await caller.Session.Prompt("Enter a short description of your character:", false, ct).ConfigureAwait(false);
            if (CreationValidation.PcNameExists(name))
            { CreationCooldownHelper.Clear(caller); caller.Msg($"Character with this name ({name}) already exists."); return; }
            var character = GameObject.Create(name, desc, isPc: true);
            character.Gender = gender;
            try
            {
                RegisterNewCharacter(character, name);
            }
            catch (InvalidOperationException ex)
            {
                CreationCooldownHelper.Clear(caller);
                caller.Msg(ex.Message);
                try { character.IsDeleted = true; } catch (Exception) { }
                return;
            }
            CreationCooldownHelper.Apply(caller, "character");
            // Atomic cap reserve: the pre-check above is advisory; this
            // decides under the account write lock.
            if (!account.TryAddCharacter(character, settings.MaxCharacters))
            {
                CreationCooldownHelper.Clear(caller);
                caller.Msg($"You already have {settings.MaxCharacters} characters.");
                try { character.IsDeleted = true; } catch (Exception) { }
                try { ObjectRegistry.RemoveObject(character); } catch (Exception) { }
                return;
            }
            if (!CharacterPuppetSetup.AttachAndHome(caller, character)) return;
            caller.Msg($"Character {name} created and puppeted.");
        }
        catch (OperationCanceledException) { return; }
    }
}
