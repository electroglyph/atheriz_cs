namespace Atheriz.Core.Objects;

// Hook registry extracted from GameObject: owns the funcName -> ordered
// delegate-list map plus the dispatch snapshot. No locking of its own — the
// owning GameObject holds its read/write lock around every call.
internal sealed class HookRegistry
{
    // Insertion-ordered per hook: dispatch runs hooks in install order.
    // Same-delegate installs dedupe (HashSet.Add semantics via Contains —
    // Delegate equality is target+method, and hook lists stay tiny).
    private readonly Dictionary<string, List<Delegate>> _entries = [];

    public void Add(string funcName, Delegate hook)
    {
        if (!_entries.TryGetValue(funcName, out var list))
        {
            list = [];
            _entries[funcName] = list;
        }
        if (!list.Contains(hook)) list.Add(hook);
    }

    public bool Has(string funcName) => _entries.TryGetValue(funcName, out var s) && s.Count > 0;

    // Dispatch snapshot: null when absent or empty (caller runs the original).
    public List<Delegate>? TrySnapshot(string funcName)
        => _entries.TryGetValue(funcName, out var list) && list.Count > 0
            ? new List<Delegate>(list)
            : null;

    public void Clear() => _entries.Clear();

    internal Dictionary<string, List<Delegate>> Raw => _entries;
}
