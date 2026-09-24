namespace Atheriz.Core.Commands.LoggedIn;

public static class SetHelper
{
    public static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    {
        "id","session","lock","locks","access","internal_cmdset","external_cmdset","scripts","hooks","channels","followers","following","is_pc","is_npc","is_item","is_container","is_mapable","is_account","is_channel","is_node","is_script","is_connected","is_deleted","is_modified","is_temporary","is_tickable","_is_tickable","password","logged_in","characters","privilege_level","quelled","is_banned","ban_reason","location","home","_contents","group_channel","contents","tags","name"
    };

    // Case-sensitive move/teleport gate behind the set/unset guards (matches
    // the old per-call arrays' Ordinal Contains exactly — not the
    // OrdinalIgnoreCase Protected set above).
    internal static readonly HashSet<string> MoveGate =
        new(StringComparer.Ordinal) { "location", "home", "_contents", "group_channel", "contents" };

    public static GameObject? ResolveTarget(GameObject caller, string s)
    {
        // Delegate to CommandHelpers for faithful dedup (handles #id/me/here/contents + loc fallback)
        return Commands.CommandHelpers.ResolveObject(caller, s);
    }

    // Known properties dispatch through ISettable (explicit switch on the
    // owning type: GameObject for the shared properties, Node/Channel/
    // Account/Script overrides for their extras). This helper only owns the
    // command-level concerns: the protected/move gates, the extras fallback,
    // and the target resolution.
    // NOTE vs the old code: exact-case flag spellings ("IsPc") used to slip past
    // IsProtected (which compares snake forms) and toggle flags. Flags are now
    // uniformly read-only through `set` (use the quell/build commands instead);
    // superuser-only narrowing for is_* is intentional hardening.

    // Faithful to Python hasattr/getattr: attribute lookup is case-SENSITIVE.
    // Exact property name first, then all-lowercase snake_case mapped to
    // PascalCase (is_pc -> IsPc). Mixed-case spellings (Is_Pc) do NOT
    // resolve: like Python's setattr creating a junk attribute, they fall
    // through to the extras store instead of hitting the real property.
    public static bool HasKnownProp(GameObject o, string attr) => o.IsKnownProperty(attr);

    // Canonical privilege names have no settable property (get-only IsBuilder/
    // IsSuperUser) but must still be guarded case-insensitively.
    private static readonly HashSet<string> ProtectedCanonical =
        new(StringComparer.OrdinalIgnoreCase) { "is_builder", "is_superuser" };

    // Case-insensitive lookup over the same entries: IsProtected compares
    // OrdinalIgnoreCase while the public set stays Ordinal (resolution
    // elsewhere is case-sensitive) — the comparers are not unified.
    private static readonly HashSet<string> ProtectedIgnoreCase =
        new(Protected.Concat(ProtectedCanonical), StringComparer.OrdinalIgnoreCase);

    // case-insensitive (frozen set is Ordinal like Python's frozenset, so
    // compare explicitly). Resolution stays case-sensitive (ISettable): unknown
    // spellings fall through to extras like Python's setattr junk attribute.
    public static bool IsProtected(string attr) =>
        attr.StartsWith("_", StringComparison.Ordinal) ||
        ProtectedIgnoreCase.Contains(attr);

    public static bool HasAttr(GameObject o, string attr)
    {
        if (HasKnownProp(o, attr)) return true;
        // Typed extra check (no _extra reflection).
        return o.HasExtra(attr);
    }

    public static void SetAttr(GameObject o, string attr, object? val)
    {
        if (o.TrySetProperty(attr, val, out var error))
            return;
        // A known-but-read-only name throws exactly like a get-only
        // property did (parameterless, like the old map path).
        if (error is not null) throw new InvalidOperationException();
        // Typed extra store. Unknown spellings land here like Python's
        // setattr junk attribute.
        JsonElement je = JsonSerializer.SerializeToElement(val);
        o.SetExtraJson(attr, je);
        return;
    }

    public static bool TryRemoveExtra(GameObject target, string attr)
    {
        // Typed (no _extra reflection, no dynamic).
        if (!target.HasExtra(attr)) return false;
        return target.TryRemoveExtraJson(attr);
    }
}
