using Atheriz.Core.Objects;

namespace Atheriz.Core.Plugins;

// Explicit hot-reload migration contract. A replacement type implementing
// IMigrateFrom<T> is constructed normally (public parameterless ctor: all
// field initializers and invariants run) and then pulls state from the live
// instance — replacing the legacy field-pairing copy, which bypasses the
// ctor and silently skips renamed fields. Types without the contract keep
// the legacy copy path (unknown game subclasses cannot be constructed any
// other way). A throwing MigrateFrom keeps the old instance live; the
// failure is logged, never half-applied.
public interface IMigrateFrom
{
    void MigrateFrom(GameObject old);
}

public interface IMigrateFrom<TOld> : IMigrateFrom where TOld : GameObject
{
    void MigrateFrom(TOld old);

    // Default forwarder so games implement only the typed method; the
    // reloader calls the untyped entry point with no reflection. The cast
    // is safe: the reloader only selects this interface when TOld is
    // assignable from the live instance's type.
    void IMigrateFrom.MigrateFrom(GameObject old) => MigrateFrom((TOld)old);
}
