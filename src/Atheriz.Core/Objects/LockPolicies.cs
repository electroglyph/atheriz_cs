namespace Atheriz.Core.Objects;

/// <summary>
/// Declarative lock-policy names persisted in save data (F004).
/// Port of the Python lock-string convention (<c>obj.locks.add("view: ...")</c>): a lock is
/// persisted as <c>name: policy|policy</c> and rebuilt on load via <see cref="TryResolve"/>,
/// so loading save data never executes code derived from the save file itself.
/// The <c>"custom"</c> policy marks predicates that cannot survive a round-trip
/// (ad-hoc lambdas); they are kept in memory but dropped on save with a loud log.
/// </summary>
public static class LockPolicies
{
    public const string Builder = "builder";
    public const string PcView = "pc-view";
    public const string NotSelf = "not-self";
    public const string PuppetOwner = "puppet-owner";
    public const string Custom = "custom";

    /// <summary>
    /// Resolves a persisted policy name to a predicate bound to <paramref name="target"/>.
    /// Returns false for unknown policies (caller must log loudly and skip).
    /// </summary>
    public static bool TryResolve(string policy, GameObject target, out Func<GameObject, bool> predicate)
    {
        switch (policy)
        {
            case Builder:
                predicate = accessing => accessing.IsBuilder;
                return true;
            case PcView:
                predicate = accessing => !target.IsPc || accessing.IsConnected || target.IsConnected;
                return true;
            case NotSelf:
                predicate = accessing => accessing.Id != target.Id;
                return true;
            case PuppetOwner:
                // Port of base_obj.py:185-193 puppet lock (typed; the old `as dynamic` fallback is gone — F001)
                predicate = accessing =>
                {
                    if (target.IsNpc) return true;
                    if (accessing.IsSuperUser) return true;
                    var sess = accessing.Session;
                    return sess?.Account is Account acc && acc.Characters.Contains(target.Id);
                };
                return true;
            default:
                predicate = _ => false;
                return false;
        }
    }
}
