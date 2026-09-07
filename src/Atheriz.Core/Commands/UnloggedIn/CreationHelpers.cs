using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.UnloggedIn;

// Shared preamble for create.py / guest.py / new.py: cooldown reserve/clear/apply
// plus the atomic session-puppet attach (also used a 4th time by ConnectCommand).
public static class CreationCooldownHelper
{
    public static string RateKey(IMessageTarget caller)
    {
        string host = (caller as BaseConnection)?.ClientHost ?? "?";
        return caller is BaseConnection bc ? (host != "?" ? host : bc.GetHashCode().ToString()) : "?";
    }

    // Mirrors try_reserve_creation_cooldown: messages + false when rate-limited.
    public static bool TryReserve(IMessageTarget caller, string kind)
    {
        var settings = Settings.AtherizSettings.Global;
        double now = Utils.TimeProvider.MonotonicSeconds();
        if (!ObjectRegistry.TryReserveCreationCooldown(kind, RateKey(caller), now, settings.CreationCooldown))
        { caller.Msg("Creation is temporarily rate-limited. Please try again later."); return false; }
        return true;
    }

    // Mirrors clear_creation_cooldown on every validation-failure return.
    public static void Clear(IMessageTarget caller)
        => ObjectRegistry.ClearCreationCooldown(RateKey(caller));

    // Mirrors apply_creation_cooldown on success.
    public static void Apply(IMessageTarget caller, string kind)
    {
        var settings = Settings.AtherizSettings.Global;
        ObjectRegistry.ApplyCreationCooldown(kind, RateKey(caller), Utils.TimeProvider.MonotonicSeconds(), settings.CreationCooldown);
    }
}

public static class SessionPuppetHelper
{
    // Atomic puppet assignment mirroring guest.py: with char.lock: with session.lock:
    // double-checked session/deleted, ConnTime stamp. Messages + false when unavailable.
    public static bool TryAttach(BaseConnection conn, GameObject character)
    {
        if (conn.Session == null) { conn.Msg("This character is not available."); return false; }
        bool notAvailable = false;
        character.SyncRoot.EnterReadLock();
        try { if (character.Session != null || character.IsDeleted) notAvailable = true; }
        finally { character.SyncRoot.ExitReadLock(); }
        if (notAvailable) { conn.Msg("This character is not available."); return false; }
        lock (conn.Session.Lock)
        {
            character.SyncRoot.EnterWriteLock();
            try
            {
                if (character.Session != null || character.IsDeleted) notAvailable = true;
                else
                {
                    conn.Session.Puppet = character;
                    character.Session = conn.Session;
                    conn.Session.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }
            finally { character.SyncRoot.ExitWriteLock(); }
        }
        if (notAvailable) { conn.Msg("This character is not available."); return false; }
        return true;
    }
}
