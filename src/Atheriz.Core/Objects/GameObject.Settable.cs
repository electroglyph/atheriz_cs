namespace Atheriz.Core.Objects;

// Switch-dispatched property sets for GameObject: one arm per accepted
// spelling (exact CLR name, lower-snake form, "_" + snake), mirroring the
// old triple-spelling map case-sensitively. Each arm converts with the
// type-specific Convert method the old generic conversion call
// delegated to, so accepted inputs and failure modes (including exception
// messages) are unchanged. A null value flows into the same cast the old
// code used, so null keeps its old outcome per property (assigned for
// reference/nullable types, NullReferenceException for value types,
// ArgumentNullException where the setter guards).
public partial class GameObject
{
    /// <summary>
    /// Whether <paramref name="name"/> names a known property (settable or
    /// read-only). Resolution is case-sensitive: mixed-case spellings fall
    /// through to extras like before.
    /// </summary>
    public virtual bool IsKnownProperty(string name) => name switch
    {
        "Desc" or "desc" or "_desc" => true,
        "Symbol" or "symbol" or "_symbol" => true,
        "MoveVerb" or "move_verb" or "_move_verb" => true,
        "Gender" or "gender" or "_gender" => true,
        "Name" or "name" or "_name" => true,
        "Quelled" or "quelled" or "_quelled" => true,
        "MapEnabled" or "map_enabled" or "_map_enabled" => true,
        "NoFollow" or "no_follow" or "_no_follow" => true,
        "TickSeconds" or "tick_seconds" or "_tick_seconds" => true,
        "LastMapTime" or "last_map_time" or "_last_map_time" => true,
        "Following" or "following" or "_following" => true,
        "PrivilegeLevel" or "privilege_level" or "_privilege_level" => true,
        "Session" or "session" or "_session" => true,
        "ExternalCmdSet" or "external_cmdset" or "_external_cmdset" => true,
        "InternalCmdSet" or "internal_cmdset" or "_internal_cmdset" => true,
        "Location" or "location" or "_location" => true,
        "Home" or "home" or "_home" => true,
        "Id" or "id" or "_id" => true,
        "IsPc" or "is_pc" or "_is_pc" => true,
        "IsNpc" or "is_npc" or "_is_npc" => true,
        "IsItem" or "is_item" or "_is_item" => true,
        "IsMapable" or "is_mapable" or "_is_mapable" => true,
        "IsContainer" or "is_container" or "_is_container" => true,
        "IsScript" or "is_script" or "_is_script" => true,
        "IsTickable" or "is_tickable" or "_is_tickable" => true,
        "IsAccount" or "is_account" or "_is_account" => true,
        "IsChannel" or "is_channel" or "_is_channel" => true,
        "IsNode" or "is_node" or "_is_node" => true,
        "IsModified" or "is_modified" or "_is_modified" => true,
        "IsDeleted" or "is_deleted" or "_is_deleted" => true,
        "IsConnected" or "is_connected" or "_is_connected" => true,
        "IsTemporary" or "is_temporary" or "_is_temporary" => true,
        "IsBanned" or "is_banned" or "_is_banned" => true,
        "CanHear" or "can_hear" or "_can_hear" => true,
        "IsSuperUser" or "is_superuser" or "_is_superuser" => true,
        "IsBuilder" or "is_builder" or "_is_builder" => true,
        "SyncRoot" or "sync_root" or "_sync_root" => true,
        "TagsSnapshot" or "tags_snapshot" or "_tags_snapshot" => true,
        "ContentsSnapshot" or "contents_snapshot" or "_contents_snapshot" => true,
        "ScriptsSnapshot" or "scripts_snapshot" or "_scripts_snapshot" => true,
        "ChannelsSnapshot" or "channels_snapshot" or "_channels_snapshot" => true,
        "FollowersSnapshot" or "followers_snapshot" or "_followers_snapshot" => true,
        "PuppetStack" or "puppet_stack" or "_puppet_stack" => true,
        _ => false,
    };

    /// <inheritdoc/>
    public virtual bool TrySetProperty(string name, object? value, out string? error)
    {
        error = null;
        switch (name)
        {
            case "Desc" or "desc" or "_desc":
                // Null passes into the non-nullable property like the old
                // (string) cast did; the compiler cannot see that flow.
                Desc = ToText(value)!;
                return true;
            case "Symbol" or "symbol" or "_symbol":
                Symbol = ToText(value)!;
                return true;
            case "MoveVerb" or "move_verb" or "_move_verb":
                MoveVerb = ToText(value)!;
                return true;
            case "Gender" or "gender" or "_gender":
                Gender = ToText(value)!;
                return true;
            case "Name" or "name" or "_name":
                Name = ToText(value)!;
                return true;
            case "Quelled" or "quelled" or "_quelled":
                Quelled = ToBool(value);
                return true;
            case "MapEnabled" or "map_enabled" or "_map_enabled":
                MapEnabled = ToBool(value);
                return true;
            case "NoFollow" or "no_follow" or "_no_follow":
                NoFollow = ToBool(value);
                return true;
            case "TickSeconds" or "tick_seconds" or "_tick_seconds":
                TickSeconds = ToDouble(value);
                return true;
            case "LastMapTime" or "last_map_time" or "_last_map_time":
                LastMapTime = value is null ? null : ToDouble(value);
                return true;
            case "Following" or "following" or "_following":
                Following = value is null ? null : ToInt(value);
                return true;
            case "PrivilegeLevel" or "privilege_level" or "_privilege_level":
                PrivilegeLevel = ToPrivilege(value);
                return true;
            case "Session" or "session" or "_session":
                Session = value is null ? null : value is Session s ? s : throw new InvalidCastException();
                return true;
            case "ExternalCmdSet" or "external_cmdset" or "_external_cmdset":
                ExternalCmdSet = value is null ? null : value is CmdSet e ? e : throw new InvalidCastException();
                return true;
            case "InternalCmdSet" or "internal_cmdset" or "_internal_cmdset":
                InternalCmdSet = value is null ? null : value is CmdSet i ? i : throw new InvalidCastException();
                return true;
            case "Location" or "location" or "_location"
                or "Home" or "home" or "_home"
                or "Id" or "id" or "_id"
                or "IsPc" or "is_pc" or "_is_pc"
                or "IsNpc" or "is_npc" or "_is_npc"
                or "IsItem" or "is_item" or "_is_item"
                or "IsMapable" or "is_mapable" or "_is_mapable"
                or "IsContainer" or "is_container" or "_is_container"
                or "IsScript" or "is_script" or "_is_script"
                or "IsTickable" or "is_tickable" or "_is_tickable"
                or "IsAccount" or "is_account" or "_is_account"
                or "IsChannel" or "is_channel" or "_is_channel"
                or "IsNode" or "is_node" or "_is_node"
                or "IsModified" or "is_modified" or "_is_modified"
                or "IsDeleted" or "is_deleted" or "_is_deleted"
                or "IsConnected" or "is_connected" or "_is_connected"
                or "IsTemporary" or "is_temporary" or "_is_temporary"
                or "IsBanned" or "is_banned" or "_is_banned"
                or "CanHear" or "can_hear" or "_can_hear"
                or "IsSuperUser" or "is_superuser" or "_is_superuser"
                or "IsBuilder" or "is_builder" or "_is_builder"
                or "SyncRoot" or "sync_root" or "_sync_root"
                or "TagsSnapshot" or "tags_snapshot" or "_tags_snapshot"
                or "ContentsSnapshot" or "contents_snapshot" or "_contents_snapshot"
                or "ScriptsSnapshot" or "scripts_snapshot" or "_scripts_snapshot"
                or "ChannelsSnapshot" or "channels_snapshot" or "_channels_snapshot"
                or "FollowersSnapshot" or "followers_snapshot" or "_followers_snapshot"
                or "PuppetStack" or "puppet_stack" or "_puppet_stack":
                error = $"'{name}' is a read-only attribute.";
                return false;
            default:
                return false;
        }
    }

    // Typed conversions below: each calls the type-specific Convert method
    // the old generic conversion delegated to for that target type,
    // so the accepted inputs and the thrown exceptions are the same.
    // Non-convertible values (lists, dicts) throw InvalidCastException like
    // the old "Object must implement IConvertible" path; only the type is
    // significant there (callers report a fixed message for it).

    /// <summary>
    /// Unboxes a null exactly like the old setter casts did (a value-type
    /// property set to null throws NullReferenceException). The
    /// null-forgiving operator hides the intentional null from the compiler;
    /// the throw itself is the runtime's, not a hand-built one.
    /// </summary>
    protected static T UnboxNull<T>() => (T)(object)null!;

    /// <summary>
    /// Converts <paramref name="value"/> to text like the old
    /// string-target conversion did (anything convertible through its own
    /// string form; null passes through).
    /// </summary>
    protected static string? ToText(object? value) => value switch
    {
        null => null,
        string s => s,
        IConvertible convertible => convertible.ToString(),
        _ => throw new InvalidCastException(),
    };

    /// <summary>
    /// Converts <paramref name="value"/> to bool like the old bool-target
    /// conversion did (bools, "True"/"False" text, non-zero numbers).
    /// </summary>
    protected static bool ToBool(object? value) => value switch
    {
        bool b => b,
        string s => Convert.ToBoolean(s),
        int i => Convert.ToBoolean(i),
        double d => Convert.ToBoolean(d),
        // Widened numerics, like the old Convert.ChangeType path accepted.
        long l => Convert.ToBoolean(l),
        float f => Convert.ToBoolean(f),
        decimal m => Convert.ToBoolean(m),
        short s2 => Convert.ToBoolean(s2),
        ushort us => Convert.ToBoolean(us),
        uint ui => Convert.ToBoolean(ui),
        ulong ul => Convert.ToBoolean(ul),
        byte b2 => Convert.ToBoolean(b2),
        sbyte sb => Convert.ToBoolean(sb),
        // Null unboxes exactly like the old (bool) cast did.
        null => UnboxNull<bool>(),
        _ => throw new InvalidCastException(),
    };

    /// <summary>
    /// Converts <paramref name="value"/> to int like the old int-target
    /// conversion did (ints, numeric text with rounding for doubles).
    /// </summary>
    protected static int ToInt(object? value) => value switch
    {
        int i => i,
        string s => Convert.ToInt32(s),
        double d => Convert.ToInt32(d),
        bool b => Convert.ToInt32(b),
        // Widened inputs, like the old Convert.ChangeType path accepted
        // (bools, floats, decimals, longs with OverflowException on overflow).
        long l => Convert.ToInt32(l),
        float f => Convert.ToInt32(f),
        decimal m => Convert.ToInt32(m),
        short s2 => Convert.ToInt32(s2),
        ushort us => Convert.ToInt32(us),
        uint ui => Convert.ToInt32(ui),
        ulong ul => Convert.ToInt32(ul),
        byte b2 => Convert.ToInt32(b2),
        sbyte sb => Convert.ToInt32(sb),
        char c => Convert.ToInt32(c),
        null => UnboxNull<int>(),
        _ => throw new InvalidCastException(),
    };

    /// <summary>
    /// Converts <paramref name="value"/> to double like the old
    /// double-target conversion did (doubles, ints, numeric text).
    /// </summary>
    protected static double ToDouble(object? value) => value switch
    {
        double d => d,
        int i => Convert.ToDouble(i),
        string s => Convert.ToDouble(s),
        // Widened inputs, like the old Convert.ChangeType path accepted.
        bool b => Convert.ToDouble(b),
        long l => Convert.ToDouble(l),
        float f => Convert.ToDouble(f),
        decimal m => Convert.ToDouble(m),
        short s2 => Convert.ToDouble(s2),
        ushort us => Convert.ToDouble(us),
        uint ui => Convert.ToDouble(ui),
        ulong ul => Convert.ToDouble(ul),
        byte b2 => Convert.ToDouble(b2),
        sbyte sb => Convert.ToDouble(sb),
        char c => Convert.ToDouble(c),
        null => UnboxNull<double>(),
        _ => throw new InvalidCastException(),
    };

    /// <summary>
    /// Converts <paramref name="value"/> to a privilege like the old
    /// enum-target conversion did (only an actual privilege value; text
    /// and numbers never converted and still throw).
    /// </summary>
    protected static Privilege ToPrivilege(object? value) => value switch
    {
        Privilege p => p,
        null => UnboxNull<Privilege>(),
        _ => throw new InvalidCastException(),
    };
}
