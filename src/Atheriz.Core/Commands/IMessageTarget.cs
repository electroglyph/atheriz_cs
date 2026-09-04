namespace Atheriz.Core.Commands;

/// <summary>
/// Minimal caller abstraction shared by GameObject and network Connections.
/// Mirrors <c>Object|Connection</c> union in <c>atheriz/commands/base_cmd.py:Command</c>.
/// </summary>
public interface IMessageTarget
{
    void Msg(string text);
}

public interface ILagInfo
{
    bool IsLagged { get; }
    int Level { get; }
}
