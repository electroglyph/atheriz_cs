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
        if (!settings.CharCreationEnabled) { caller.Msg("Character creation is not enabled."); return; }
        // sync stub for tests: expects "name gender desc"
        var text = args as string ?? "";
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { caller.Msg("Usage: new <name> (interactive in real server)."); return; }
        string name = parts[0];
        var err = Validation.ValidateCharacterName(name);
        if (err != null) { caller.Msg(err); return; }
        if (caller is BaseConnection conn && conn.Session?.Account is Account acc)
        {
            if (acc.Characters.Count >= settings.MaxCharacters) { caller.Msg($"You already have {settings.MaxCharacters} characters."); return; }
            var exists = ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0;
            if (exists) { caller.Msg($"Character with this name ({name}) already exists."); return; }
            var character = GameObject.Create(name, "", isPc: true);
            character.Gender = parts.Length > 1 ? parts[1] : "neutral";
            try
            {
                ObjectRegistry.AddObjectUnique(character, o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase), $"Character with this name ({name}) already exists.");
            }
            catch (InvalidOperationException ex)
            {
                caller.Msg(ex.Message);
                try { character.IsDeleted = true; } catch { }
                return;
            }
            acc.AddCharacter(character);
            // puppet assignment with lock and AtPostPuppet
            bool notAvail = false;
            character.SyncRoot.EnterReadLock();
            try { if (character.Session != null || character.IsDeleted) notAvail = true; }
            finally { character.SyncRoot.ExitReadLock(); }
            if (notAvail) { caller.Msg("This character is not available."); return; }
            // set gender already
            lock (conn.Session.Lock)
            {
                character.SyncRoot.EnterWriteLock();
                try
                {
                    if (character.Session != null || character.IsDeleted) notAvail = true;
                    else
                    {
                        conn.Session.Puppet = character;
                        character.Session = conn.Session;
                        conn.Session.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    }
                }
                finally { character.SyncRoot.ExitWriteLock(); }
            }
            if (notAvail) { caller.Msg("This character is not available."); return; }
            var nh = NodeHandler.GetCurrent();
            var home = nh?.GetNode(settings.DefaultHome);
            if (home != null) { character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord); character.MoveTo(home); }
            try { character.AtPostPuppet(); } catch { }
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
        if (!settings.CharCreationEnabled) { caller.Msg("Character creation is not enabled."); return; }
        var account = caller.Session.Account as Account;
        if (account == null) { caller.Msg("You must be logged in first."); return; }
        if (account.Characters.Count >= settings.MaxCharacters) { caller.Msg($"You already have {settings.MaxCharacters} characters."); return; }
        string host = caller.ClientHost ?? "?";
        string rateKey = host != "?" ? host : caller.SessionId ?? caller.GetHashCode().ToString();
        double now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        if (!ObjectRegistry.TryReserveCreationCooldown("character", rateKey, now, settings.CreationCooldown))
        { caller.Msg("Creation is temporarily rate-limited. Please try again later."); return; }
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
            try { character.IsDeleted = true; } catch { }
            return;
        }
        double now2 = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        ObjectRegistry.ApplyCreationCooldown("character", rateKey, now2, settings.CreationCooldown);
        account.AddCharacter(character);
        // puppet with lock
        bool notAvail = false;
        character.SyncRoot.EnterReadLock();
        try { if (character.Session != null || character.IsDeleted) notAvail = true; }
        finally { character.SyncRoot.ExitReadLock(); }
        if (notAvail) { caller.Msg("This character is not available."); return; }
        lock (caller.Session.Lock)
        {
            character.SyncRoot.EnterWriteLock();
            try
            {
                if (character.Session != null || character.IsDeleted) notAvail = true;
                else
                {
                    caller.Session.Puppet = character;
                    character.Session = caller.Session;
                    caller.Session.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }
            finally { character.SyncRoot.ExitWriteLock(); }
        }
        if (notAvail) { caller.Msg("This character is not available."); return; }
        var nh = NodeHandler.GetCurrent();
        var home = nh?.GetNode(settings.DefaultHome);
        if (home != null) { character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord); character.MoveTo(home); }
        try { character.AtPostPuppet(); } catch { }
        caller.Msg($"Character {name} created and puppeted.");
    }
}
