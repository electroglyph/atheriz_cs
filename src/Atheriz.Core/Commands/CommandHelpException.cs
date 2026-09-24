namespace Atheriz.Core.Commands;

/// <summary>
/// Help-as-signal, not help-as-error: thrown for <c>--help</c> / print-help
/// paths that carry no diagnosis. Derives from <see cref="CommandError"/> so
/// existing catches keep working; <c>Command.Execute</c> catches it first and
/// sends a single help message instead of diagnosis-plus-help.
/// </summary>
public sealed class CommandHelpException : CommandError
{
    public CommandHelpException(string message) : base(message) { }
}
