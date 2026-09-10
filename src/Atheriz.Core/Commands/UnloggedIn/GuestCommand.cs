// Port of atheriz/commands/unloggedin/guest.py:134
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

    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Guest accounts are not enabled."); return; }
        if (!CreationCooldownHelper.TryReserve(caller, "guest")) return;
        var text = args as string ?? "";
        var parts = Command.SplitStubArgs(text);
        string name = parts.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(name)) { CreationCooldownHelper.Clear(caller); caller.Msg("Usage: guest <name> (interactive in real server)."); return; }
        var err = Validation.ValidateCharacterName(name);
        if (err is not null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
        if (CreationValidation.PcNameExists(name)) { CreationCooldownHelper.Clear(caller); caller.Msg($"Character with this name ({name}) already exists."); return; }
        var character = GameObject.Create(name, "", isPc: true);
        character.IsTemporary = true;
        character.Gender = parts.Count > 1 ? parts[1] : "neutral";
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
    public async Task RunAsync(BaseConnection caller)
    {
        var settings = Settings.AtherizSettings.Global;
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Guest accounts are not enabled."); return; }
        string rateKey = CreationCooldownHelper.RateKey(caller);
        if (!CreationCooldownHelper.TryReserve(caller, "guest")) return;
        string name = await caller.Session.Prompt("Enter a name for your guest character:").ConfigureAwait(false);
        name = name.Trim();
        var err = Validation.ValidateCharacterName(name);
        if (err is not null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
        string gender = await caller.Session.Prompt("Enter your character's gender:").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(gender)) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg("Gender cannot be empty."); return; }
        gender = gender.Trim();
        if (string.IsNullOrEmpty(gender)) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg("Gender cannot be empty."); return; }
        string desc = await caller.Session.Prompt("Enter a short description of your character:").ConfigureAwait(false);
        if (CreationValidation.PcNameExists(name))
        { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg($"Character with this name ({name}) already exists."); return; }
        var character = GameObject.Create(name, desc, isPc: true);
        character.IsTemporary = true;
        character.Gender = gender;
        try
        {
            RegisterGuest(character, name);
        }
        catch (InvalidOperationException ex)
        {
            ObjectRegistry.ClearCreationCooldown(rateKey);
            caller.Msg(ex.Message);
            try { character.IsDeleted = true; } catch (Exception) { }
            return;
        }
        double now2 = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        ObjectRegistry.ApplyCreationCooldown("guest", rateKey, now2, settings.CreationCooldown);
        // puppet with lock mirroring Python
        if (!CharacterPuppetSetup.AttachAndHome(caller, character)) return;
        caller.Msg($"Guest {name} created.");
    }
}
