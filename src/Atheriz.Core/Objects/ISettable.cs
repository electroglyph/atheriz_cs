namespace Atheriz.Core.Objects;

/// <summary>
/// Typed property setting for game objects. Known properties dispatch
/// through an explicit per-type switch (one arm per accepted spelling);
/// unknown names are not handled here — the caller falls through to the
/// extras store.
/// </summary>
public interface ISettable
{
    /// <summary>
    /// Tries to set the known property <paramref name="name"/> to
    /// <paramref name="value"/>. Returns true when the property was set.
    /// Returns false with a null <paramref name="error"/> for unknown
    /// names (the caller tries the extras store), or false with a reason
    /// for known-but-read-only names. Conversion failures throw the same
    /// exceptions the old type-based conversion path threw
    /// (<c>FormatException</c>/<c>OverflowException</c> with the same
    /// messages, <c>InvalidCastException</c> for unsettable types).
    /// </summary>
    bool TrySetProperty(string name, object? value, out string? error);
}
