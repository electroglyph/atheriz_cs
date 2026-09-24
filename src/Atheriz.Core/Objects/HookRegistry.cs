namespace Atheriz.Core.Objects;

// Hook registry extracted from GameObject: owns the funcName -> delegate-set
// map plus the dispatch snapshot. No locking of its own — the owning
// GameObject holds its read/write lock around every call.
internal sealed class HookRegistry
{
    private readonly Dictionary<string, HashSet<Delegate>> _entries = [];

    public void Add(string funcName, Delegate hook)
    {
        if (!_entries.TryGetValue(funcName, out var set))
        {
            set = [];
            _entries[funcName] = set;
        }
        set.Add(hook);
    }

    public bool Has(string funcName) => _entries.TryGetValue(funcName, out var s) && s.Count > 0;

    // Dispatch snapshot: null when absent or empty (caller runs the original).
    public HashSet<Delegate>? TrySnapshot(string funcName)
        => _entries.TryGetValue(funcName, out var hs) && hs.Count > 0
            ? new HashSet<Delegate>(hs)
            : null;

    public void Clear() => _entries.Clear();

    internal Dictionary<string, HashSet<Delegate>> Raw => _entries;
}
