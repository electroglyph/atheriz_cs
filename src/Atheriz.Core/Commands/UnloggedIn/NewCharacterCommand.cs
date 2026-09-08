// Port of atheriz/commands/unloggedin/new.py:132
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class NewCharacterCommand : Command
{
    public override string Key => "new";
    public override string Desc => "Create a new character for your account.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        var settings = Settings.AtherizSettings.Global;
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Character creation is not enabled."); return; }
        if (!CreationCooldownHelper.TryReserve(caller, "character")) return;
        // sync stub for tests: expects "name gender desc"
        var text = args as string ?? "";
        var parts = Command.SplitStubArgs(text);
        if (parts.Count == 0) { CreationCooldownHelper.Clear(caller); caller.Msg("Usage: new <name> (interactive in real server)."); return; }
        string name = parts[0];
        var err = Validation.ValidateCharacterName(name);
        if (err != null) { CreationCooldownHelper.Clear(caller); caller.Msg(err); return; }
        if (caller is BaseConnection conn && conn.Session?.Account is Account acc)
        {
            if (acc.Characters.Count >= settings.MaxCharacters) { CreationCooldownHelper.Clear(caller); caller.Msg($"You already have {settings.MaxCharacters} characters."); return; }
            var exists = ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0;
            if (exists) { CreationCooldownHelper.Clear(caller); caller.Msg($"Character with this name ({name}) already exists."); return; }
            // Desc is the remainder after name+gender (was dropped as "" before).
            string desc = parts.Count > 2 ? string.Join(" ", parts.Skip(2)) : "";
            var character = GameObject.Create(name, desc, isPc: true);
            character.Gender = parts.Count > 1 ? parts[1] : "neutral";
            try
            {
                ObjectRegistry.AddObjectUnique(character, o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase), $"Character with this name ({name}) already exists.");
            }
            catch (InvalidOperationException ex)
            {
                CreationCooldownHelper.Clear(caller);
                caller.Msg(ex.Message);
                try { character.IsDeleted = true; } catch (Exception) { }
                return;
            }
            CreationCooldownHelper.Apply(caller, "character");
            acc.AddCharacter(character);
            if (!SessionPuppetHelper.TryAttach(conn, character)) return;
            var nh = NodeHandler.GetCurrent();
            var home = nh?.GetNode(settings.DefaultHome);
            if (home != null) { character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord); character.MoveTo(home); }
            try { character.AtPostPuppet(); } catch (Exception) { }
            caller.Msg($"Character {name} created.");
        }
        else
        {
            // For GameObject test caller, just validate
            var exists = ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0;
            if (exists) caller.Msg($"Character with this name ({name}) already exists.");
            else caller.Msg($"Would create character {name} (no account session).");
        }
    }
    public async Task RunAsync(BaseConnection caller)
    {
        var settings = Settings.AtherizSettings.Global;
        if (!CommandDispatcher.IsUnloggedInEnabled(this)) { caller.Msg("Character creation is not enabled."); return; }
        var account = caller.Session.Account as Account;
        if (account == null) { caller.Msg("You must be logged in first."); return; }
        if (account.Characters.Count >= settings.MaxCharacters) { caller.Msg($"You already have {settings.MaxCharacters} characters."); return; }
        string rateKey = CreationCooldownHelper.RateKey(caller);
        if (!CreationCooldownHelper.TryReserve(caller, "character")) return;
        string name = await caller.Session.Prompt("Enter a name for your character:");
        name = name.Trim();
        var err = Validation.ValidateCharacterName(name);
        if (err != null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
        string gender = await caller.Session.Prompt("Enter your character's gender:");
        gender = gender.Trim();
        if (string.IsNullOrEmpty(gender)) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg("Gender cannot be empty."); return; }
        string desc = await caller.Session.Prompt("Enter a short description of your character:");
        if (ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0)
        { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg($"Character with this name ({name}) already exists."); return; }
        var character = GameObject.Create(name, desc, isPc: true);
        character.Gender = gender;
        try
        {
            ObjectRegistry.AddObjectUnique(character, o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase), $"Character with this name ({name}) already exists.");
        }
        catch (InvalidOperationException ex)
        {
            ObjectRegistry.ClearCreationCooldown(rateKey);
            caller.Msg(ex.Message);
            try { character.IsDeleted = true; } catch (Exception) { }
            return;
        }
        double now2 = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        ObjectRegistry.ApplyCreationCooldown("character", rateKey, now2, settings.CreationCooldown);
        account.AddCharacter(character);
        if (!SessionPuppetHelper.TryAttach(caller, character)) return;
        var nh = NodeHandler.GetCurrent();
        var home = nh?.GetNode(settings.DefaultHome);
        if (home != null) { character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord); character.MoveTo(home); }
        try { character.AtPostPuppet(); } catch (Exception) { }
        caller.Msg($"Character {name} created and puppeted.");
    }
}
