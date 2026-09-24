namespace Atheriz.Core.Commands;

/// <summary>
/// Thread-safe command set.
/// </summary>
public class CmdSet
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);
    // Ordinal-sorted key snapshot for the dispatch hot path (AutoAlias). The
    // cached list is replaced, never mutated, so a reader holding a previous
    // snapshot enumerates consistently. Cleared under the write lock on every
    // mutation path below, so the cache read and the mutation share the lock.
    private List<string>? _sortedKeysCache;

    public IReadOnlyList<Command> GetAll()
    {
        lock (_lock)
        {
            // one entry per command instance. The dict holds Key +
            // every alias; without Distinct every caller must remember to
            // dedupe (two HelpCommands do; the locals.AddRange path didn't).
            return _commands.Values.Distinct().ToList();
        }
    }

    public void Add(Command command, string? tag = null) => Adds([command], tag);

    public virtual void Adds(IEnumerable<Command> commands, string? tag = null)
    {
        var list = commands.ToList();
        if (tag is not null) foreach (var c in list) c.Tag = tag;
        lock (_lock)
        {
            var claimed = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
            foreach (var cmd in list)
            {
                // Key first, then aliases — no LINQ/concat allocs on this path.
                ClaimName(cmd.Key, cmd, claimed);
                var aliases = cmd.Aliases;
                if (aliases is not null)
                    foreach (var name in aliases)
                        ClaimName(name, cmd, claimed);
            }
            foreach (var cmd in list) Register(cmd);
            _sortedKeysCache = null;
        }
    }

    // Single-name claim check shared by the Key + each alias (caller holds
    // the write lock): batch-local claims shadow the table so a within-batch
    // collision throws before anything registers (atomic Adds).
    private void ClaimName(string name, Command cmd, Dictionary<string, Command> claimed)
    {
        if (!claimed.TryGetValue(name, out var existing))
            _commands.TryGetValue(name, out existing);
        if (existing is not null && !ReferenceEquals(existing, cmd))
            throw new InvalidOperationException(
                $"Command key/alias '{name}' already registered to '{existing.Key}'; refusing to overwrite with '{cmd.Key}'.");
        claimed[name] = cmd;
    }

    public void Remove(Command command)
    {
        lock (_lock)
        {
            // remove by instance identity, not by current Key/Aliases.
            // Channel/exit commands are SetKey()-rekeyed after registration
            // (no Python set_key exists); key-based removal leaked the
            // originally-registered entries.
            List<string>? doomed = null;
            foreach (var kv in _commands)
                if (ReferenceEquals(kv.Value, command))
                    (doomed ??= []).Add(kv.Key);
            if (doomed is null) return;
            foreach (var k in doomed) _commands.Remove(k);
            _sortedKeysCache = null;
        }
    }

    public virtual void RemoveByTag(string tag)
    {
        lock (_lock)
        {
            // Single hold: snapshot and delete atomically, so a key added
            // with this tag between a collect pass and a delete pass cannot
            // survive this eviction.
            bool removed = false;
            foreach (var kv in _commands.ToList())
                if (kv.Value.Tag == tag)
                {
                    _commands.Remove(kv.Key);
                    removed = true;
                }
            if (removed) _sortedKeysCache = null;
        }
    }

    public virtual Command? Get(string name)
    {
        lock (_lock)
        {
            _commands.TryGetValue(name, out var c);
            return c;
        }
    }

    private void Register(Command cmd)
    {
        _commands[cmd.Key] = cmd;
        foreach (var a in cmd.Aliases ?? []) _commands[a] = cmd;
    }

    public virtual IReadOnlyList<string> GetKeys()
    {
        lock (_lock)
        {
            return _commands.Keys.ToList();
        }
    }

    // Cached ordinal-sorted key snapshot for the dispatch hot path
    // (AutoAlias): same order as GetKeys().OrderBy(Ordinal), without the
    // per-dispatch sort. The list is replaced, never mutated, so readers
    // holding an older snapshot stay consistent; every mutation path above
    // clears it under the write lock.
    public virtual IReadOnlyList<string> GetSortedKeys()
    {
        lock (_lock)
        {
            if (_sortedKeysCache is not null) return _sortedKeysCache;
        }
        lock (_lock)
        {
            if (_sortedKeysCache is null)
            {
                var keys = _commands.Keys.ToList();
                keys.Sort(StringComparer.Ordinal);
                _sortedKeysCache = keys;
            }
            return _sortedKeysCache;
        }
    }

    public int Count
    {
        get { lock (_lock) return _commands.Count; }
    }
}
