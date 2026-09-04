// Port of atheriz/commands/loggedin/ban.py:13-63 helpers (_resolve_target/_find_account/_target_ip)
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

internal static class BanHelper
{
    internal static GameObject? ResolveTarget(GameObject caller, string name)
    {
        if (name.StartsWith("#"))
        {
            if (!int.TryParse(name[1..], out var id))
            {
                caller.Msg("Invalid ID format. Use #<number>.");
                return null;
            }
            var objs = ObjectRegistry.Get(id);
            if (objs.Count == 0)
            {
                caller.Msg($"No object found with ID {id}.");
                return null;
            }
            var t = objs[0];
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
            caller.Msg($"Multiple matches for '{name}':");
            foreach (var m in matches) caller.Msg($"  #{m.Id} {m.Name}");
            return null;
        }
        return matches[0];
    }

    internal static GameObject? FindAccount(GameObject target)
    {
        var sess = target.Session;
        var acct = sess?.Account as GameObject;
        if (acct != null) return acct;
        var accounts = ObjectRegistry.FilterBy(x => x.IsAccount && (x as Account)?.Characters.Contains(target.Id) == true);
        return accounts.FirstOrDefault();
    }

    internal static string? GetHost(GameObject target)
    {
        var sess = target.Session;
        var conn = sess?.Connection;
        if (conn == null) return null;
        var host = conn.ClientHost;
        if (string.IsNullOrEmpty(host) || host == "?") return null;
        return host;
    }
}
