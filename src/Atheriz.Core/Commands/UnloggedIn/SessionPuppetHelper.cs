using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.UnloggedIn;

public static class SessionPuppetHelper
{
    // Atomic puppet assignment mirroring guest.py: with char.lock: with session.lock:
    // double-checked session/deleted, ConnTime stamp. Messages + false when unavailable.
    public static bool TryAttach(BaseConnection conn, GameObject character)
    {
        if (conn.Session is null) { conn.Msg("This character is not available."); return false; }
        var session = conn.Session;
        bool notAvailable = false;
        character.SyncRoot.EnterReadLock();
        try { if (character.Session is not null || character.IsDeleted) notAvailable = true; }
        finally { character.SyncRoot.ExitReadLock(); }
        if (notAvailable) { conn.Msg("This character is not available."); return false; }
        lock (session.Lock)
        {
            // A session past AtDisconnect accepts no new puppet: the teardown
            // already unwound the old one, so attaching here would orphan the
            // character on a dead session.
            if (session.Closed) notAvailable = true;
            else
            {
                character.SyncRoot.EnterWriteLock();
                try
                {
                    if (character.Session is not null || character.IsDeleted) notAvailable = true;
                    else
                    {
                        session.Puppet = character;
                        character.Session = session;
                        session.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    }
                }
                finally { character.SyncRoot.ExitWriteLock(); }
            }
        }
        if (notAvailable) { conn.Msg("This character is not available."); return false; }
        return true;
    }
}
