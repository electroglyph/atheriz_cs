using Atheriz.Core.Settings;
// Port of atheriz/commands/unloggedin/guest.py:134
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Network;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class GuestCommand : Command
{
    public override string Key => "guest";
    public override string Desc => "Create a temporary guest character and enter the game.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!Settings.AtherizSettings.Global.GuestEnabled) { caller.Msg("Guest accounts are not enabled."); return; }
        var text = args as string ?? "";
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string name = parts.Length > 0 ? parts[0] : "";
        if (string.IsNullOrWhiteSpace(name)) { caller.Msg("Usage: guest <name> (interactive in real server)."); return; }
        var err = Validation.ValidateCharacterName(name);
        if (err != null) { caller.Msg(err); return; }
        if (ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0) { caller.Msg($"Character with this name ({name}) already exists."); return; }
        var character = GameObject.Create(name, "", isPc: true);
        character.IsTemporary = true;
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
        if (caller is BaseConnection conn && conn.Session != null)
        {
            // atomic puppet assignment mirroring guest.py:118-126
            bool notAvailable = false;
            character.SyncRoot.EnterReadLock();
            try { if (character.Session != null || character.IsDeleted) notAvailable = true; }
            finally { character.SyncRoot.ExitReadLock(); }
            if (notAvailable) { caller.Msg("This character is not available."); return; }
            lock (conn.Session.Lock)
            {
                character.SyncRoot.EnterWriteLock();
                try
                {
                    if (character.Session != null || character.IsDeleted) { notAvailable = true; }
                    else
                    {
                        conn.Session.Puppet = character;
                        character.Session = conn.Session;
                        conn.Session.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    }
                }
                finally { character.SyncRoot.ExitWriteLock(); }
            }
            if (notAvailable) { caller.Msg("This character is not available."); return; }
            var nh = NodeHandler.GetCurrent();
            var home = nh?.GetNode(AtherizSettings.Global.DefaultHome);
            if (home != null) { character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord); character.MoveTo(home); }
            try { character.AtPostPuppet(); } catch { }
            caller.Msg($"Guest {name} created.");
        }
        else caller.Msg($"Guest {name} created (no session).");
    }
    public async Task RunAsync(BaseConnection caller)
    {
        var settings = Settings.AtherizSettings.Global;
        if (!settings.GuestEnabled) { caller.Msg("Guest accounts are not enabled."); return; }
        string host = caller.ClientHost ?? "?";
        string rateKey = host != "?" ? host : caller.SessionId ?? caller.GetHashCode().ToString();
        double now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        if (!ObjectRegistry.TryReserveCreationCooldown("guest", rateKey, now, settings.CreationCooldown))
        { caller.Msg("Creation is temporarily rate-limited. Please try again later."); return; }
        string name = await caller.Session.Prompt("Enter a name for your guest character:");
        name = name.Trim();
        var err = Validation.ValidateCharacterName(name);
        if (err != null) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg(err); return; }
        string gender = await caller.Session.Prompt("Enter your character's gender:");
        if (string.IsNullOrWhiteSpace(gender)) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg("Gender cannot be empty."); return; }
        gender = gender.Trim();
        if (string.IsNullOrEmpty(gender)) { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg("Gender cannot be empty."); return; }
        string desc = await caller.Session.Prompt("Enter a short description of your character:");
        if (ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0)
        { ObjectRegistry.ClearCreationCooldown(rateKey); caller.Msg($"Character with this name ({name}) already exists."); return; }
        var character = GameObject.Create(name, desc, isPc: true);
        character.IsTemporary = true;
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
        ObjectRegistry.ApplyCreationCooldown("guest", rateKey, now2, settings.CreationCooldown);
        // puppet with lock mirroring Python
        character.SyncRoot.EnterReadLock();
        bool notAvail = false;
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
        caller.Msg($"Guest {name} created.");
    }
}
