namespace Atheriz.Core.Commands;

/// <summary>
/// Minimal caller abstraction shared by GameObject and network Connections.
/// Mirrors <c>Object|Connection</c> union in <c>atheriz/commands/base_cmd.py:Command</c>.
/// </summary>
public interface IMessageTarget
{
    void Msg(string text);
}

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

public interface ILagInfo
{
    bool IsLagged { get; }
    int Level { get; }
}
