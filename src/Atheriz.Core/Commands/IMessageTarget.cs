using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands;

/// <summary>
/// Minimal caller abstraction shared by GameObject and network Connections.
/// Mirrors <c>Object|Connection</c> union in <c>atheriz/commands/base_cmd.py:Command</c>.
/// </summary>
public interface IMessageTarget
{
    void Msg(string text);

    /// <summary>
    /// Owning session when the caller has one (GameObject, BaseConnection);
    /// null for session-less shapes, which keeps the shared quiet-close a
    /// silent noop for them.
    /// </summary>
    Session? Session { get => null; }

    /// <summary>
    /// Closes the caller's connection. Noop unless the implementer owns one
    /// (BaseConnection overrides it).
    /// </summary>
    void Close() { }
}
