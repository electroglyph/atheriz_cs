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
    private bool _isPc;
    private bool _isNpc;
    private bool _isItem;
    private bool _isMapable;
    private bool _isContainer;
    private bool _isScript;
    private bool _isTickable;
    private bool _isAccount;
    private bool _isChannel;
    private bool _isNode;
    private bool _isModified = true; // FLAG_DEFAULTS["is_modified"] = True
    private bool _isDeleted;
    private bool _isConnected;
    private bool _isTemporary;
    private bool _isBanned;
    private bool _canHear;

    public bool IsPc { get => _isPc; set => _isPc = value; }
    public bool IsNpc { get => _isNpc; set => _isNpc = value; }
    public bool IsItem { get => _isItem; set => _isItem = value; }
    public bool IsMapable { get => _isMapable; set => _isMapable = value; }
    public bool IsContainer { get => _isContainer; set => _isContainer = value; }
    public bool IsScript { get => _isScript; set => _isScript = value; }
    public bool IsTickable { get => _isTickable; set => _isTickable = value; }
    public bool IsAccount { get => _isAccount; set => _isAccount = value; }
    public bool IsChannel { get => _isChannel; set => _isChannel = value; }
    public bool IsNode { get => _isNode; set => _isNode = value; }
    public bool IsModified { get => _isModified; set => _isModified = value; }
    public bool IsDeleted { get => _isDeleted; set => _isDeleted = value; }
    public bool IsConnected { get => _isConnected; set => _isConnected = value; }
    public bool IsTemporary { get => _isTemporary; set => _isTemporary = value; }
    public bool IsBanned { get => _isBanned; set => _isBanned = value; }
    public bool CanHear { get => _canHear; set => _canHear = value; }

    /// <summary>
    /// Tries to set a flag by name (Python <c>__setattr__</c> / FLAG_DEFAULTS key).
    /// Returns true if value changed. Mirrors dynamic flag loop in <c>base_flags.Flags.__init__</c>.
    /// </summary>
    public bool TrySet(string name, bool value)
    {
        switch (name)
        {
            case nameof(IsPc): case "is_pc": case "_isPc": if (_isPc == value) return false; _isPc = value; return true;
            case nameof(IsNpc): case "is_npc": if (_isNpc == value) return false; _isNpc = value; return true;
            case nameof(IsItem): case "is_item": if (_isItem == value) return false; _isItem = value; return true;
            case nameof(IsMapable): case "is_mapable": if (_isMapable == value) return false; _isMapable = value; return true;
            case nameof(IsContainer): case "is_container": if (_isContainer == value) return false; _isContainer = value; return true;
            case nameof(IsScript): case "is_script": if (_isScript == value) return false; _isScript = value; return true;
            case nameof(IsTickable): case "is_tickable": case "_is_tickable": if (_isTickable == value) return false; _isTickable = value; return true;
            case nameof(IsAccount): case "is_account": if (_isAccount == value) return false; _isAccount = value; return true;
            case nameof(IsChannel): case "is_channel": if (_isChannel == value) return false; _isChannel = value; return true;
            case nameof(IsNode): case "is_node": if (_isNode == value) return false; _isNode = value; return true;
            case nameof(IsModified): case "is_modified": if (_isModified == value) return false; _isModified = value; return true;
            case nameof(IsDeleted): case "is_deleted": if (_isDeleted == value) return false; _isDeleted = value; return true;
            case nameof(IsConnected): case "is_connected": if (_isConnected == value) return false; _isConnected = value; return true;
            case nameof(IsTemporary): case "is_temporary": if (_isTemporary == value) return false; _isTemporary = value; return true;
            case nameof(IsBanned): case "is_banned": if (_isBanned == value) return false; _isBanned = value; return true;
            case nameof(CanHear): case "can_hear": if (_canHear == value) return false; _canHear = value; return true;
            default: return false;
        }
    }

    public Flags Clone() => (Flags)MemberwiseClone();
}
