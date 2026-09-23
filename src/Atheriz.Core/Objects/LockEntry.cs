namespace Atheriz.Core.Objects;

// One lock-table row: the declarative policy (what persists) plus the live
// predicate (what decides). A single collection of these replaces the old
// parallel predicate/policy lists, so the two can never drift out of sync.
internal sealed record LockEntry(LockPolicies.LockPolicy Policy, Func<GameObject, bool> Predicate);
