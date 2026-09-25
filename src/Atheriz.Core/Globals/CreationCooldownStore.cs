namespace Atheriz.Core.Globals;

// Creation-cooldown store split out of ObjectRegistry: rate-limit state
// lives here, world state stays in the registry. Own lock via
// BoundedDictionary (atomic check+reserve needs no outer lock).
public static class CreationCooldownStore
{
    // Owner token per reservation: a blind host-keyed Clear lets one
    // drain's validation failure wipe another drain's live reservation
    // (throttle bypass). Clears only honor the reserving token.
    private sealed record CooldownEntry(double Expires, Guid Owner);
    private static readonly BoundedDictionary<string, CooldownEntry> CreationCooldowns = new();

    public static bool CreationCooldownActive(string host, double now)
    {
        if (host == "?") return false;
        if (!CreationCooldowns.TryGetValue(host, out var entry) || entry is null) return false;
        if (entry.Expires > now) return true;
        CreationCooldowns.RemoveIfEqual(host, entry);
        return false;
    }
    public static void ApplyCreationCooldown(string op, string host, double now, double cooldown)
    {
        if (host == "?" || cooldown <= 0) return;
        CreationCooldowns.Set(host, new CooldownEntry(now + cooldown, Guid.NewGuid()));
    }
    // Returns the owning token on success, null when throttled.
    public static Guid? TryReserveCreationCooldown(string op, string host, double now, double cooldown)
    {
        if (host == "?") return Guid.NewGuid();
        var owner = Guid.NewGuid();
        if (cooldown <= 0) return CreationCooldownActive(host, now) ? null : owner;
        // Atomic check+reserve on the dict's own lock — no snapshot, no outer lock.
        bool ok = CreationCooldowns.CheckAndSet(host, new CooldownEntry(now + cooldown, owner), (exists, exp) => !exists || exp.Expires <= now);
        return ok ? owner : null;
    }
    public static void ClearCreationCooldown(string host, Guid owner)
    {
        if (host == "?") return;
        if (CreationCooldowns.TryGetValue(host, out var entry) && entry is not null && entry.Owner == owner)
            CreationCooldowns.RemoveIfEqual(host, entry);
    }

    internal static void Clear() => CreationCooldowns.Clear();
}
