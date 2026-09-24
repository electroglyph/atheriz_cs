namespace Atheriz.Core.Commands;

/// <summary>
/// Typed seam for "caller has a session" (F001). Implemented by
/// <c>GameObject</c>, <c>Session</c> (itself) and <c>BaseConnection</c> so
/// commands and menu code no longer need <c>dynamic</c>/reflection to reach
/// <c>caller.Session</c>. Exotic test doubles without a session fall back to
/// the legacy reflection path at each call site.
/// </summary>
public interface ISessionProvider
{
    Objects.Session? Session { get; }
}
