using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class GuestCommand : Command
{
    public override string Key => "guest";
    public override string Desc => "Create a temporary guest character and enter the game.";
    public override bool UseParser => false;

    // Per-verb core shared by Run + RunAsync: atomic unique registration of
    // the new guest character. Throws InvalidOperationException on a
    // duplicate exactly like the inline call it replaces; cooldown release
    // and messaging stay at the call sites (they pair differently per path).
    // (Kept per-verb: the new verb's twin looks identical but must stay
    // scoped to its own verb.)
    private static void RegisterGuest(GameObject character, string name)
        => ObjectRegistry.AddObjectUnique(character, o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase), $"Character with this name ({name}) already exists.");

    public override void Run(CommandContext ctx)
    {
        var caller = ctx.Caller;
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Guest accounts are not enabled."); return; }
        if (!CreationCooldownHelper.TryReserve(caller, "guest")) return;
        var text = ctx.RawText;
        // One walk for head + verbatim tail (see CreateAccountCommand): the
        // desc keeps interior spacing exactly as the async prompt line keeps
        // it, instead of re-joined tokens which would collapse it.
        var (head, descTail) = Command.SplitHeadTail(text, 2);
        string name = head.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(name)) { CreationCooldownHelper.Clear(caller); caller.Msg("Usage: guest <name> (interactive in real server)."); return; }
        var err = Validation.ValidateCharacterName(name);
        if (err is not null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
        if (CreationValidation.PcNameExists(name)) { CreationCooldownHelper.Clear(caller); caller.Msg($"Character with this name ({name}) already exists."); return; }
        // Desc is the raw remainder after name+gender (was dropped, while
        // the async prompt path keeps it and `new` keeps it via
        // SplitHeadTail): re-joined tokens would collapse interior
        // spacing the async prompt line keeps verbatim. The tail is empty
        // exactly when there is no third token, which is the old
        // `parts.Count > 2` gate (Head is capped at the first two tokens).
        string desc = descTail;
        var character = GameObject.Create(name, desc, isPc: true);
        character.IsTemporary = true;
        character.Gender = head.Count > 1 ? head[1] : "neutral";
        try
        {
            RegisterGuest(character, name);
        }
        catch (InvalidOperationException ex)
        {
            CreationCooldownHelper.Clear(caller);
            caller.Msg(ex.Message);
            try { character.IsDeleted = true; } catch (Exception) { }
            return;
        }
        CreationCooldownHelper.Apply(caller, "guest");
        if (caller is BaseConnection conn && conn.Session is not null)
        {
            if (!CharacterPuppetSetup.AttachAndHome(conn, character)) return;
            caller.Msg($"Guest {name} created.");
        }
        else caller.Msg($"Guest {name} created (no session).");
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
            if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Guest accounts are not enabled."); return; }
            if (!CreationCooldownHelper.TryReserve(caller, "guest")) return;
            string name = await caller.Session.Prompt("Enter a name for your guest character:", false, ct).ConfigureAwait(false);
            name = name.Trim();
            var err = Validation.ValidateCharacterName(name);
            if (err is not null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
            string gender = await caller.Session.Prompt("Enter your character's gender:", false, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(gender)) { CreationCooldownHelper.Clear(caller); caller.Msg("Gender cannot be empty."); return; }
            gender = gender.Trim();
            if (string.IsNullOrEmpty(gender)) { CreationCooldownHelper.Clear(caller); caller.Msg("Gender cannot be empty."); return; }
            string desc = await caller.Session.Prompt("Enter a short description of your character:", false, ct).ConfigureAwait(false);
            if (CreationValidation.PcNameExists(name))
            { CreationCooldownHelper.Clear(caller); caller.Msg($"Character with this name ({name}) already exists."); return; }
            var character = GameObject.Create(name, desc, isPc: true);
            character.IsTemporary = true;
            character.Gender = gender;
            try
            {
                RegisterGuest(character, name);
            }
            catch (InvalidOperationException ex)
            {
                CreationCooldownHelper.Clear(caller);
                caller.Msg(ex.Message);
                try { character.IsDeleted = true; } catch (Exception) { }
                return;
            }
            CreationCooldownHelper.Apply(caller, "guest");
            // puppet with lock mirroring Python
            if (!CharacterPuppetSetup.AttachAndHome(caller, character)) return;
            caller.Msg($"Guest {name} created.");
        }
        catch (OperationCanceledException) { return; }
    }
}
