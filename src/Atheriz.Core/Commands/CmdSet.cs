namespace Atheriz.Core.Commands;

/// <summary>
/// Thread-safe command set. Mirrors <c>atheriz/commands/base_cmdset.py:CmdSet</c> (139 LOC).
/// </summary>
public class CmdSet
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, Command> _commands = new(StringComparer.Ordinal);

    public IReadOnlyList<Command> GetAll()
    {
        _lock.EnterReadLock();
        try { return _commands.Values.ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    // For distinct usage (e.g., Help listing) expose separate helper if needed
    public IReadOnlyList<Command> GetAllDistinct()
    {
        _lock.EnterReadLock();
        try { return _commands.Values.Distinct().ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    public void Add(Command command, string? tag = null) => Adds([command], tag);

    public virtual void Adds(IEnumerable<Command> commands, string? tag = null)
    {
        var list = commands.ToList();
        if (tag is not null) foreach (var c in list) c.Tag = tag;
        _lock.EnterWriteLock();
        try
        {
            var claimed = new Dictionary<string, Command>(StringComparer.Ordinal);
            foreach (var cmd in list)
            {
                foreach (var name in new[] { cmd.Key }.Concat(cmd.Aliases ?? []))
                {
                    Command? existing = null;
                    if (!claimed.TryGetValue(name, out existing))
                        _commands.TryGetValue(name, out existing);
                    if (existing is not null && !ReferenceEquals(existing, cmd))
                        throw new InvalidOperationException(
                            $"Command key/alias '{name}' already registered to '{existing.Key}'; refusing to overwrite with '{cmd.Key}'.");
                    claimed[name] = cmd;
                }
            }
            foreach (var cmd in list) Register(cmd);
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void Remove(Command command)
    {
        _lock.EnterWriteLock();
        try
        {
            _commands.Remove(command.Key);
            if (command.Aliases is not null)
                foreach (var a in command.Aliases) _commands.Remove(a);
        }
        finally { _lock.ExitWriteLock(); }
    }

    public virtual void RemoveByTag(string tag)
    {
        List<string> toDel = [];
        _lock.EnterReadLock();
        try
        {
            foreach (var kv in _commands) if (kv.Value.Tag == tag) toDel.Add(kv.Key);
        }
        finally { _lock.ExitReadLock(); }
        if (toDel.Count == 0) return;
        _lock.EnterWriteLock();
        try { foreach (var k in toDel) _commands.Remove(k); }
        finally { _lock.ExitWriteLock(); }
    }

    public virtual Command? Get(string name)
    {
        _lock.EnterReadLock();
        try { _commands.TryGetValue(name, out var c); return c; }
        finally { _lock.ExitReadLock(); }
    }

    private void Register(Command cmd)
    {
        _commands[cmd.Key] = cmd;
        foreach (var a in cmd.Aliases ?? []) _commands[a] = cmd;
    }

    public virtual IReadOnlyList<string> GetKeys()
    {
        _lock.EnterReadLock();
        try { return _commands.Keys.ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    public int Count
    {
        get { _lock.EnterReadLock(); try { return _commands.Count; } finally { _lock.ExitReadLock(); } }
    }
}
