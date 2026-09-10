// Port of atheriz/commands/loggedin/ban.py:13-63 helpers (_resolve_target/_find_account/_target_ip)

namespace Atheriz.Core.Commands.LoggedIn;

internal static class BanHelper
{
    internal static GameObject? ResolveTarget(GameObject caller, string name)
    {
        if (name.StartsWith("#", StringComparison.Ordinal))
        {
            if (!CommandHelpers.TryParseIdRef(name, out var id))
            {
                caller.Msg("Invalid ID format. Use #<number>.");
                return null;
            }
            var t = ObjectRegistry.GetSingle(id);
            if (t is null)
            {
                caller.Msg($"No object found with ID {id}.");
                return null;
            }
            if (!t.IsPc)
            {
                caller.Msg("You can only ban player characters.");
                return null;
            }
            return t;
        }
        var matches = ObjectRegistry.FilterBy(x => x.IsPc && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (matches.Count == 0)
        {
            caller.Msg($"No player character found named '{name}'.");
            return null;
        }
        if (matches.Count > 1)
        {
            CommandHelpers.MsgMultipleMatchesColon(caller, name);
            foreach (var m in matches) caller.Msg($"  #{m.Id} {m.Name}");
            return null;
        }
        return matches[0];
    }

    internal static GameObject? FindAccount(GameObject target)
    {
        var sess = target.Session;
        var acct = sess?.Account as GameObject;
        if (acct is not null) return acct;
        var accounts = ObjectRegistry.FilterBy(x => x.IsAccount && (x as Account)?.Characters.Contains(target.Id) == true);
        return accounts.FirstOrDefault();
    }

    internal static string? GetHost(GameObject target)
    {
        var sess = target.Session;
        var conn = sess?.Connection;
        if (conn is null) return null;
        var host = conn.ClientHost;
        if (string.IsNullOrEmpty(host) || host == "?") return null;
        return host;
    }

    // Single home for the account-characters fetch repeated across the
    // ban/unban verbs (one verb fetched the same id list twice). Read-only
    // snapshot in, fresh list out; a non-account holder yields the same
    // empty list the old inline `as Account` guards produced.
    internal static List<GameObject> GetAccountChars(GameObject acct)
        => acct is Account acc ? ObjectRegistry.Get(acc.Characters.ToList()) : [];

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
