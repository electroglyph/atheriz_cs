namespace Atheriz.Core.Globals;

// Creation-cooldown store split out of ObjectRegistry: rate-limit state
// lives here, world state stays in the registry. Own lock via
// BoundedDictionary (atomic check+reserve needs no outer lock).
public static class CreationCooldownStore
{
    private static readonly BoundedDictionary<string, double> CreationCooldowns = new();

    public static bool CreationCooldownActive(string host, double now)
    {
        if (host == "?") return false;
        if (!CreationCooldowns.TryGetValue(host, out var exp)) return false;
        if (exp > now) return true;
        CreationCooldowns.RemoveIfEqual(host, exp);
        return false;
    }
    public static void ApplyCreationCooldown(string op, string host, double now, double cooldown)
    {
        if (host == "?" || cooldown <= 0) return;
        CreationCooldowns.Set(host, now + cooldown);
    }
    public static bool TryReserveCreationCooldown(string op, string host, double now, double cooldown)
    {
        if (host == "?") return true;
        if (cooldown <= 0) return !CreationCooldownActive(host, now);
        // Atomic check+reserve on the dict's own lock — no snapshot, no outer lock.
        return CreationCooldowns.CheckAndSet(host, now + cooldown, (exists, exp) => !exists || exp <= now);
    }
    public static void ClearCreationCooldown(string host)
    {
        if (host == "?") return;
        CreationCooldowns.Remove(host);
    }

    internal static void Clear() => CreationCooldowns.Clear();
}
