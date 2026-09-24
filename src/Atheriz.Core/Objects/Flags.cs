namespace Atheriz.Core.Objects;

/// <summary>
/// OOP extraction of <c>atheriz/objects/base_flags.py:3 FLAG_DEFAULTS</c>.
/// Holds the 16 boolean flags that were previously 16 duplicated private fields
/// in <see cref="GameObject"/> (lines 46-62). Single source of truth, no locks —
/// locking stays on the owning <see cref="GameObject"/>.
/// Mirrors defaults: is_modified=True, everything else False, tags are held on GameObject.
/// </summary>
public sealed class Flags
{
    public bool IsPc { get; set; }
    public bool IsNpc { get; set; }
    public bool IsItem { get; set; }
    public bool IsMapable { get; set; }
    public bool IsContainer { get; set; }
    public bool IsScript { get; set; }
    public bool IsTickable { get; set; }
    public bool IsAccount { get; set; }
    public bool IsChannel { get; set; }
    public bool IsNode { get; set; }
    public bool IsModified { get; set; } = true; // FLAG_DEFAULTS["is_modified"] = True
    public bool IsDeleted { get; set; }
    public bool IsConnected { get; set; }
    public bool IsTemporary { get; set; }
    public bool IsBanned { get; set; }
    public bool CanHear { get; set; }

    public Flags Clone() => (Flags)MemberwiseClone();
}
