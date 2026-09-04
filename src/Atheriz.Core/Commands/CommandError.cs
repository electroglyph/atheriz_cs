namespace Atheriz.Core.Commands;

/// <summary>
/// Raised when argument parsing fails or help is requested.
/// Mirrors <c>atheriz/commands/base_cmd.py:CommandError</c>.
/// </summary>
public sealed class CommandError : Exception
{
    public CommandError(string message) : base(message) { }
}
