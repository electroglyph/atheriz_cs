namespace Atheriz.Core.Objects;

/// <summary>
/// Declarative lock-policy names persisted in save data (F004).
/// persisted as typed <see cref="LockPolicy"/> values and rebuilt on load via <see cref="TryResolve"/>,
/// so loading save data never executes code derived from the save file itself.
/// The <c>Custom</c> policy marks predicates that cannot survive a round-trip
/// (ad-hoc lambdas); they are kept in memory but dropped on save with a loud log.
/// The <c>Denied</c> policy is the fail-closed marker: unresolvable policies
/// persist as <c>Denied</c> (still denying) instead of vanishing into allow.
/// </summary>
public static class LockPolicies
{
    public const string Builder = "builder";
    public const string PcView = "pc-view";
    public const string NotSelf = "not-self";
    public const string PuppetOwner = "puppet-owner";
    public const string Custom = "custom";
    public const string Denied = "denied";

    // Typed policy names for authoring sites: AddLock and TryResolve take the
    // enum and map through Name, so a misspelled policy is a compile error.
    // Persistence stores the enum values; the string overloads stay for
    // authoring call sites and external game code.
    public enum LockPolicy
    {
        Builder,
        PcView,
        NotSelf,
        PuppetOwner,
        Custom,
        Denied,
    }

    public static string Name(LockPolicy policy) => policy switch
    {
        LockPolicy.Builder => Builder,
        LockPolicy.PcView => PcView,
        LockPolicy.NotSelf => NotSelf,
        LockPolicy.PuppetOwner => PuppetOwner,
        LockPolicy.Denied => Denied,
        _ => Custom,
    };

    public static bool TryParseName(string? raw, out LockPolicy policy)
    {
        var t = raw?.Trim();
        if (string.Equals(t, Builder, StringComparison.OrdinalIgnoreCase)) { policy = LockPolicy.Builder; return true; }
        if (string.Equals(t, PcView, StringComparison.OrdinalIgnoreCase)) { policy = LockPolicy.PcView; return true; }
        if (string.Equals(t, NotSelf, StringComparison.OrdinalIgnoreCase)) { policy = LockPolicy.NotSelf; return true; }
        if (string.Equals(t, PuppetOwner, StringComparison.OrdinalIgnoreCase)) { policy = LockPolicy.PuppetOwner; return true; }
        if (string.Equals(t, Custom, StringComparison.OrdinalIgnoreCase)) { policy = LockPolicy.Custom; return true; }
        if (string.Equals(t, Denied, StringComparison.OrdinalIgnoreCase)) { policy = LockPolicy.Denied; return true; }
        policy = LockPolicy.Custom;
        return false;
    }

    // String authoring labels classify to the typed policy for storage. Known
    // names (including "custom") keep their meaning; unrecognized labels become
    // Denied so a later save/load still denies instead of resurrecting nothing.
    internal static LockPolicy Classify(string? raw)
        => TryParseName(raw, out var policy) ? policy : LockPolicy.Denied;

    // Shared target-independent leaf for the Builder arms of both overloads:
    // both read only lock-guarded accessing.IsBuilder. Do NOT fold the 1-arg
    // overload into the 2-arg one with a dummy target — the 2-arg overload
    // binds PcView/NotSelf/PuppetOwner predicates to the dummy.
    private static bool IsBuilder(GameObject accessing) => accessing.IsBuilder;

    /// <summary>
    /// Target-independent subset (for holders like <c>Door</c> that are not
    /// <c>GameObject</c>s): only policies that don't bind the target resolve.
    /// </summary>
    public static bool TryResolve(string policy, out Func<GameObject, bool> predicate)
    {
        if (policy == Builder)
        {
            predicate = IsBuilder;
            return true;
        }
        if (policy == Denied)
        {
            predicate = _ => false;
            return true;
        }
        predicate = _ => false;
        return false;
    }

    public static bool TryResolve(LockPolicy policy, out Func<GameObject, bool> predicate)
        => TryResolve(Name(policy), out predicate);
    /// <summary>
    /// Resolves a persisted policy name to a predicate bound to <paramref name="target"/>.
    /// Returns false for unknown policies (caller must log loudly and skip).
    /// </summary>
    public static bool TryResolve(string policy, GameObject target, out Func<GameObject, bool> predicate)
    {
        switch (policy)
        {
            case Builder:
                predicate = IsBuilder;
                return true;
            case PcView:
// tests only the *target's* connection.
                // Builders and above keep sight of offline PCs (room lists,
                // search, examine); regular players fail view and never see them.
                predicate = accessing => !target.IsPc || target.IsConnected || accessing.IsBuilder;
                return true;
            case NotSelf:
                predicate = accessing => accessing.Id != target.Id;
                return true;
            case PuppetOwner:
// F001)
                predicate = accessing =>
                {
                    if (target.IsNpc) return true;
                    if (accessing.IsSuperUser) return true;
                    var sess = accessing.Session;
                    return sess?.Account is Account acc && acc.Characters.Contains(target.Id);
                };
                return true;
            case Denied:
                predicate = _ => false;
                return true;
            default:
                predicate = _ => false;
                return false;
        }
    }

    public static bool TryResolve(LockPolicy policy, GameObject target, out Func<GameObject, bool> predicate)
        => TryResolve(Name(policy), target, out predicate);
}
