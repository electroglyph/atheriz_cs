
namespace Atheriz.Core.Commands.LoggedIn;

internal static class BanHelper
{
    internal static GameObject? ResolveTarget(GameObject caller, string name)
    {
        if (name.StartsWith("#", StringComparison.Ordinal))
        {
            var (t, err) = TargetResolution.ResolveById(caller, name);
            if (err is not null) { caller.Msg(err); return null; }
            if (!t!.IsPc)
            {
                caller.Msg("You can only ban player characters.");
                return null;
            }
            return t;
        }
        // Name search runs through the shared resolver. The filter repeats
        // the exact-name check so the world-scope union lands on exactly the
        // old global result (same match, same order, same messages); the
        // multi-line error is sent one message per line as before.
        var (target, resolveErr) = TargetResolution.ResolveObject(
            caller,
            name,
            filter: o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase),
            notFound: $"No player character found named '{name}'.",
            multiHeader: $"Multiple matches for '{name}':");
        if (resolveErr is not null)
        {
            foreach (var line in resolveErr.Split('\n')) caller.Msg(line);
            return null;
        }
        return target;
    }

    internal static GameObject? FindAccount(GameObject target)
    {
        // Atomic session/account read: the old sess-then-Account
        // two-step could mix generations across a swap. One snapshot pair.
        var sess = target.Session;
        if (sess is not null)
        {
            var (acct, _) = sess.GetAccountPair();
            if (acct is GameObject go) return go;
        }
        var accounts = ObjectRegistry.FilterBy(x => x.IsAccount && (x as Account)?.Characters.Contains(target.Id) == true);
        return accounts.FirstOrDefault();
    }

    internal static string? GetHost(GameObject target)
    {
        // Atomic session/connection read: the old three-step could hand
        // a fresh connection's host for a stale session's ban (wrong-IP ban
        // on reconnect). The pair below never mixes generations.
        var sess = target.Session;
        if (sess is null) return null;
        Atheriz.Core.Network.BaseConnection? conn;
        lock (sess.Lock) { conn = sess.Connection; }
        if (conn is null) return null;
        var host = conn.ClientHost;
        if (string.IsNullOrEmpty(host) || host == "?") return null;
        return host;
    }

    // Atomic live-connection snapshot for the ban kick: the old
    // Session-then-Connection two-step could Msg+Close a swapped-in fresh
    // connection, or miss the kick entirely. Fails closed (false) when the
    // target re-homed mid-kick or the session died — the caller reports it
    // instead of acting on a torn pair. Lock order is session -> object
    // (same as Puppet/Unpuppet/AtDisconnect).
    internal static bool TryGetLiveConn(GameObject t, out Atheriz.Core.Objects.Session? sess, out Atheriz.Core.Network.BaseConnection? conn)
    {
        sess = t.Session;
        conn = null;
        if (sess is null) return false;
        lock (sess.Lock)
        {
            if (!ReferenceEquals(t.Session, sess)) return false;
            if (sess.Closed) return false;
            conn = sess.Connection;
            return conn is not null;
        }
    }

    // Single home for the account-characters fetch repeated across the
    // ban/unban verbs (one verb fetched the same id list twice). Read-only
    // snapshot in, fresh list out; a non-account holder yields the same
    // empty list the old inline `as Account` guards produced.
    internal static List<GameObject> GetAccountChars(GameObject acct)
        => acct is Account acc ? ObjectRegistry.Get(acc.Characters.ToList()) : [];

    // Atomic scope-state apply: ban-vs-unban interleaves used to set
    // each member under its own hold, leaving half-banned accounts. All
    // members share one ordered write hold (Id order, matching the object
    // leg of MoveTo's CompareLockOrder), so concurrent scopes serialize.
    internal static void ApplyScopeState(List<GameObject> scope, bool banned, string? reason)
    {
        var ordered = scope.Where(o => o is not null).Distinct().OrderBy(o => o.Id).ToList();
        foreach (var o in ordered) o.SyncRoot.EnterWriteLock();
        try
        {
            foreach (var o in ordered)
            {
                o.IsBanned = banned;
                if (reason is not null)
                {
                    o.BanReason = reason;
                    if (reason.Length == 0) o.TryRemoveExtraJson("ban_reason");
                }
            }
        }
        finally
        {
            for (int i = ordered.Count - 1; i >= 0; i--)
                try { ordered[i].SyncRoot.ExitWriteLock(); } catch (Exception) { }
        }
    }

    // Single-peer privilege gate behind the direct target check and the
    // account-member loop. The verb word reproduces each verb's refusal
    // exactly ("ban"/"unban").
    internal static bool CheckPrivilege(GameObject go, GameObject candidate, string verb)
    {
        if (candidate.PrivilegeLevel >= go.PrivilegeLevel)
        {
            go.Msg($"You cannot {verb} someone of equal or higher privilege.");
            return false;
        }
        return true;
    }

    // Shared six-step preamble behind the ban/unban verbs: target resolution,
    // peer privilege gate, account expansion (+ its member gate), host lookup.
    // A blank target name means "show help": false with no message (the caller
    // prints its own help, which this helper cannot reach). Reason handling
    // stays at the call sites — the ban verb's reason is optional text, not a
    // gate, so no wantReason switch is needed. Returns false after messaging
    // on any refusal; the out accountScope is the effective scope (a missing
    // account flips it to character-only, with the verb's gerund in the note).
    internal static bool TryResolveBanPreamble(
        GameObject go,
        string? targetName,
        bool wantAccount,
        bool wantIp,
        string verb,
        string gerund,
        out GameObject? target,
        out GameObject? acct,
        out List<GameObject> acctChars,
        out string? host,
        out bool accountScope)
    {
        target = null;
        acct = null;
        acctChars = [];
        host = null;
        accountScope = wantAccount;
        if (string.IsNullOrWhiteSpace(targetName)) return false;
        var resolved = ResolveTarget(go, targetName);
        if (resolved is null) return false;
        // No self-exempt idiom on purpose: banning yourself locks your own
        // account (worse than deleting a disposable object), so self-ban
        // stays refused by the equal-or-higher rule below like any peer's.
        if (!CheckPrivilege(go, resolved, verb)) return false;
        target = resolved;
        if (accountScope)
        {
            acct = FindAccount(resolved);
            if (acct is null)
            {
                go.Msg($"Could not find the account owning {resolved.Name}; {gerund} character only.");
                accountScope = false;
            }
            else
            {
                acctChars = GetAccountChars(acct);
                foreach (var ch in acctChars)
                    if (!CheckPrivilege(go, ch, verb)) return false;
            }
        }
        if (wantIp) host = GetHost(resolved);
        return true;
    }
}
