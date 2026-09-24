namespace Atheriz.Core.Objects;

// Lock table extracted from GameObject: owns the name -> predicate-list
// map plus the snapshot projections. No locking of its own — the owning
// GameObject holds its read/write lock around every call.
internal sealed class LockTable
{
    private readonly Dictionary<string, List<LockEntry>> _entries = [];

    public void AddRestored(string lockName, LockEntry entry)
    {
        if (!_entries.TryGetValue(lockName, out var lst))
        {
            lst = [];
            _entries[lockName] = lst;
        }
        lst.Add(entry);
    }

    public bool Remove(string lockName) => _entries.Remove(lockName);

    public void Clear() => _entries.Clear();

    // Snapshot core for Access: absent and empty both mean "allow".
    public bool TrySnapshot(string lockName, out List<LockEntry> snapshot)
    {
        if (!_entries.TryGetValue(lockName, out var lst) || lst.Count == 0)
        {
            snapshot = [];
            return false;
        }
        snapshot = new List<LockEntry>(lst);
        return true;
    }

    public Dictionary<string, List<LockEntry>> SnapshotEntries()
        => _entries.ToDictionary(kv => kv.Key, kv => new List<LockEntry>(kv.Value));

    public Dictionary<string, List<Func<GameObject, bool>>> SnapshotPredicates()
        => _entries.ToDictionary(kv => kv.Key, kv => kv.Value.Select(e => e.Predicate).ToList());

    public Dictionary<string, List<string>> SnapshotPolicyNames()
        => _entries.ToDictionary(kv => kv.Key, kv => kv.Value.Select(e => LockPolicies.Name(e.Policy)).ToList());
}
