namespace Atheriz.Core.Globals;

// Creation-cooldown store split out of ObjectRegistry: rate-limit state
// lives here, world state stays in the registry. Own lock via
// BoundedDictionary (atomic check+reserve needs no outer lock).
public static class CreationCooldownStore
{
    // Owner token per reservation: a blind host-keyed Clear lets one
    // drain's validation failure wipe another drain's live reservation
    // (throttle bypass). Clears only honor the reserving token.
    // Keyed per (op, host): an account creation must not consume the
    // character-creation budget on the same host (create -> new is the
    // normal onboarding flow), and vice versa. Each verb is throttled
    // independently at the same rate. The host stays verbatim (no
    // normalization); only the op is lowercased.
    private sealed record CooldownEntry(double Expires, Guid Owner);
    private static readonly BoundedDictionary<string, CooldownEntry> CreationCooldowns = new();

    private static string Key(string op, string host) => string.Concat(op.ToLowerInvariant(), "\0", host);

    public static bool CreationCooldownActive(string op, string host, double now)
    {
        if (host == "?") return false;
        var key = Key(op, host);
        if (!CreationCooldowns.TryGetValue(key, out var entry) || entry is null) return false;
        if (entry.Expires > now) return true;
        CreationCooldowns.RemoveIfEqual(key, entry);
        return false;
    }
    public static void ApplyCreationCooldown(string op, string host, double now, double cooldown, Guid owner)
    {
        if (host == "?" || cooldown <= 0) return;
        CreationCooldowns.Set(Key(op, host), new CooldownEntry(now + cooldown, owner));
    }
    // Returns the owning token on success, null when throttled.
    public static Guid? TryReserveCreationCooldown(string op, string host, double now, double cooldown)
    {
        if (host == "?") return Guid.NewGuid();
        var owner = Guid.NewGuid();
        if (cooldown <= 0) return CreationCooldownActive(op, host, now) ? null : owner;
        // Atomic check+reserve on the dict's own lock — no snapshot, no outer lock.
        bool ok = CreationCooldowns.CheckAndSet(Key(op, host), new CooldownEntry(now + cooldown, owner), (exists, exp) => !exists || exp.Expires <= now);
        return ok ? owner : null;
    }
    public static void ClearCreationCooldown(string op, string host, Guid owner)
    {
        if (host == "?") return;
        var key = Key(op, host);
        if (CreationCooldowns.TryGetValue(key, out var entry) && entry is not null && entry.Owner == owner)
            CreationCooldowns.RemoveIfEqual(key, entry);
    }

    internal static void Clear() => CreationCooldowns.Clear();
}
